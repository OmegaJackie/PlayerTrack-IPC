using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FluentDapperLite.Runner;
using PlayerTrack.API;
using PlayerTrack.Domain;
using PlayerTrack.Extensions;
using PlayerTrack.Handler;
using PlayerTrack.Infrastructure;
using PlayerTrack.Resource;
using PlayerTrack.Windows;

namespace PlayerTrack;

public class Plugin : IDalamudPlugin
{
    [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] public static ICondition ConditionHandler { get; private set; } = null!;
    [PluginService] public static IObjectTable ObjectCollection { get; private set; } = null!;
    [PluginService] public static IClientState ClientStateHandler { get; private set; } = null!;
    [PluginService] public static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] public static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] public static IChatGui ChatGuiHandler { get; private set; } = null!;
    [PluginService] public static IFramework GameFramework { get; private set; } = null!;
    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static IPluginLog PluginLog { get; set; } = null!;
    [PluginService] public static IGameInteropProvider HookManager { get; set; } = null!;
    [PluginService] public static IContextMenu ContextMenu { get; set; } = null!;
    [PluginService] public static INamePlateGui NamePlateGuiHandler { get; set; } = null!;
    [PluginService] public static IDataManager DataManager { get; set; } = null!;
    [PluginService] public static ITargetManager TargetManager { get; set; } = null!;
    [PluginService] public static IAddonLifecycle AddonLifecycle { get; set; } = null!;
    [PluginService] public static IPartyList PartyList { get; set; } = null!;

    public static WindowManager WindowManager { get; set; } = null!;

    public static SocialListHandler SocialListHandler { get; set; } = null!;
    public static PlayerLocationManager PlayerLocationManager { get; set; } = null!;

    /// <summary>
    /// Set to true once <see cref="Dispose"/> has begun.  RunPostStartup is a
    /// fire-and-forget task that can still be scheduled when Dalamud unloads
    /// the plugin (e.g. profile state flip mid-startup); checking this flag
    /// between steps prevents downstream services from touching the disposed
    /// SQLiteConnection.
    /// </summary>
    private static volatile bool _isDisposing;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        _isDisposing = false;
        if (pluginInterface.IsDifferentVersionLoaded())
        {
            PluginLog.Error("Terminating plugin since another version of PlayerTrack is loaded.");
            return;
        }

        var isDatabaseLoadedSuccessfully = LoadDatabase();
        if (!isDatabaseLoadedSuccessfully)
            return;

        LanguageChanged(PluginInterface.UiLanguage);

        WindowManager = new WindowManager();
        SocialListHandler = new SocialListHandler();
        PlayerLocationManager = new PlayerLocationManager();

        RepositoryContext.Initialize(PluginInterface.GetPluginConfigDirectory());
        ServiceContext.Initialize();
        RunPostStartup();

        PluginInterface.LanguageChanged += LanguageChanged;
    }

    private PlayerTrackProvider? PlayerTrackProvider { get; set; }

    public void Dispose()
    {
        PluginLog.Verbose("Entering Plugin.Dispose()");
        _isDisposing = true;
        GC.SuppressFinalize(this);
        try
        {
            PlayerTrackProvider?.Dispose();
            PartyMonitor.Dispose();
            BioScraper.Dispose();
            HousingProvider.Dispose();
            PlateWatcher.Dispose();
            EncounterWatcher.Dispose();
            CommandHandler.Dispose();
            NameplateHandler.Dispose();
            EventDispatcher.Dispose();
            ContextMenuHandler.Dispose();
            GuiController.Dispose();
            ServiceContext.Dispose();
            RepositoryContext.Dispose();
            PlayerLocationManager.Dispose();
            SocialListHandler.Dispose();
            WindowManager.Dispose();

            PluginInterface.LanguageChanged -= LanguageChanged;
        }
        catch (Exception ex)
        {
            PluginLog.Error(ex, "Failed to dispose plugin.");
        }
    }

    /// <summary>
    /// Sets the language to be used for loc.
    /// </summary>
    private void LanguageChanged(string langCode)
    {
        var culture = new CultureInfo(langCode);

        Language.Culture = culture;
        Utils.CurrentCulture = culture;
    }

    private static void SetPluginVersion()
    {
        PluginLog.Verbose("Entering Plugin.SetPluginVersion()");
        var pluginVersion = PluginInterface.GetPluginVersion();
        var config = ServiceContext.ConfigService.GetConfig();
        config.PluginVersion = pluginVersion;
        ServiceContext.ConfigService.SaveConfig(config);
    }

    private static bool LoadDatabase()
    {
        try
        {
            PluginLog.Verbose("Entering Plugin.LoadDatabase()");
            var dataSource = Path.Combine(PluginInterface.GetPluginConfigDirectory(), "data.db");
            SQLiteFluentMigratorRunner.Run(dataSource, Assembly.GetExecutingAssembly());
            return true;
        }
        catch (Exception exception)
        {
            // Log the error to the console and then return false,
            // originally, this method would not return anything if an exception thrown.
            // This method still has the same result if an uncaught error was thrown.
            PluginLog.Error(exception, "Failed to load database.");
            return false;
        }
    }

    private void RunPostStartup() => Task.Run(() =>
    {
        // Helper: bail out cleanly if Dalamud started unloading us mid-startup.
        // RunPostStartup is fire-and-forget, so a profile state flip (or any
        // other reason Dalamud yanks the plugin) can dispose RepositoryContext
        // while this task is still scheduled.  Checking between steps stops
        // every downstream call from blowing up with ObjectDisposedException.
        bool Aborted()
        {
            if (!_isDisposing) return false;
            PluginLog.Information("[Plugin] RunPostStartup aborted -- Plugin.Dispose was called before initialization completed.");
            return true;
        }

        try
        {
            PluginLog.Verbose("Entering Plugin.RunPostStartup()");
            if (Aborted()) return;
            EncounterService.EnsureNoOpenEncounters();
            if (Aborted()) return;
            ServiceContext.LodestoneService.Start();
            if (Aborted()) return;
            ServiceContext.ConfigService.SyncIcons();
            if (Aborted()) return;
            ServiceContext.PlayerCacheService.LoadPlayers();
            if (Aborted()) return;
            ServiceContext.VisibilityService.Initialize();
            if (Aborted()) return;
            SetPluginVersion();
            if (Aborted()) return;
            ServiceContext.BackupService.Startup();
            if (Aborted()) return;
            GuiController.Start();
            ContextMenuHandler.Start();
            EventDispatcher.Start();
            NameplateHandler.Start();
            CommandHandler.Start();
            PlayerLocationManager.Start();
            SocialListHandler.Start();
            ServiceContext.PlayerProcessService.Start();
            HousingProvider.Start();
            PlateWatcher.Start();
            BioScraper.Start();
            EncounterWatcher.Start();
            PartyMonitor.Start();
            ServiceContext.BlacklistAlertService.Start();
            if (Aborted()) return;
            PlayerTrackProvider = new PlayerTrackProvider(PluginInterface, new PlayerTrackAPI());
        }
        catch (ObjectDisposedException ex) when (_isDisposing)
        {
            // Expected when Dalamud disposes mid-startup.  Demote from ERR to INF.
            PluginLog.Information(
                "[Plugin] RunPostStartup caught ObjectDisposedException during shutdown ({0}).  Suppressing.",
                ex.ObjectName);
        }
        catch (Exception ex)
        {
            PluginLog.Error(ex, "[Plugin] RunPostStartup threw an unhandled exception; plugin may be partially initialized.");
        }
    });
}
