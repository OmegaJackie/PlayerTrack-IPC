using System;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using PlayerTrack.Data;
using PlayerTrack.Domain;

namespace PlayerTrack.Handler;

/// <summary>
/// Polls the game's HousingManager on the framework thread and records the ward /
/// plot / apartment of the current housing district onto the open encounter.
///
/// Housing data loads asynchronously a moment after a territory change, so it cannot
/// be read reliably when the encounter starts. Instead this polls while an encounter
/// is open and hands the reading to <see cref="EncounterService.SyncCurrentEncounterHousing" />,
/// which captures it once, the first time it becomes available.
/// </summary>
public static class HousingProvider
{
    private const long PollIntervalMs = 500;
    private static long _lastPoll;

    public static void Start()
    {
        Plugin.PluginLog.Verbose("Entering HousingProvider.Start()");
        Plugin.GameFramework.Update += OnFrameworkUpdate;
    }

    public static void Dispose()
    {
        Plugin.PluginLog.Verbose("Entering HousingProvider.Dispose()");
        Plugin.GameFramework.Update -= OnFrameworkUpdate;
    }

    private static void OnFrameworkUpdate(IFramework framework)
    {
        var now = Environment.TickCount64;
        if (now - _lastPoll < PollIntervalMs)
            return;
        _lastPoll = now;

        // Nothing to capture unless an encounter is open and still needs housing.
        var enc = ServiceContext.EncounterService.CurrentEncounter;
        if (enc == null || enc.HousingWard != 0)
            return;

        var housing = ReadCurrentHousing();
        if (housing.HasHousing)
            ServiceContext.EncounterService.SyncCurrentEncounterHousing(housing);
    }

    private static unsafe HousingData ReadCurrentHousing()
    {
        var manager = HousingManager.Instance();
        if (manager == null || manager->CurrentTerritory == null)
            return new HousingData();

        var ward = manager->GetCurrentWard();
        if (ward < 0)
            return new HousingData();

        return new HousingData
        {
            Ward = (short)(ward + 1),
            Plot = manager->GetCurrentPlot(),
            Room = manager->GetCurrentRoom(),
            Division = manager->GetCurrentDivision(),
        };
    }
}
