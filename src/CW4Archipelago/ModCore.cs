using System;
using System.Collections.Concurrent;
using BepInEx.Logging;
using CW4Archipelago.Appliers;
using UnityEngine.SceneManagement;

namespace CW4Archipelago;

/// <summary>
/// Plain static hub (IL2CPP-safe home for state and logic). Owns the AP client,
/// the main-thread dispatch queue, and the appliers. Driven by the thin
/// ModBehaviour shim.
/// </summary>
public static class ModCore
{
    public static ManualLogSource Log { get; private set; } = null!;
    public static ModConfig Config { get; private set; } = null!;
    public static ApClient Client { get; private set; } = null!;

    private static readonly ConcurrentQueue<Action> Dispatch = new();
    private static MenuUi _menu = null!;
    private static DebugChannel _debug = null!;
    private static UnitGate _units = null!;
    private static ErnGranter _erns = null!;
    private static LocationWatcher _locations = null!;
    private static TrackerView _tracker = null!;
    private static ApMessageBox _messageBox = null!;

    private static string _lastScene = "";

    public static string CurrentScene => _lastScene;

    public static void Init(ManualLogSource log, ModConfig config)
    {
        Log = log;
        Config = config;
        Client = new ApClient(log, Enqueue, StoreRoot());
        _menu = new MenuUi();
        _debug = new DebugChannel();
        _units = new UnitGate();
        _erns = new ErnGranter();
        _locations = new LocationWatcher();
        _tracker = new TrackerView();
        _messageBox = new ApMessageBox();
        Client.StateChanged += OnClientStateChanged;
        Client.LineReceived += (spans, relevant) => AppendLine(spans, relevant);
        Log.LogInfo("ModCore initialized");
    }

    public static void Enqueue(Action action) => Dispatch.Enqueue(action);

    /// <summary>Rolling history of colored message lines (survives scene changes).</summary>
    public static readonly System.Collections.Generic.List<Appliers.MsgLine> MessageHistory = new();
    private const int MaxHistory = 200;

    /// <summary>Append a colored line to the history and the live box.</summary>
    public static void AppendLine(System.Collections.Generic.List<Appliers.MsgSpan> spans, bool relevant = true)
    {
        MessageHistory.Add(new Appliers.MsgLine(spans, relevant));
        while (MessageHistory.Count > MaxHistory) MessageHistory.RemoveAt(0);
        _messageBox.AppendLine(spans, relevant);
    }

    /// <summary>Append a single-color line (connection status, debug).</summary>
    public static void EnqueueToast(string message)
        => AppendLine(new System.Collections.Generic.List<Appliers.MsgSpan> { new Appliers.MsgSpan(message, "C0C8D4") }, true);

    /// <summary>Toggle the message box between relevant-only and show-all, and
    /// re-render history so the change is retroactive (used by the debug channel;
    /// the in-box button toggles directly).</summary>
    public static void SetShowAll(bool on)
    {
        Appliers.ApMessageBox.ShowAll = on;
        _messageBox.Rerender();
        Log.LogInfo($"MSGBOX SHOWALL={(on ? 1 : 0)} (debug)");
    }

    // Per-slot cache lives next to the game's own save data.
    private static string StoreRoot()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return System.IO.Path.Combine(docs, "My Games", "creeperworld4", "archipelago");
    }

    public static void Connect()
        => Client.Connect(Config.Host.Value, Config.Port.Value, Config.Slot.Value, Config.Password.Value);

    public static void SafeTick()
    {
        try { Tick(); }
        catch (Exception e) { Log.LogError($"tick failed: {e.Message}"); }
    }

    public static void SafeLateTick()
    {
        try { _tracker.ApplyTints(); }
        catch (Exception e) { Log.LogError($"late tick failed: {e.Message}"); }
        try { _messageBox.LateTick(_lastScene); }
        catch (Exception e) { Log.LogError($"toast tick failed: {e.Message}"); }
    }

    private static void Tick()
    {
        while (Dispatch.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception e) { Log.LogError($"dispatched action failed: {e.Message}"); }
        }

        var scene = SceneManager.GetActiveScene().name;
        if (scene != _lastScene)
        {
            _lastScene = scene;
            OnSceneChanged(scene);
        }

        _menu.Tick(scene);
        _units.Tick();
        _erns.Tick();
        _locations.Tick();
        TrapEffects.Tick();   // restores a timed trap:emit burst

        // Periodic pending-check flush safety (~ every 5s at 60fps).
        if (++_retryCountdown >= 300)
        {
            _retryCountdown = 0;
            Client.RetryPendingIfConnected();
        }

        if (Config.DebugCommands.Value)
            _debug.Tick();
    }

    private static int _retryCountdown;

    private static ConnectionStatus _lastStatus = ConnectionStatus.Disconnected;

    private static void OnClientStateChanged()
    {
        // Appliers are pull-based (they read State each Tick); the menu needs a
        // push to repaint its status line, and a status TRANSITION becomes an
        // in-mission toast so the player sees drops/reconnects without leaving.
        _menu.OnStateChanged();

        var status = Client.Status;
        if (status != _lastStatus)
        {
            _lastStatus = status;
            string? toast = status switch
            {
                ConnectionStatus.Connected => "Archipelago: connected",
                ConnectionStatus.Connecting => "Archipelago: connecting...",
                ConnectionStatus.Disconnected => "Archipelago: " + Client.StatusText,
                ConnectionStatus.Failed => "Archipelago: " + Client.StatusText,
                _ => null,
            };
            if (toast != null)
            {
                EnqueueToast(toast);
                Log.LogInfo($"STATUS TOAST: {toast}");
            }
        }
    }

    private static void OnSceneChanged(string scene)
    {
        Log.LogInfo($"SCENE: '{scene}'");
        _units.OnSceneEnter(scene);
        if (scene == "Galaxy")
        {
            _menu.OnGalaxyEntered();
            // Re-attempt on EVERY return to the menu (not just the first),
            // so "save, quit to menu, reconnect" re-syncs: pending checks
            // flush and received items are pulled on the new connection.
            if (Config.AutoConnect.Value && !Client.Connected && HasConnectionInfo())
            {
                Log.LogInfo("AUTOCONNECT: attempting (menu entry)");
                Connect();
            }
        }
        if (scene == "Game" && Client.Connected)
        {
            // Seed guard: the live saves/farsite is stamped for the connected
            // seed/slot on login. A mismatch means a save set from another seed
            // (offline play / manual file move) - warn rather than corrupt it.
            try
            {
                if (!Appliers.SaveArchiver.SeedMatches(Client.State.Seed, Client.State.Slot))
                    Log.LogWarning($"SEED GUARD: saves/farsite not stamped for connected seed='{Client.State.Seed}' slot='{Client.State.Slot}' - possible wrong-seed save");
                else
                    Log.LogInfo("SEED GUARD: active saves match connected seed/slot");
            }
            catch { }
        }
    }

    private static bool HasConnectionInfo()
        => !string.IsNullOrWhiteSpace(Config.Host.Value) && !string.IsNullOrWhiteSpace(Config.Slot.Value);
}
