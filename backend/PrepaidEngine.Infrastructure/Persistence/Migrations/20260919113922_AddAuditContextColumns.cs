using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrepaidEngine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditContextColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActorRole",
                table: "AuditEntries",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                table: "AuditEntries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceIp",
                table: "AuditEntries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_CorrelationId",
                table: "AuditEntries",
                column: "CorrelationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditEntries_CorrelationId",
                table: "AuditEntries");

            migrationBuilder.DropColumn(
                name: "ActorRole",
                table: "AuditEntries");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "AuditEntries");

            migrationBuilder.DropColumn(
                name: "SourceIp",
                table: "AuditEntries");
        }
    }
}
