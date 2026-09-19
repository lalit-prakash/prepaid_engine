using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrepaidEngine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMeterDataTimeIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_RegisterReadings_ReadingTimestamp",
                table: "RegisterReadings",
                column: "ReadingTimestamp");

            migrationBuilder.CreateIndex(
                name: "IX_MeterEvents_EventTimestamp",
                table: "MeterEvents",
                column: "EventTimestamp");

            migrationBuilder.CreateIndex(
                name: "IX_MeterAlarms_RaisedAt",
                table: "MeterAlarms",
                column: "RaisedAt");

            migrationBuilder.CreateIndex(
                name: "IX_LoadSurveyIntervals_IntervalStart",
                table: "LoadSurveyIntervals",
                column: "IntervalStart");

            migrationBuilder.CreateIndex(
                name: "IX_DailyLoadProfiles_ProfileDate",
                table: "DailyLoadProfiles",
                column: "ProfileDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RegisterReadings_ReadingTimestamp",
                table: "RegisterReadings");

            migrationBuilder.DropIndex(
                name: "IX_MeterEvents_EventTimestamp",
                table: "MeterEvents");

            migrationBuilder.DropIndex(
                name: "IX_MeterAlarms_RaisedAt",
                table: "MeterAlarms");

            migrationBuilder.DropIndex(
                name: "IX_LoadSurveyIntervals_IntervalStart",
                table: "LoadSurveyIntervals");

            migrationBuilder.DropIndex(
                name: "IX_DailyLoadProfiles_ProfileDate",
                table: "DailyLoadProfiles");
        }
    }
}
