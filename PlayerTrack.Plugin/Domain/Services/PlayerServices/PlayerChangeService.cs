using System;
using System.Collections.Generic;
using System.Linq;
using PlayerTrack.Infrastructure;
using PlayerTrack.Models;

namespace PlayerTrack.Domain;

public class PlayerChangeService
{
    public static void UpdatePlayerId(int originalPlayerId, int newPlayerId)
    {
        Plugin.PluginLog.Verbose($"Entering PlayerChangeService.UpdatePlayerId(): {originalPlayerId}, {newPlayerId}");
        RepositoryContext.PlayerNameWorldHistoryRepository.UpdatePlayerId(originalPlayerId, newPlayerId);
        RepositoryContext.PlayerCustomizeHistoryRepository.UpdatePlayerId(originalPlayerId, newPlayerId);
    }

    public static void AddNameWorldHistory(int playerId, string playerName, uint worldId) =>
        RepositoryContext.PlayerNameWorldHistoryRepository.CreatePlayerNameWorldHistory(new PlayerNameWorldHistory
        {
            PlayerId = playerId,
            PlayerName = playerName,
            WorldId = worldId,
        });

    public static void AddCustomizeHistory(int playerId, byte[] playerCustomize) =>
        RepositoryContext.PlayerCustomizeHistoryRepository.CreatePlayerCustomizeHistory(new PlayerCustomizeHistory
        {
            PlayerId = playerId,
            Customize = playerCustomize,
        });

    public static void DeleteCustomizeHistory(int playerId) =>
        RepositoryContext.PlayerCustomizeHistoryRepository.DeleteCustomizeHistory(playerId);

    public static void DeleteNameWorldHistory(int playerId) =>
        RepositoryContext.PlayerNameWorldHistoryRepository.DeleteNameWorldHistory(playerId);

    public static string GetPreviousNames(int playerId, string currentName)
    {
        Plugin.PluginLog.Verbose($"Entering PlayerChangeService.GetPreviousNames(): {playerId}, {currentName}");
        var names = RepositoryContext.PlayerNameWorldHistoryRepository.GetHistoricalNames(playerId);
        if (names == null)
            return string.Empty;

        var uniqueNames = names
            .Distinct()
            .Where(name => !string.IsNullOrEmpty(name) && !string.Equals(name, currentName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return uniqueNames.Count != 0 ? string.Join(", ", uniqueNames) : string.Empty;
    }

    public static string GetPreviousWorlds(int playerId, string currentWorldName)
    {
        Plugin.PluginLog.Verbose($"Entering PlayerChangeService.GetPreviousWorlds(): {playerId}, {currentWorldName}");
        var worldIds = RepositoryContext.PlayerNameWorldHistoryRepository.GetHistoricalWorlds(playerId);
        if (worldIds == null)
            return string.Empty;

        var worldNames = worldIds
            .Where(worldId => worldId != 0)
            .Select(Sheets.GetWorldNameById)
            .Distinct()
            .Where(worldName => !string.IsNullOrEmpty(worldName) && !string.Equals(worldName, currentWorldName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return worldNames.Count != 0 ? string.Join(", ", worldNames) : string.Empty;
    }

    public static List<PlayerNameWorldHistory> GetPlayerNameWorldHistory(int playerId)
    {
        Plugin.PluginLog.Verbose($"Entering PlayerChangeService.GetPlayerNameWorldHistory(): {playerId}");
        var nameWorldHistories = RepositoryContext.PlayerNameWorldHistoryRepository.GetPlayerNameWorldHistories(playerId);
        return nameWorldHistories == null ? [] : nameWorldHistories.ToList();
    }

    public static List<PlayerNameWorldHistory> GetAllPlayerNameWorldHistories()
    {
        Plugin.PluginLog.Verbose($"Entering PlayerChangeService.GetAllPlayerNameWorldHistories()");
        var nameWorldHistories = RepositoryContext.PlayerNameWorldHistoryRepository.GetAllPlayerNameWorldHistories();
        return nameWorldHistories == null ? [] : nameWorldHistories.ToList();
    }

    /// <summary>
    /// Populates PreviousNames/PreviousWorlds on all players with a single bulk query.
    /// </summary>
    public static void PopulateNameWorldHistories(List<Player> players)
    {
        var histories = GetAllPlayerNameWorldHistories();
        if (histories.Count == 0)
            return;

        var playersById = new Dictionary<int, Player>(players.Count);
        foreach (var player in players)
            playersById[player.Id] = player;

        foreach (var group in histories.GroupBy(history => history.PlayerId))
        {
            if (playersById.TryGetValue(group.Key, out var player))
                ApplyNameWorldHistory(player, group);
        }
    }

    public static void PopulateNameWorldHistory(Player player) =>
        ApplyNameWorldHistory(player, GetPlayerNameWorldHistory(player.Id));

    /// <summary>
    /// Updates the in-memory PreviousNames/PreviousWorlds for a name/world change,
    /// avoiding a re-fetch of the history table. Call before overwriting the player's
    /// current name/world with the new values.
    /// </summary>
    public static void TrackNameWorldChange(Player player, string newName, uint newWorldId)
    {
        if (!string.Equals(player.Name, newName, StringComparison.OrdinalIgnoreCase))
        {
            player.PreviousNames = player.PreviousNames
                .Append(player.Name)
                .Where(name => !string.Equals(name, newName, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        if (player.WorldId != newWorldId)
        {
            var newWorldName = Sheets.GetWorldNameById(newWorldId);
            player.PreviousWorlds = player.PreviousWorlds
                .Append(Sheets.GetWorldNameById(player.WorldId))
                .Where(world => !string.Equals(world, newWorldName, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    private static void ApplyNameWorldHistory(Player player, IEnumerable<PlayerNameWorldHistory> histories)
    {
        var names = new List<string>();
        var worlds = new List<string>();
        foreach (var history in histories)
        {
            if (!string.IsNullOrEmpty(history.PlayerName))
                names.Add(history.PlayerName);

            if (history.WorldId != 0)
                worlds.Add(Sheets.GetWorldNameById(history.WorldId));
        }

        var currentWorldName = player.WorldName();
        player.PreviousNames = names
            .Where(name => !string.Equals(name, player.Name, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        player.PreviousWorlds = worlds
            .Where(world => !string.IsNullOrEmpty(world) && !string.Equals(world, currentWorldName, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static List<PlayerCustomizeHistory> GetPlayerCustomizeHistory(int playerId)
    {
        Plugin.PluginLog.Verbose($"Entering PlayerChangeService.GetPlayerCustomizeHistory(): {playerId}");
        var customizeHistories = RepositoryContext.PlayerCustomizeHistoryRepository.GetPlayerCustomizeHistories(playerId);
        return customizeHistories == null ? [] : customizeHistories.ToList();
    }
}
