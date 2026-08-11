using Dapper.Contrib.Extensions;

namespace PlayerTrack.Models;

public class Encounter
{
    public int Id { get; set; }

    public uint TerritoryTypeId { get; set; }

    /// <summary>
    /// Gets or sets the 1-based housing ward captured for this encounter (0 = not in housing).
    /// </summary>
    public short HousingWard { get; set; }

    /// <summary>
    /// Gets or sets the raw HousingManager plot value (only meaningful when <see cref="HousingWard" /> &gt; 0).
    /// </summary>
    public short HousingPlot { get; set; }

    /// <summary>
    /// Gets or sets the apartment / house room number captured for this encounter (0 = none).
    /// </summary>
    public short HousingRoom { get; set; }

    /// <summary>
    /// Gets or sets the raw housing division (1 = main, 2 = subdivision; 0 = not in housing).
    /// </summary>
    public short HousingDivision { get; set; }

    public long Ended { get; set; }

    public long Created { get; set; }

    public long Updated { get; set; }

    [Write(false)] public bool SaveEncounter { get; set; }

    [Write(false)] public bool SavePlayers { get; set; }

    [Write(false)] public int CategoryId { get; set; }
}
