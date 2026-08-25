using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using BepInEx.Logging;
using CW4Archipelago.Core;
using Newtonsoft.Json;

namespace CW4Archipelago;

public enum ConnectionStatus { Disconnected, Connecting, Connected, Failed }

/// <summary>
/// Owns the MultiClient.Net session and translates it into SlotState changes.
/// All socket-thread callbacks are marshalled to the game main thread through
/// the dispatch delegate; nothing here touches Unity.
/// </summary>
public sealed class ApClient
{
    public const string Game = "Creeper World 4";
    private static readonly Version ApVersion = new(0, 5, 0);

    private readonly ManualLogSource _log;
    private readonly Action<Action> _dispatch;
    private readonly SlotStore _store;

    private ArchipelagoSession? _session;

    public SlotState State { get; private set; } = new();
    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;
    public string StatusText { get; private set; } = "not connected";
    public bool Connected => Status == ConnectionStatus.Connected;

    public event Action? StateChanged;   // raised on the main thread after any change

    public ApClient(ManualLogSource log, Action<Action> dispatch, string storeRoot)
    {
        _log = log;
        _dispatch = dispatch;
        _store = new SlotStore(storeRoot);
    }

    private void Persist()
    {
        try { if (!string.IsNullOrEmpty(State.Seed)) _store.Save(State); }
        catch (Exception e) { _log.LogWarning($"slot cache save failed: {e.Message}"); }
    }

    private string _lastHost = "";
    private int _lastPort;
    private string _lastSlot = "";
    private string _lastPass = "";

    // Serializes all connect attempts (manual and auto-reconnect) so they can
    // never run concurrently and race the pending-check state.
    private volatile bool _connectInFlight;

    public void Connect(string host, int port, string slot, string password)
    {
        if (_connectInFlight || Status == ConnectionStatus.Connecting)
            return;
        _lastHost = host; _lastPort = port; _lastSlot = slot; _lastPass = password;
        _manualDisconnect = false;
        _retryDelay = 5;   // fresh manual connect resets the backoff
        SetStatus(ConnectionStatus.Connecting, $"connecting to {host}:{port} as {slot}...");
        Task.Run(() => ConnectBlocking(host, port, slot, password));
    }

    private void ConnectBlocking(string host, int port, string slot, string password)
    {
        if (_connectInFlight)
            return;
        _connectInFlight = true;
        try
        {
            var session = ArchipelagoSessionFactory.CreateSession(host, port);
            var result = session.TryConnectAndLogin(
                Game, slot, ItemsHandlingFlags.AllItems, ApVersion,
                tags: null, uuid: null,
                password: string.IsNullOrEmpty(password) ? null : password,
                requestSlotData: true);

            if (result is LoginSuccessful success)
            {
                _dispatch(() => OnLoginSuccess(session, slot, success));
            }
            else
            {
                var errors = result is LoginFailure f ? string.Join("; ", f.Errors) : "unknown error";
                _dispatch(() => SetStatus(ConnectionStatus.Failed, $"login failed: {errors}"));
            }
        }
        catch (Exception e)
        {
            _dispatch(() => SetStatus(ConnectionStatus.Failed, $"connect error: {e.Message}"));
        }
        finally
        {
            _connectInFlight = false;
        }
    }

    private void OnLoginSuccess(ArchipelagoSession session, string slot, LoginSuccessful success)
    {
        _session = session;

        var seed = session.RoomState.Seed ?? "";
        var hintsJson = JsonConvert.SerializeObject(success.SlotData ?? new Dictionary<string, object>());
        var hints = SlotData.FromJson(hintsJson);

        var allLocations = session.Locations.AllLocations
            .Select(id => session.Locations.GetLocationNameFromId(id, Game))
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();
        var checkedNames = session.Locations.AllLocationsChecked
            .Select(id => session.Locations.GetLocationNameFromId(id, Game))
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();
        var received = session.Items.AllItemsReceived.Select(i => i.ItemName).ToList();

        // Only the server's AllLocationsChecked is authoritative. Everything we
        // still owe the server is our run's pending plus any cached pending -
        // these must be (re)sent even though we display them as checked, so we
        // must NOT fold them into the server-acknowledged set.
        var serverChecked = new HashSet<string>(checkedNames);
        var owed = new List<string>();
        if (State.Slot == slot)
            owed.AddRange(State.PendingChecks);
        var cached = _store.Load(seed, slot);
        if (cached != null)
            owed.AddRange(cached.PendingChecks);
        owed = owed.Distinct().Where(l => !serverChecked.Contains(l)).ToList();

        var state = new SlotState { Seed = seed, Slot = slot, Hints = hints };
        state.SetAllLocations(allLocations);
        state.ApplyReceivedItems(received);
        state.ReconcileChecked(serverChecked);
        foreach (var loc in owed)
        {
            state.CheckedLocations.Add(loc);        // display it as checked
            if (!state.PendingChecks.Contains(loc)) // and still owe it to the server
                state.PendingChecks.Add(loc);
        }
        State = state;

        session.Items.ItemReceived += OnItemReceived;
        session.Socket.SocketClosed += _ => _dispatch(() => OnSocketClosed());

        _retryDelay = 5;   // healthy connection resets backoff
        SetStatus(ConnectionStatus.Connected, $"connected as {slot} (seed {seed})");
        _log.LogInfo($"AP CONNECTED slot='{slot}' seed='{seed}' locations={allLocations.Count} received={received.Count}");

        FlushPending();
        Persist();
    }

