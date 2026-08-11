using System.Collections.Concurrent;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using PlayerTrack.Data;
using PlayerTrack.Models;

namespace PlayerTrack.Domain;

public class PlayerNameplateService
{
    // This runs for every tracked player on every nameplate refresh, so logging the
    // outcome unconditionally floods the log with thousands of identical lines. Keep
    // the diagnostics -- they explain why a nameplate was or wasn't styled -- but only
    // emit them when the outcome for a player actually changes.
    private const int MaxLoggedOutcomes = 1000;
    private static readonly ConcurrentDictionary<int, string> LastLoggedOutcome = new();

    private static void LogOutcomeOnChange(Player player, string outcome, string message)
    {
        if (LastLoggedOutcome.TryGetValue(player.Id, out var previous) && previous == outcome)
            return;

        // Bound the dictionary so a long session with many players can't grow it forever.
        if (LastLoggedOutcome.Count >= MaxLoggedOutcomes)
            LastLoggedOutcome.Clear();

        LastLoggedOutcome[player.Id] = outcome;
        Plugin.PluginLog.Debug(message);
    }

    /// <summary>
    /// Forgets the cached log outcomes so the next refresh re-reports why each
    /// nameplate was styled or skipped. Called when nameplate settings change.
    /// </summary>
    public static void ResetOutcomeLog() => LastLoggedOutcome.Clear();

    public static PlayerNameplate GetPlayerNameplate(Player player, LocationType locationType)
    {
        var showInLocation = locationType switch
        {
            LocationType.Overworld => PlayerConfigService.GetNameplateShowInOverworld(player),
            LocationType.Content => PlayerConfigService.GetNameplateShowInContent(player),
            LocationType.HighEndContent => PlayerConfigService.GetNameplateShowInHighEndContent(player),
            _ => false,
        };

        var nameplate = new PlayerNameplate
        {
            CustomizeNameplate = showInLocation,
        };

        if (!nameplate.CustomizeNameplate)
        {
            LogOutcomeOnChange(
                player,
                $"hidden:{locationType}",
                $"[Nameplate] {player.Name} skipped: NameplateShowIn{locationType}=false " +
                "(check the per-category 'Show in {Location}' override -- it may be inheriting 'false' from another category or the default config).");
            return nameplate;
        }

        var isColorEnabled = PlayerConfigService.GetNameplateUseColor(player);
        ushort color = 0;
        if (isColorEnabled)
        {
            color = (ushort)PlayerConfigService.GetNameplateColor(player);
            nameplate.TitleLeftQuote = new SeString().Append(new UIForegroundPayload(color)).Append("《");
            nameplate.TitleRightQuote = new SeString().Append("》").Append(UIForegroundPayload.UIForegroundOff);
            nameplate.NameTextWrap = (new SeString(new UIForegroundPayload(color)), new SeString(UIForegroundPayload.UIForegroundOff));
            nameplate.FreeCompanyLeftQuote = new SeString().Append(new UIForegroundPayload(color)).Append(" «");
            nameplate.FreeCompanyRightQuote = new SeString().Append("»").Append(UIForegroundPayload.UIForegroundOff);
        }

        nameplate.NameplateUseColorIfDead = PlayerConfigService.GetNameplateUseColorIfDead(player);

        var nameplateTitleType = PlayerConfigService.GetNameplateTitleType(player);

        var title = nameplateTitleType switch
        {
            NameplateTitleType.CustomTitle => PlayerConfigService.GetNameplateCustomTitle(player),
            NameplateTitleType.CategoryName when player.PrimaryCategoryId != 0 => ServiceContext.CategoryService
                .GetCategory(player.PrimaryCategoryId)
                ?.Name ?? string.Empty,
            _ => string.Empty
        };

        if (nameplateTitleType != NameplateTitleType.NoChange && !string.IsNullOrEmpty(title))
        {
            nameplate.CustomTitle = title;
            nameplate.HasCustomTitle = true;
        }

        if ((!isColorEnabled || color == 0) && !nameplate.HasCustomTitle)
        {
            // No visible styling would be produced -- skip customization so the game
            // nameplate falls back to default rendering.  Log WHY so the user can
            // identify which setting needs to be toggled.
            string reason;
            if (!isColorEnabled && nameplateTitleType == NameplateTitleType.NoChange)
                reason = "NameplateUseColor=false AND NameplateTitleType=NoChange (enable 'Use Color' or pick a title type on the category)";
            else if (!isColorEnabled)
                reason = $"NameplateUseColor=false (title type is {nameplateTitleType} but resolved title was empty)";
            else if (color == 0)
                reason = "NameplateColor resolved to 0 (pick a non-default color on the category)";
            else
                reason = "no title and color disabled";

            LogOutcomeOnChange(
                player,
                $"unstyled:{locationType}:{reason}",
                $"[Nameplate] {player.Name} ({locationType}) skipped styling: {reason}. " +
                $"PrimaryCategoryId={player.PrimaryCategoryId} AssignedCategories={player.AssignedCategories.Count}");
            nameplate.CustomizeNameplate = false;
        }
        else
        {
            var styling = $"color={(isColorEnabled ? color.ToString() : "off")} " +
                          $"title=\"{(nameplate.HasCustomTitle ? nameplate.CustomTitle : "")}\"";
            LogOutcomeOnChange(
                player,
                $"styled:{locationType}:{styling}",
                $"[Nameplate] {player.Name} ({locationType}) styled: {styling}");
        }

        return nameplate;
    }
}
