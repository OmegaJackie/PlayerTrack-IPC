namespace PlayerTrack.Models;

public enum PlayerListFilter
{
    CurrentPlayers,
    RecentPlayers,
    AllPlayers,
    PlayersByCategory,
    PlayersByTag,
    // Always last in the dropdown: search-only mode (list is empty until the
    // user types in the search box, then shows matches across all players).
    PlayerSearch,
}
