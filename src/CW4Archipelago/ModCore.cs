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

    private static string _lastScene = "";
    private static bool _autoConnectTried;

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
        Client.StateChanged += OnClientStateChanged;
        Log.LogInfo("ModCore initialized");
    }

    public static void Enqueue(Action action) => Dispatch.Enqueue(action);

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

    private static void OnClientStateChanged()
    {
        // Appliers are pull-based (they read State each Tick); only the menu
        // needs a push to repaint the status line.
        _menu.OnStateChanged();
    }

    private static void OnSceneChanged(string scene)
    {
        Log.LogInfo($"SCENE: '{scene}'");
        _units.OnSceneEnter(scene);
        if (scene == "Galaxy")
        {
            _menu.OnGalaxyEntered();
            if (Config.AutoConnect.Value && !_autoConnectTried && !Client.Connected)
            {
                _autoConnectTried = true;
                Log.LogInfo("AUTOCONNECT: attempting");
                Connect();
            }
        }
    }
}
