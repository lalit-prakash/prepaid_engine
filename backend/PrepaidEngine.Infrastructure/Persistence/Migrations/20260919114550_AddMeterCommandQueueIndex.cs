using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrepaidEngine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMeterCommandQueueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_MeterCommands_Status_CreatedAt",
                table: "MeterCommands",
                columns: new[] { "Status", "CreatedAt" },
                filter: "\"Status\" IN ('Queued', 'Sent')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MeterCommands_Status_CreatedAt",
                table: "MeterCommands");
        }
    }
}
