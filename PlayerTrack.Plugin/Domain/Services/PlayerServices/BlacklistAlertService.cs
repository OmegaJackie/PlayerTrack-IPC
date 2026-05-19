using System;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using PlayerTrack.Extensions;
using PlayerTrack.Handler;
using PlayerTrack.Models;

namespace PlayerTrack.Domain;

/// <summary>
/// Watches PartyMonitor for newly joined party members and prints a chat alert
/// to the local Echo channel when a blacklisted player joins.
///
/// "Blacklisted" means the player is in any PlayerTrack category whose
/// SocialListId is the local player's BlackList SocialList -- the native FFXIV
/// blacklist sync.  Players the user has manually added to that same synced
/// category are treated identically, which extends the native list with manual
/// entries for when the in-game list is full.
/// </summary>
public class BlacklistAlertService
{
    /// <summary>UI color used when the matched player has no category color.</summary>
    private const ushort DefaultRedUiColor = 17;

    private static readonly DalamudLinkPayload OpenPlayerTrackChatLinkHandler =
        Plugin.ChatGuiHandler.AddChatLinkHandler(10002, OnChatLinkClick);

    private bool _started;

    public void Start()
    {
        if (_started) return;
        PartyMonitor.OnPartyMemberJoined += OnPartyMemberJoined;
        _started = true;
        Plugin.PluginLog.Information("[BlacklistAlertService] Started.");
    }

    public void Dispose()
    {
        if (!_started) return;
        PartyMonitor.OnPartyMemberJoined -= OnPartyMemberJoined;
        _started = false;
    }

    private static void OnPartyMemberJoined(ulong contentId, string name, uint worldId) => Task.Run(() =>
    {
        try
        {
            var player = ServiceContext.PlayerDataService.GetPlayer(name, worldId);
            if (player == null)
                return; // Not in our tracked players -- can't be blacklisted.

            if (!IsBlacklisted(player))
                return;

            var colorId = PlayerConfigService.GetNameColor(player);
            var fgColor = colorId == 0 ? DefaultRedUiColor : (ushort)colorId;

            Plugin.ChatGuiHandler.PluginPrintNotice([
                OpenPlayerTrackChatLinkHandler,
                new UIForegroundPayload(fgColor),
                new TextPayload(player.Name),
                new IconPayload(BitmapFontIcon.CrossWorld),
                new TextPayload(Sheets.GetWorldNameById(player.WorldId)),
                new UIForegroundPayload(0),
                new TextPayload(", who is on your blacklist, has just joined the party."),
                RawPayload.LinkTerminator,
            ]);

            Plugin.PluginLog.Information(
                $"[BlacklistAlertService] Alerted on party-join: \"{player.Name}\"@worldId={player.WorldId}.");
        }
        catch (Exception ex)
        {
            Plugin.PluginLog.Error(ex, "[BlacklistAlertService] Unhandled exception in OnPartyMemberJoined.");
        }
    });

    /// <summary>
    /// Returns true when the player is assigned to any category that is synced
    /// to the local player's BlackList SocialList -- whether the entry was
    /// added by the native FFXIV blacklist sync or manually by the user.
    /// </summary>
    private static bool IsBlacklisted(Player player)
    {
        try
        {
            var localContentId = Plugin.PlayerState.ContentId;
            if (localContentId == 0) return false;

            // Find the local player's BlackList SocialList.
            var blacklistSocialList = Infrastructure.RepositoryContext.SocialListRepository
                .GetSocialList(localContentId, SocialListType.BlackList);
            if (blacklistSocialList == null)
                return false;

            // Find the category synced to that SocialList.
            var blacklistCategory = ServiceContext.CategoryService.GetSyncedCategory(blacklistSocialList.Id);
            if (blacklistCategory == null)
                return false;

            return player.AssignedCategories.Any(c => c.Id == blacklistCategory.Id);
        }
        catch (Exception ex)
        {
            Plugin.PluginLog.Warning(ex, "[BlacklistAlertService] Failed to evaluate blacklist membership.");
            return false;
        }
    }

    private static void OnChatLinkClick(uint id, SeString message)
    {
        Plugin.PluginLog.Verbose($"Entering BlacklistAlertService.OnChatLinkClick(): {id}");
        // Message format: "{Name}{CrossWorld}{World}, who is on your blacklist, has just joined the party."
        // The icon strips out so message.TextValue is "{Name}{World}, who is on your blacklist..." -- name and
        // world are concatenated without a separator.  Use the same regex pattern PlayerAlertService uses for
        // proximity/name-change alerts.
        var match = System.Text.RegularExpressions.Regex.Match(
            message.TextValue,
            @"^(?<playerName>[A-Z][a-zA-Z'-]*\s[A-Z][a-zA-Z'-]*)(?<worldName>[A-Z][a-zA-Z]*)\s.*");

        if (match.Success)
        {
            var name = match.Groups["playerName"].Value;
            var worldName = match.Groups["worldName"].Value;
            ServiceContext.PlayerProcessService.SelectPlayer(name, worldName);
        }
        else
        {
            Plugin.PluginLog.Verbose("[BlacklistAlertService] Failed to parse chat link.");
        }
    }
}
