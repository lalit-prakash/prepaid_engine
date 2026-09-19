using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrepaidEngine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReportJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReportJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportKey = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ParametersJson = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RowCount = table.Column<long>(type: "bigint", nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    FileName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportJobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReportJobs_RequestedBy_RequestedAt",
                table: "ReportJobs",
                columns: new[] { "RequestedBy", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportJobs_Status_RequestedAt",
                table: "ReportJobs",
                columns: new[] { "Status", "RequestedAt" },
                filter: "\"Status\" IN ('Queued', 'Running')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReportJobs");
        }
    }
}
