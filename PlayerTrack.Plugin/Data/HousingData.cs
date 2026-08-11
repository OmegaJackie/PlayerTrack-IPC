using System.Collections.Generic;

namespace PlayerTrack.Data;

/// <summary>
/// A decoded housing location (ward / plot / apartment) captured from the game's
/// HousingManager while an encounter was open. <see cref="Ward" /> is 1-based; the
/// remaining values are the raw numbers returned by HousingManager and are only
/// meaningful when <see cref="Ward" /> is greater than zero.
/// </summary>
public class HousingData
{
    /// <summary>
    /// Gets or sets the 1-based ward number. Zero means the location is not in a housing district.
    /// </summary>
    public short Ward { get; set; }

    /// <summary>
    /// Gets or sets the raw HousingManager plot value. Values &gt;= 0 are a house plot (0-based);
    /// values &lt; -1 indicate an apartment (-128 = main division, -127 = subdivision).
    /// </summary>
    public short Plot { get; set; }

    /// <summary>
    /// Gets or sets the apartment number, or house room number (0 = none / apartment lobby).
    /// </summary>
    public short Room { get; set; }

    /// <summary>
    /// Gets or sets the raw division value (1 = main division, 2 = subdivision).
    /// </summary>
    public short Division { get; set; }

    /// <summary>
    /// Gets a value indicating whether housing information is present.
    /// </summary>
    public bool HasHousing => Ward > 0;

    /// <summary>
    /// Formats the housing location for display, e.g. "Ward 5, Plot 30" or
    /// "Ward 3, Subdivision, Apartment 12". Mirrors the in-game address layout; the
    /// main division is implied (only the subdivision is labelled). Returns an empty
    /// string when there is no housing information.
    /// </summary>
    /// <returns>the formatted housing address.</returns>
    public string Format()
    {
        if (Ward <= 0)
            return string.Empty;

        var parts = new List<string> { $"Ward {Ward}" };

        // Subdivision markers: the division flag, a high plot index, or the subdivision apartment sentinel.
        if (Division == 2 || Plot >= 30 || Plot == -127)
            parts.Add("Subdivision");

        if (Plot < -1)
            parts.Add($"Apartment {(Room == 0 ? "Lobby" : Room.ToString())}");
        else if (Plot > -1)
        {
            parts.Add($"Plot {Plot + 1}");
            if (Room > 0)
                parts.Add($"Room {Room}");
        }

        return string.Join(", ", parts);
    }
}
