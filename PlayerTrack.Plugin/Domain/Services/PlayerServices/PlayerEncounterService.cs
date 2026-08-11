using System;
using PlayerTrack.Infrastructure;
using PlayerTrack.Models;
using System.Collections.Generic;
using PlayerTrack.Data;

namespace PlayerTrack.Domain;

public class PlayerEncounterService
{
    public static void UpdatePlayerId(int originalPlayerId, int newPlayerId) =>
        RepositoryContext.PlayerEncounterRepository.UpdatePlayerId(originalPlayerId, newPlayerId);

    public static List<PlayerEncounter>? GetPlayerEncountersByPlayer(int playerId) =>
        RepositoryContext.PlayerEncounterRepository.GetAllByPlayerId(playerId);

    public static void DeletePlayerEncountersByPlayer(int playerId) =>
        RepositoryContext.PlayerEncounterRepository.DeleteAllByPlayerId(playerId);

    // CreatePlayerEncounter/GetEncounterLocation/EndPlayerEncounter all run once per
    // player entering or leaving the object table, so their entry and early-return
    // tracing has been removed -- it dominated the log in populated zones.
    public static int CreatePlayerEncounter(PlayerData toadPlayer, Player player)
    {
        if (player.Id == 0)
        {
            Plugin.PluginLog.Warning("Player Id is 0, cannot create player encounter.");
            return 0;
        }

        var encId = ServiceContext.EncounterService.CurrentEncounter?.Id ?? 0;
        if (encId == 0)
            return 0;

        var playerEncounter = RepositoryContext.PlayerEncounterRepository.GetByPlayerIdAndEncId(player.Id, encId);
        if (playerEncounter != null)
            return playerEncounter.Id;

        playerEncounter = new PlayerEncounter
        {
            PlayerId = player.Id,
            EncounterId = encId,
            JobId = toadPlayer.ClassJob,
            JobLvl = toadPlayer.Level,
        };
        return RepositoryContext.PlayerEncounterRepository.CreatePlayerEncounter(playerEncounter);
    }

    public static LocationData GetEncounterLocation()
    {
        var lastLocId = ServiceContext.EncounterService.CurrentEncounter?.TerritoryTypeId ?? 0;
        return Sheets.Locations[lastLocId];
    }

    public static void EndPlayerEncounters(int encounterId)
    {
        Plugin.PluginLog.Verbose($"Entering PlayerEncounterService.EndPlayerEncounters(): {encounterId}");
        var playerEncounters = RepositoryContext.PlayerEncounterRepository.GetAllByEncounterId(encounterId);
        if (playerEncounters == null || playerEncounters.Count == 0)
            return;

        var ended = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var playerEncounter in playerEncounters)
        {
            playerEncounter.Ended = ended;
            RepositoryContext.PlayerEncounterRepository.UpdatePlayerEncounter(playerEncounter);
        }
    }

    public static void EndPlayerEncounter(Player player, Encounter? encounter)
    {
        if (encounter == null || encounter.Id == 0)
            return;

        if (player.OpenPlayerEncounterId == 0)
            return;

        if (!encounter.SaveEncounter)
            return;

        var pEnc = RepositoryContext.PlayerEncounterRepository.GetByPlayerIdAndEncId(player.Id, encounter.Id);
        if (pEnc == null)
            return;

        pEnc.Ended = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        RepositoryContext.PlayerEncounterRepository.UpdatePlayerEncounter(pEnc);
    }
}
