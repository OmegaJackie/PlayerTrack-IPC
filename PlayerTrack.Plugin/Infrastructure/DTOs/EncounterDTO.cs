using FluentDapperLite.Repository;

namespace PlayerTrack.Infrastructure;

public class EncounterDTO : DTO
{
    public ushort territory_type_id { get; set; }

    public short housing_ward { get; set; }

    public short housing_plot { get; set; }

    public short housing_room { get; set; }

    public short housing_division { get; set; }

    public long ended { get; set; }
}
