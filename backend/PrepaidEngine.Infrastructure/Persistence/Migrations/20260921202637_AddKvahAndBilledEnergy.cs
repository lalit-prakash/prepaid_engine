using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrepaidEngine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKvahAndBilledEnergy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ImportKvah",
                table: "LoadSurveyIntervals",
                type: "numeric(18,3)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EndCumulativeKvah",
                table: "DailyLoadProfiles",
                type: "numeric(18,3)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StartCumulativeKvah",
                table: "DailyLoadProfiles",
                type: "numeric(18,3)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalKvah",
                table: "DailyLoadProfiles",
                type: "numeric(18,3)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BilledEnergy",
                table: "DailyBills",
                type: "numeric(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "BilledUnit",
                table: "DailyBills",
                type: "character varying(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "kWh");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImportKvah",
                table: "LoadSurveyIntervals");

            migrationBuilder.DropColumn(
                name: "EndCumulativeKvah",
                table: "DailyLoadProfiles");

            migrationBuilder.DropColumn(
                name: "StartCumulativeKvah",
                table: "DailyLoadProfiles");

            migrationBuilder.DropColumn(
                name: "TotalKvah",
                table: "DailyLoadProfiles");

            migrationBuilder.DropColumn(
                name: "BilledEnergy",
                table: "DailyBills");

            migrationBuilder.DropColumn(
                name: "BilledUnit",
                table: "DailyBills");
        }
    }
}
