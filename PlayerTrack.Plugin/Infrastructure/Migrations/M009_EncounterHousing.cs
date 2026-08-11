using FluentMigrator;

namespace PlayerTrack.Repositories.Migrations;

[Migration(20260714120000)]
public class M009_EncounterHousing : FluentMigrator.Migration
{
    public override void Up()
    {
        // Housing ward / plot / apartment captured while the encounter was open.
        // Existing encounters default to 0 (no housing information).
        Alter.Table("encounters")
            .AddColumn("housing_ward").AsInt16().NotNullable().WithDefaultValue(0)
            .AddColumn("housing_plot").AsInt16().NotNullable().WithDefaultValue(0)
            .AddColumn("housing_room").AsInt16().NotNullable().WithDefaultValue(0)
            .AddColumn("housing_division").AsInt16().NotNullable().WithDefaultValue(0);
    }

    public override void Down()
    {
    }
}
