using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrepaidEngine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMeterLastCommunicatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastCommunicatedAt",
                table: "Meters",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Meters_LastCommunicatedAt",
                table: "Meters",
                column: "LastCommunicatedAt");

            // Existing meters: the latest time any real data from them reached the engine. Generated (provisional) daily profiles are not
            // the meter talking, so they are left out. A meter with no data stays null: never communicated.
            migrationBuilder.Sql("""
                UPDATE "Meters" m SET "LastCommunicatedAt" = x.t
                FROM (
                    SELECT "MeterId", MAX("ReceivedAt") AS t FROM (
                        SELECT "MeterId", "ReceivedAt" FROM "DailyLoadProfiles" WHERE NOT "IsProvisional"
                        UNION ALL SELECT "MeterId", "ReceivedAt" FROM "LoadSurveyIntervals"
                        UNION ALL SELECT "MeterId", "ReceivedAt" FROM "InstantaneousReadings"
                        UNION ALL SELECT "MeterId", "ReceivedAt" FROM "RegisterReadings"
                        UNION ALL SELECT "MeterId", "ReceivedAt" FROM "MeterEvents"
                        UNION ALL SELECT "MeterId", "ReceivedAt" FROM "MeterAlarms"
                    ) u GROUP BY "MeterId"
                ) x
                WHERE x."MeterId" = m."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Meters_LastCommunicatedAt",
                table: "Meters");

            migrationBuilder.DropColumn(
                name: "LastCommunicatedAt",
                table: "Meters");
        }
    }
}