    private void OnItemReceived(ReceivedItemsHelper helper)
    {
        // Drain on the socket thread into a local list, apply on the main thread.
        var names = new List<string>();
        while (helper.Any())
            names.Add(helper.DequeueItem().ItemName);
        if (names.Count == 0)
            return;
        _dispatch(() =>
        {
            foreach (var n in names)
            {
                State.ReceiveItem(n);
                _log.LogInfo($"AP ITEM RECEIVED: {n}");
            }
            Persist();
            RaiseChanged();
        });
    }

    private void OnSocketClosed()
    {
        session_ItemsUnsubscribe();
        if (_manualDisconnect)
        {
            _manualDisconnect = false;
            return;   // intentional disconnect already reported status
        }
        SetStatus(ConnectionStatus.Disconnected, "disconnected - will retry");
        ScheduleReconnect();
    }

    // Reconnect backoff: 5s, 10s, 20s, capped at 60s. Reset on success.
    private int _retryDelay = 5;
    private bool _reconnecting;

    private void ScheduleReconnect()
    {
        if (_reconnecting) return;
        _reconnecting = true;
        var host = _lastHost; var port = _lastPort; var slot = _lastSlot; var pass = _lastPass;
        var delay = _retryDelay;
        _retryDelay = System.Math.Min(_retryDelay * 2, 60);
        Task.Run(async () =>
        {
            await Task.Delay(delay * 1000);
            _reconnecting = false;
            if (Status != ConnectionStatus.Connected)
                ConnectBlocking(host, port, slot, pass);
        });
    }

    private void session_ItemsUnsubscribe()
    {
        try { if (_session != null) _session.Items.ItemReceived -= OnItemReceived; } catch { }
    }

    /// <summary>Send checks for the named locations (and queue any that fail).</summary>
    public void SendChecks(IEnumerable<string> locationNames)
    {
        var names = locationNames.Distinct().ToList();
        if (names.Count == 0)
            return;
        if (!Connected || _session == null)
        {
            foreach (var n in names)
                if (!State.PendingChecks.Contains(n))
                    State.PendingChecks.Add(n);
            _log.LogInfo($"AP CHECKS QUEUED (offline): {string.Join(", ", names)}");
            Persist();
            return;
        }
        var ids = new List<long>();
        foreach (var n in names)
        {
            long id = 0;
            try { id = _session.Locations.GetLocationIdFromName(Game, n); } catch { }
            if (id > 0)
            {
                ids.Add(id);
            }
            else
            {
                // Could not resolve (name mismatch, datapackage not ready) -
                // keep it queued rather than dropping it.
                if (!State.PendingChecks.Contains(n))
                    State.PendingChecks.Add(n);
                _log.LogWarning($"AP CHECK UNRESOLVED (re-queued): '{n}'");
            }
        }
        if (ids.Count > 0)
        {
            _session.Locations.CompleteLocationChecks(ids.ToArray());
            _log.LogInfo($"AP CHECKS SENT: {string.Join(", ", names)}");
        }
        Persist();
    }

    public void SendGoal()
    {
        if (Connected && _session != null)
        {
            _session.SetGoalAchieved();
            State.GoalPending = false;
            _log.LogInfo("AP GOAL ACHIEVED sent");
        }
        else
        {
            State.GoalPending = true;
        }
    }

    private void FlushPending()
    {
        var pending = State.TakePendingChecks();
        if (pending.Count > 0)
        {
            SendChecks(pending);
            _log.LogInfo($"AP FLUSHED {pending.Count} queued check(s)");
        }
        if (State.GoalPending)
            SendGoal();
    }

    /// <summary>Periodic safety: retry any queued checks while connected.
    /// Covers a check that could not resolve at flush time (e.g. a datapackage
    /// not-yet-ready race) so nothing stays stuck in the queue.</summary>
    public void RetryPendingIfConnected()
    {
        if (Connected && _session != null && State.PendingChecks.Count > 0)
            FlushPending();
    }

    private bool _manualDisconnect;

    public void Disconnect()
    {
        _manualDisconnect = true;   // suppress auto-reconnect for an intentional disconnect
        session_ItemsUnsubscribe();
        try { _session?.Socket.DisconnectAsync(); } catch { }
        _session = null;
        SetStatus(ConnectionStatus.Disconnected, "disconnected");
    }

    private void SetStatus(ConnectionStatus status, string text)
    {
        Status = status;
        StatusText = text;
        _log.LogInfo($"AP STATUS: {text}");
        RaiseChanged();
    }

    private void RaiseChanged() => StateChanged?.Invoke();
}
