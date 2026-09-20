using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using PlayerTrack.Infrastructure;
using PlayerTrack.Models;

namespace PlayerTrack.Domain;

/// <summary>
/// Manages the per-player plate bio history.
/// Bio entries are written only when the bio text has changed since the last
/// recorded entry, preventing duplicate records for repeated plate views.
/// </summary>
public class PlayerBioService
{
    // Latest bio text per player, kept in memory so search predicates never hit the DB on the render thread.
    // Lazily loaded on first use and updated in place as new bios are recorded.
    private static ConcurrentDictionary<int, string>? latestBioCache;
    private static readonly object BioCacheLock = new();

    /// <summary>
    /// Records a new bio snapshot for <paramref name="playerId"/> only if the
    /// text differs from the most recently stored entry.  Empty bios are ignored.
    /// </summary>
    public static void UpdateBioIfChanged(int playerId, string bio)
    {
        if (string.IsNullOrWhiteSpace(bio)) return;

        try
        {
            var latest = RepositoryContext.PlayerBioRepository.GetLatestByPlayerId(playerId);
            if (latest != null && latest.Bio == bio)
                return; // Bio unchanged; skip.

            Plugin.PluginLog.Debug(
                $"[PlayerBioService] Recording new bio for player {playerId} " +
                $"(length={bio.Length}).");

            RepositoryContext.PlayerBioRepository.CreatePlayerBio(new PlayerBio
            {
                PlayerId = playerId,
                Bio      = bio,
            });

            // Keep the in-memory search cache current without a re-query.
            latestBioCache?.AddOrUpdate(playerId, bio, (_, _) => bio);
        }
        catch (Exception ex)
        {
            Plugin.PluginLog.Error(ex, $"[PlayerBioService] Failed to update bio for player {playerId}.");
        }
    }

    /// <summary>
    /// Returns the most recent bio text for each player that has one, keyed by player id.
    /// Backed by an in-memory cache loaded once; safe to call from the render thread.
    /// </summary>
    public static IReadOnlyDictionary<int, string> GetLatestBios()
    {
        var cache = latestBioCache;
        if (cache != null)
            return cache;

        lock (BioCacheLock)
        {
            if (latestBioCache != null)
                return latestBioCache;

            try
            {
                latestBioCache = new ConcurrentDictionary<int, string>(RepositoryContext.PlayerBioRepository.GetLatestBios());
            }
            catch (Exception ex)
            {
                Plugin.PluginLog.Error(ex, "[PlayerBioService] Failed to load latest bios.");
                latestBioCache = new ConcurrentDictionary<int, string>();
            }

            return latestBioCache;
        }
    }

    /// <summary>Drops the in-memory bio cache so it is reloaded from the database on next use.</summary>
    public static void InvalidateCache() => latestBioCache = null;

    /// <summary>Returns all recorded bios for a player, newest first.</summary>
    public static List<PlayerBio> GetBioHistory(int playerId)
    {
        try
        {
            return RepositoryContext.PlayerBioRepository.GetAllByPlayerId(playerId);
        }
        catch (Exception ex)
        {
            Plugin.PluginLog.Error(ex, $"[PlayerBioService] Failed to get bio history for player {playerId}.");
            return [];
        }
    }
}
