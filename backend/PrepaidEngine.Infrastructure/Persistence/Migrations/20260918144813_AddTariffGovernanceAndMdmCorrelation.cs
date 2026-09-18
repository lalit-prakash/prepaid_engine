using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrepaidEngine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTariffGovernanceAndMdmCorrelation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tariffs_Name",
                table: "Tariffs");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Tariffs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Active");

            migrationBuilder.AddColumn<string>(
                name: "ExternalCommandId",
                table: "MeterCommands",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponseCode",
                table: "MeterCommands",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponseMessage",
                table: "MeterCommands",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TariffChangeRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupersedesTariffId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResultingTariffId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProposedName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProposedCategory = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ProposedFixedChargePerUnitPerMonth = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ProposedPrepaidEnergyRebatePercent = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    ProposedEmergencyCreditLimit = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ProposedMinVendAmountSinglePhase = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    ProposedMaxVendAmountSinglePhase = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    ProposedMinVendAmountThreePhase = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    ProposedMaxVendAmountThreePhase = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ChangeReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SubmittedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CommencementDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RejectedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RejectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ActivatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TariffChangeRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TariffChangeRequestSlabs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FromKwh = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    UpToKwh = table.Column<decimal>(type: "numeric(18,3)", nullable: true),
                    RatePerKwh = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    TariffChangeRequestId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TariffChangeRequestSlabs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TariffChangeRequestSlabs_TariffChangeRequests_TariffChangeR~",
                        column: x => x.TariffChangeRequestId,
                        principalTable: "TariffChangeRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TariffChangeRequestTouPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    StartTime = table.Column<TimeSpan>(type: "interval", nullable: false),
                    EndTime = table.Column<TimeSpan>(type: "interval", nullable: false),
                    RatePerKvah = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    TariffChangeRequestId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TariffChangeRequestTouPeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TariffChangeRequestTouPeriods_TariffChangeRequests_TariffCh~",
                        column: x => x.TariffChangeRequestId,
                        principalTable: "TariffChangeRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tariffs_Name",
                table: "Tariffs",
                column: "Name",
                unique: true,
                filter: "\"Status\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_TariffChangeRequests_Status",
                table: "TariffChangeRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_TariffChangeRequests_SupersedesTariffId",
                table: "TariffChangeRequests",
                column: "SupersedesTariffId");

            migrationBuilder.CreateIndex(
                name: "IX_TariffChangeRequestSlabs_TariffChangeRequestId",
                table: "TariffChangeRequestSlabs",
                column: "TariffChangeRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_TariffChangeRequestTouPeriods_TariffChangeRequestId",
                table: "TariffChangeRequestTouPeriods",
                column: "TariffChangeRequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TariffChangeRequestSlabs");

            migrationBuilder.DropTable(
                name: "TariffChangeRequestTouPeriods");

            migrationBuilder.DropTable(
                name: "TariffChangeRequests");

            migrationBuilder.DropIndex(
                name: "IX_Tariffs_Name",
                table: "Tariffs");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Tariffs");

            migrationBuilder.DropColumn(
                name: "ExternalCommandId",
                table: "MeterCommands");

            migrationBuilder.DropColumn(
                name: "ResponseCode",
                table: "MeterCommands");

            migrationBuilder.DropColumn(
                name: "ResponseMessage",
                table: "MeterCommands");

            migrationBuilder.CreateIndex(
                name: "IX_Tariffs_Name",
                table: "Tariffs",
                column: "Name",
                unique: true);
        }
    }
}
