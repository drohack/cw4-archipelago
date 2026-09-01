using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using Archipelago.MultiClient.Net.MessageLog.Messages;
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

    private SlotState _state = new();

    /// <summary>The live slot state.
    ///
    /// Assigning it re-wires the change events, because connecting REPLACES the
    /// object rather than mutating it - so anything subscribed to the old
    /// instance would be left listening to a corpse. Forwarding the state's own
    /// events to StateChanged means one subscription serves every consumer and
    /// covers every mutation.
    ///
    /// This matters: StateChanged used to be raised only when an item arrived or
    /// the connection status moved, so a location CHECK changed nothing that any
    /// listener could see. The map's colouring got away with it only because it
    /// polled every frame.</summary>
    public SlotState State
    {
        get => _state;
        private set
        {
            if (ReferenceEquals(_state, value))
                return;
            if (_state != null)
            {
                _state.ItemsChanged -= RaiseChanged;
                _state.LocationsChanged -= RaiseChanged;
            }
            _state = value;
            if (_state != null)
            {
                _state.ItemsChanged += RaiseChanged;
                _state.LocationsChanged += RaiseChanged;
            }
            RaiseChanged();
        }
    }
    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;
    public string StatusText { get; private set; } = "not connected";
    public bool Connected => Status == ConnectionStatus.Connected;

    public event Action? StateChanged;         // raised on the main thread after any change
    public event Action<string>? MessageReceived;   // AP server log line (plain text), main thread
    public event Action<System.Collections.Generic.List<Appliers.MsgSpan>, bool>? LineReceived;  // colored parts + relevance, main thread

    /// <summary>Wire the initial state's events; the property setter handles
    /// every later replacement.</summary>
    private void WireInitialState()
    {
        _state.ItemsChanged += RaiseChanged;
        _state.LocationsChanged += RaiseChanged;
    }

    public ApClient(ManualLogSource log, Action<Action> dispatch, string storeRoot)
    {
        _log = log;
        _dispatch = dispatch;
        _store = new SlotStore(storeRoot);
        WireInitialState();
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
        _retryCount = 0;   // a manual/menu connect re-arms the retry budget
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
                // Any failure is retryable up to the cap: server-unreachable
                // comes back here as a LoginFailure ("Connection timed out"),
                // not an exception, and a bad slot/password simply exhausts the
                // 3 tries and then stops with the error shown.
                var errors = result is LoginFailure f ? string.Join("; ", f.Errors) : "unknown error";
                _dispatch(() =>
                {
                    SetStatus(ConnectionStatus.Failed, $"login failed: {errors}");
                    ScheduleReconnect();
                });
            }
        }
        catch (Exception e)
        {
            _dispatch(() =>
            {
                SetStatus(ConnectionStatus.Failed, $"connect error: {e.Message}");
                ScheduleReconnect();
            });
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

        // Isolate the game's Farsite saves for this slot before anything reads
        // them, so a save from another seed can't appear in this one's load list.
        Appliers.SaveArchiver.SwitchTo(seed, slot, m => _log.LogInfo(m));
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
        session.MessageLog.OnMessageReceived += OnServerMessage;
        session.Socket.SocketClosed += _ => _dispatch(() => OnSocketClosed());

        _retryCount = 0;   // healthy connection re-arms the retry budget
        SetStatus(ConnectionStatus.Connected, $"connected as {slot} (seed {seed})");
        _log.LogInfo($"AP CONNECTED slot='{slot}' seed='{seed}' locations={allLocations.Count} received={received.Count}");
        // The seed's shape, in one greppable line. Which missions start unlocked
        // is decided per seed and was previously invisible: a player asking "why
        // can I only play these two?" had no answer, and a test had no choice but
        // to hard-code a guess - which is exactly how apbattery2 came to assert
        // that story1 is always a starter, years after that stopped being true.
        _log.LogInfo(
            $"AP SEED SHAPE: starters=[{string.Join(",", hints.StarterMissions)}] " +
            $"missionsForFinale={hints.MissionsForFinale}");

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

    private void OnServerMessage(Archipelago.MultiClient.Net.MessageLog.Messages.LogMessage message)
    {
        string text;
        var spans = new System.Collections.Generic.List<Appliers.MsgSpan>();
        try
        {
            text = message.ToString();
            foreach (var part in message.Parts)
            {
                string hex;
                try { var c = part.Color; hex = $"{c.R:X2}{c.G:X2}{c.B:X2}"; }
                catch { hex = "FFFFFF"; }
                spans.Add(new Appliers.MsgSpan(part.Text ?? "", hex));
            }
        }
        catch { return; }
        if (spans.Count == 0)
            return;
        bool relevant;
        try { var (k, r) = Classify(message); relevant = Core.MessageRelevance.IsRelevant(k, r); }
        catch { relevant = true; }
        _dispatch(() =>
        {
            _log.LogInfo($"AP MESSAGE: relevant={(relevant ? 1 : 0)} {text}");
            MessageReceived?.Invoke(text);
            LineReceived?.Invoke(spans, relevant);
        });
    }

    // Map a server LogMessage to (kind, related-to-active-player) so the pure
    // Core.MessageRelevance predicate can decide default visibility. Derived
    // item types are matched before their ItemSendLogMessage base.
    private static (Core.ApMessageKind kind, bool related) Classify(LogMessage m)
    {
        bool related =
            m is ItemSendLogMessage ism ? ism.IsRelatedToActivePlayer :
            m is PlayerSpecificLogMessage psm ? psm.IsRelatedToActivePlayer : false;
        Core.ApMessageKind kind = m switch
        {
            HintItemSendLogMessage => Core.ApMessageKind.Hint,
            ItemCheatLogMessage => Core.ApMessageKind.ItemCheat,
            ItemSendLogMessage => Core.ApMessageKind.ItemSend,
            ChatLogMessage => Core.ApMessageKind.Chat,
            ServerChatLogMessage => Core.ApMessageKind.ServerChat,
            AdminCommandResultLogMessage => Core.ApMessageKind.AdminCommandResult,
            CommandResultLogMessage => Core.ApMessageKind.CommandResult,
            CollectLogMessage => Core.ApMessageKind.Collect,
            ReleaseLogMessage => Core.ApMessageKind.Release,
            GoalLogMessage => Core.ApMessageKind.Goal,
            JoinLogMessage => Core.ApMessageKind.Join,
            LeaveLogMessage => Core.ApMessageKind.Leave,
            TagsChangedLogMessage => Core.ApMessageKind.TagsChanged,
            CountdownLogMessage => Core.ApMessageKind.Countdown,
            TutorialLogMessage => Core.ApMessageKind.Tutorial,
            _ => Core.ApMessageKind.Other,
        };
        return (kind, related);
    }

    /// <summary>Send a chat line (or a leading-! server command) to the server.
    /// No-op when not connected. The server echoes it back via OnServerMessage.</summary>
    public void Say(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var s = _session;
        if (s == null || !Connected) { _log.LogInfo("SAY ignored (not connected)"); return; }
        try { s.Say(text); _log.LogInfo($"SAY: {text}"); }
        catch (Exception e) { _log.LogWarning($"Say failed: {e.Message}"); }
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

    // Auto-reconnect: up to 3 attempts (5s, 10s, 15s apart), then stop. Works
    // in-game and at the menu. After giving up, the player can save, quit to
    // the menu, and reconnect (returning to the menu re-arms a fresh budget).
    // Reset on a successful connect and on a manual/menu connect.
    private const int MaxRetries = 3;
    private int _retryCount;
    private bool _reconnecting;

    private void ScheduleReconnect()
    {
        if (_reconnecting) return;
        if (_retryCount >= MaxRetries)
        {
            SetStatus(ConnectionStatus.Disconnected,
                "reconnect failed after 3 tries - save and return to the menu to retry");
            return;
        }
        _reconnecting = true;
        _retryCount++;
        int attempt = _retryCount;
        int delay = 5 * attempt;   // 5s, 10s, 15s
        var host = _lastHost; var port = _lastPort; var slot = _lastSlot; var pass = _lastPass;
        _log.LogInfo($"AP RECONNECT: attempt {attempt}/{MaxRetries} in {delay}s");
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
            try
            {
                _session.Locations.CompleteLocationChecks(ids.ToArray());
                _log.LogInfo($"AP CHECKS SENT: {string.Join(", ", names)}");
            }
            catch (Exception e)
            {
                // Send failed on a dead socket - re-queue everything and treat
                // as a drop so the bounded auto-reconnect kicks in.
                foreach (var n in names)
                    if (!State.PendingChecks.Contains(n))
                        State.PendingChecks.Add(n);
                _log.LogWarning($"AP CHECKS SEND FAILED (re-queued, handling as drop): {e.Message}");
                Persist();
                OnSocketClosed();
                return;
            }
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

    /// <summary>Periodic safety (called on a timer): retry any queued checks
    /// while connected, and detect a dropped socket the close event missed.</summary>
    public void RetryPendingIfConnected()
    {
        if (!Connected || _session == null)
            return;

        // Detect a drop the SocketClosed event did not surface (an ungraceful
        // server death can leave the websocket without firing a close). If the
        // socket reports not-connected while we still think we are, handle it
        // as a drop (which schedules the bounded auto-reconnect).
        bool socketUp = true;
        try { socketUp = _session.Socket.Connected; } catch { socketUp = false; }
        if (!socketUp)
        {
            _log.LogInfo("AP: socket reports disconnected (missed close) - handling as drop");
            OnSocketClosed();
            return;
        }

        if (State.PendingChecks.Count > 0)
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
