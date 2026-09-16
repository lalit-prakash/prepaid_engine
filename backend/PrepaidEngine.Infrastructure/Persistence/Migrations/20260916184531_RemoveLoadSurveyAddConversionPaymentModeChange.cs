using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrepaidEngine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLoadSurveyAddConversionPaymentModeChange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LoadSurveyIntervals");

            migrationBuilder.AddColumn<decimal>(
                name: "DiaAmount",
                table: "ConversionRequests",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FoaAmount",
                table: "ConversionRequests",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "IsPermanentConsumer",
                table: "ConversionRequests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "LastBillFrKvah",
                table: "ConversionRequests",
                type: "numeric(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "LastBillFrKwh",
                table: "ConversionRequests",
                type: "numeric(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "LastBillMaxDemandKw",
                table: "ConversionRequests",
                type: "numeric(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastBillingDate",
                table: "ConversionRequests",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "LastReadingDate",
                table: "ConversionRequests",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "MeterStatus",
                table: "ConversionRequests",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "OutstandingAmount",
                table: "ConversionRequests",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ReadingAtConversion",
                table: "ConversionRequests",
                type: "numeric(18,3)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReconnectionDate",
                table: "ConversionRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TemporaryDisconnectionDate",
                table: "ConversionRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PaymentModeChangeCommands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversionRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MeterReadingAtConversion = table.Column<decimal>(type: "numeric(18,3)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcknowledgedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentModeChangeCommands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentModeChangeCommands_Consumers_ConsumerId",
                        column: x => x.ConsumerId,
                        principalTable: "Consumers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentModeChangeCommands_ConversionRequests_ConversionRequ~",
                        column: x => x.ConversionRequestId,
                        principalTable: "ConversionRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentModeChangeCommands_ConsumerId",
                table: "PaymentModeChangeCommands",
                column: "ConsumerId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentModeChangeCommands_ConversionRequestId",
                table: "PaymentModeChangeCommands",
                column: "ConversionRequestId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentModeChangeCommands");

            migrationBuilder.DropColumn(
                name: "DiaAmount",
                table: "ConversionRequests");

            migrationBuilder.DropColumn(
                name: "FoaAmount",
                table: "ConversionRequests");

            migrationBuilder.DropColumn(
                name: "IsPermanentConsumer",
                table: "ConversionRequests");

            migrationBuilder.DropColumn(
                name: "LastBillFrKvah",
                table: "ConversionRequests");

            migrationBuilder.DropColumn(
                name: "LastBillFrKwh",
                table: "ConversionRequests");

            migrationBuilder.DropColumn(
                name: "LastBillMaxDemandKw",
                table: "ConversionRequests");

            migrationBuilder.DropColumn(
                name: "LastBillingDate",
                table: "ConversionRequests");

            migrationBuilder.DropColumn(
                name: "LastReadingDate",
                table: "ConversionRequests");

            migrationBuilder.DropColumn(
                name: "MeterStatus",
                table: "ConversionRequests");

            migrationBuilder.DropColumn(
                name: "OutstandingAmount",
                table: "ConversionRequests");

            migrationBuilder.DropColumn(
                name: "ReadingAtConversion",
                table: "ConversionRequests");

            migrationBuilder.DropColumn(
                name: "ReconnectionDate",
                table: "ConversionRequests");

            migrationBuilder.DropColumn(
                name: "TemporaryDisconnectionDate",
                table: "ConversionRequests");

            migrationBuilder.CreateTable(
                name: "LoadSurveyIntervals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CumulativeKwh = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    IntervalEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IntervalKwh = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    IntervalStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MeterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quality = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoadSurveyIntervals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoadSurveyIntervals_Consumers_ConsumerId",
                        column: x => x.ConsumerId,
                        principalTable: "Consumers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LoadSurveyIntervals_Meters_MeterId",
                        column: x => x.MeterId,
                        principalTable: "Meters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LoadSurveyIntervals_ConsumerId",
                table: "LoadSurveyIntervals",
                column: "ConsumerId");

            migrationBuilder.CreateIndex(
                name: "IX_LoadSurveyIntervals_MeterId_IntervalStart_IntervalEnd",
                table: "LoadSurveyIntervals",
                columns: new[] { "MeterId", "IntervalStart", "IntervalEnd" },
                unique: true);
        }
    }
}
