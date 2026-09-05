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
        LoadCachedSlot();
    }

    /// <summary>Start on the last slot played, before any server is reached.
    ///
    /// Without this the game came up with an empty state whenever the server was
    /// unreachable, which means EVERY mission locked and nothing playable - even
    /// with a complete cache of the slot sitting on disk. A host that is down for
    /// ten minutes should not be a ten-minute ban from your own campaign.
    ///
    /// This is the same footing as a mid-session drop, which was always allowed:
    /// you keep playing, checks queue, and the queue flushes on the next
    /// connection. Archipelago requires that much - "the client must send those
    /// location checks on connection so that they are not permanently lost"
    /// (docs/adding games.md) - so progress is pushed, never reverted.
    ///
    /// Connecting to a DIFFERENT seed does not fold this progress in: the
    /// reconcile keys on (seed, slot) and leaves the other seed's cache alone,
    /// still owed the next time that seed is joined.</summary>
    private void LoadCachedSlot()
    {
        try
        {
            var cached = _store.LoadLast();
            if (cached == null)
                return;
            State = cached;
            var owed = cached.PendingChecks.Count;
            SetStatus(ConnectionStatus.Disconnected,
                $"offline - playing cached slot {cached.Slot}"
                + (owed > 0 ? $" ({owed} check(s) queued)" : ""));
            _log.LogInfo(
                $"AP OFFLINE: loaded cached slot='{cached.Slot}' seed='{cached.Seed}' " +
                $"items={cached.ReceivedItems.Count} checked={cached.CheckedLocations.Count} pending={owed}");
        }
        catch (Exception e)
        {
            // Never let a bad cache stop the game from starting.
            _log.LogWarning($"cached slot load failed, starting empty: {e.Message}");
        }
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

    /// <summary>Connect, superseding anything already in flight.
    ///
    /// This used to return early when an attempt was in flight or the status was
    /// Connecting, which silently DID NOTHING. With the bounded three-try retry
    /// that was rare; once the retry became unbounded there is nearly always an
    /// attempt running or pending, each taking about 40 seconds to time out, so
    /// pressing CONNECT usually landed in that window and looked dead. A button
    /// that ignores a click is worse than one that fails.
    ///
    /// Every attempt carries a generation. Bumping it makes the in-flight one
    /// stale, so when it finally returns it cannot set a status, schedule a
    /// retry, or install a session behind a newer request or a deliberate
    /// disconnect.</summary>
    public void Connect(string host, int port, string slot, string password)
    {
        _lastHost = host; _lastPort = port; _lastSlot = slot; _lastPass = password;
        _manualDisconnect = false;
        _retryCount = 0;   // a manual/menu connect re-arms the retry budget
        int gen = ++_connectGen;
        SetStatus(ConnectionStatus.Connecting, $"connecting to {host}:{port} as {slot}...");
        Task.Run(() => ConnectBlocking(gen, host, port, slot, password));
    }

    /// <summary>Bumped by every connect and every disconnect. An attempt whose
    /// generation is stale must have no effect.</summary>
    private int _connectGen;
    private bool IsStale(int gen) => gen != _connectGen;

    private void ConnectBlocking(int gen, string host, int port, string slot, string password)
    {
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
                _dispatch(() =>
                {
                    if (IsStale(gen))
                    {
                        // A newer request or a disconnect happened while this was
                        // negotiating. Close it rather than installing a session
                        // nobody asked for any more.
                        _log.LogInfo("AP: discarding a superseded connection");
                        try { session.Socket.DisconnectAsync(); } catch { }
                        return;
                    }
                    OnLoginSuccess(session, slot, success);
                });
            }
            else
            {
                // Two very different failures arrive down this one path, and now
                // that the retry is unbounded they have to be told apart.
                //
                // A REFUSAL is the server answering: wrong slot name, wrong
                // password, wrong game, incompatible version. Retrying cannot
                // help - the answer will be the same in a minute - and the
                // reference client does not auto-retry one either. Show it and
                // stop, so a typo says so instead of spinning forever.
                //
                // Anything else is the transport: unreachable host, timeout, DNS.
                // Those are exactly what the backoff exists for. A server that is
                // down comes back here as a LoginFailure ("Connection timed
                // out"), not an exception, which is why this is not a catch.
                var failure = result as LoginFailure;
                var errors = failure != null ? string.Join("; ", failure.Errors) : "unknown error";
                var refused = failure?.ErrorCodes is { Length: > 0 };
                _dispatch(() =>
                {
                    if (IsStale(gen)) return;
                    if (refused)
                    {
                        SetStatus(ConnectionStatus.Failed, $"login refused: {errors}");
                        _log.LogWarning($"AP LOGIN REFUSED (not retrying): {errors}");
                        return;
                    }
                    SetStatus(ConnectionStatus.Failed, $"cannot reach server: {errors}");
                    ScheduleReconnect();
                });
            }
        }
        catch (Exception e)
        {
            _dispatch(() =>
            {
                if (IsStale(gen)) return;
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

        // Only the server's AllLocationsChecked is authoritative; what we still
        // owe it, what goal we owe it, and how far the trap mark has advanced
        // are ours to carry. That decision is pure logic and lives in Core,
        // where it is tested - it was inline here and wrong three ways at once.
        var state = SessionReconcile.OnConnected(
            State, _store.Load(seed, slot),
            seed, slot, hints, allLocations, received, checkedNames);
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
            // Intentional disconnect; status was already reported. Deliberately
            // does NOT clear the flag: the close event arrives more than once
            // per disconnect, and clearing it here let the SECOND arrival fall
            // through to "disconnected - will retry" and reconnect a few
            // seconds after the player pressed DISCONNECT. Measured in
            // tools/offline-test.sh, which caught it because a manual
            // disconnect silently became a connection again mid-test.
            //
            // Only Connect() clears it, which is the same rule CommonClient uses
            // for disconnected_intentionally: a deliberate disconnect stays
            // deliberate until the player asks to connect again.
            return;
        SetStatus(ConnectionStatus.Disconnected, "disconnected - will retry");
        ScheduleReconnect();
    }

    // Auto-reconnect: keep trying, backing off 5, 10, 20, 40 then 60 seconds,
    // and stop only on a deliberate disconnect. Works in-game and at the menu.
    // The attempt counter resets on a successful connect and on a manual or
    // menu connect, so the backoff starts short again after each real drop.
    private const int MaxRetryDelaySeconds = 60;
    private int _retryCount;
    private bool _reconnecting;

    /// <summary>Retry forever, with exponential backoff capped at a minute.
    ///
    /// This used to stop after three tries at 5, 10 and 15 seconds - so a host
    /// restart that took two minutes left the player disconnected until they
    /// noticed and pressed CONNECT. Archipelago's reference client
    /// (CommonClient.py) retries indefinitely, doubling the delay each time and
    /// stopping only on a deliberate disconnect; its hard requirement is simply
    /// "reconnect if the connection is unstable and lost while playing".
    ///
    /// The cap is ours: pure doubling reaches an hour by attempt twelve, which is
    /// indistinguishable from having given up. Sixty seconds keeps it quiet
    /// without going to sleep. A manual disconnect stops it dead
    /// (_manualDisconnect), and any manual or menu connect re-arms it.</summary>
    private void ScheduleReconnect()
    {
        if (_reconnecting) return;
        if (_manualDisconnect) return;
        _reconnecting = true;
        _retryCount++;
        int attempt = _retryCount;
        // 5, 10, 20, 40, then 60 for as long as it takes.
        int delay = attempt >= 5 ? MaxRetryDelaySeconds
            : Math.Min(MaxRetryDelaySeconds, 5 * (1 << (attempt - 1)));
        var host = _lastHost; var port = _lastPort; var slot = _lastSlot; var pass = _lastPass;
        int gen = _connectGen;
        _log.LogInfo($"AP RECONNECT: attempt {attempt} in {delay}s");
        Task.Run(async () =>
        {
            await Task.Delay(delay * 1000);
            _reconnecting = false;
            // A manual connect or a disconnect during the wait wins.
            if (IsStale(gen) || _manualDisconnect) return;
            if (Status != ConnectionStatus.Connected)
                ConnectBlocking(gen, host, port, slot, pass);
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
            // Queued, exactly like a check made offline, and it is replayed by
            // the reconcile on the next connect. Logged because it was silent
            // here AND silently dropped on reconnect - so a player who beat the
            // finale with the server down had no way to tell either had
            // happened.
            State.GoalPending = true;
            _log.LogInfo("AP GOAL QUEUED (offline) - will be sent on reconnect");
            Persist();
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
        _connectGen++;              // and cancel any attempt still negotiating
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
