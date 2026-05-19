using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using PlayerTrack.Domain;

namespace PlayerTrack.Handler;

/// <summary>
/// Watches the local player's party roster and fires <see cref="OnPartyMemberJoined"/>
/// whenever a new member appears that wasn't there on the previous poll.
///
/// The local player is excluded.  Members already in the party at the time the
/// monitor starts (or when a zone load suddenly repopulates the party) do NOT
/// trigger alerts -- only true joins after the monitor's last observation do.
/// </summary>
public static class PartyMonitor
{
    /// <summary>
    /// Throttle: don't poll more than once every 500 ms.  Party joins are
    /// player-driven so missing one frame is fine, and this keeps the per-tick
    /// cost negligible even with the framework update firing 60+ times per second.
    /// </summary>
    private const long PollIntervalMs = 500;

    private static readonly HashSet<ulong> LastSeenContentIds = new();
    private static long _lastPollMs;
    private static bool _started;

    public delegate void PartyMemberJoinedDelegate(ulong contentId, string name, uint worldId);
    public static event PartyMemberJoinedDelegate? OnPartyMemberJoined;

    public static void Start()
    {
        Plugin.PluginLog.Verbose("Entering PartyMonitor.Start()");
        if (_started) return;
        Plugin.GameFramework.Update += OnFrameworkUpdate;
        _started = true;
        Plugin.PluginLog.Information("[PartyMonitor] Started.");
    }

    public static void Dispose()
    {
        Plugin.PluginLog.Verbose("Entering PartyMonitor.Dispose()");
        if (!_started) return;
        Plugin.GameFramework.Update -= OnFrameworkUpdate;
        LastSeenContentIds.Clear();
        _started = false;
        Plugin.PluginLog.Information("[PartyMonitor] Disposed.");
    }

    private static void OnFrameworkUpdate(IFramework framework)
    {
        try
        {
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (nowMs - _lastPollMs < PollIntervalMs) return;
            _lastPollMs = nowMs;

            // Capture current party roster.
            var current = new HashSet<ulong>();
            var newMembers = new List<(ulong ContentId, string Name, uint WorldId)>();
            var localContentId = Plugin.PlayerState.ContentId;

            foreach (var member in Plugin.PartyList)
            {
                if (member == null) continue;
                ulong contentId;
                try
                {
                    contentId = (ulong)member.ContentId;
                }
                catch
                {
                    continue;
                }
                if (contentId == 0) continue;
                if (contentId == localContentId) continue;

                current.Add(contentId);
                if (!LastSeenContentIds.Contains(contentId))
                {
                    var name = member.Name.TextValue;
                    var worldId = (uint)member.World.RowId;
                    newMembers.Add((contentId, name, worldId));
                }
            }

            // First-ever poll seeds the cache silently so we don't alert for
            // pre-existing members (e.g. plugin restart while in a party).
            if (LastSeenContentIds.Count == 0 && current.Count > 0)
            {
                foreach (var id in current)
                    LastSeenContentIds.Add(id);
                return;
            }

            // Sync cache to current roster (handles leaves).
            LastSeenContentIds.Clear();
            foreach (var id in current)
                LastSeenContentIds.Add(id);

            // Fire join events for new members.
            foreach (var (contentId, name, worldId) in newMembers)
            {
                try
                {
                    Plugin.PluginLog.Debug(
                        $"[PartyMonitor] New party member: \"{name}\"@worldId={worldId} contentId={contentId}.");
                    OnPartyMemberJoined?.Invoke(contentId, name, worldId);
                }
                catch (Exception ex)
                {
                    Plugin.PluginLog.Error(ex, "[PartyMonitor] Exception in OnPartyMemberJoined handler.");
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.PluginLog.Error(ex, "[PartyMonitor] Unhandled exception in OnFrameworkUpdate.");
        }
    }
}
