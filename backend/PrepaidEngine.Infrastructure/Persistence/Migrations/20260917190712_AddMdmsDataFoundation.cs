using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrepaidEngine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMdmsDataFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EnergyValidationResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerId = table.Column<Guid>(type: "uuid", nullable: false),
                    MeterId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValidationDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Rule = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ExpectedValueKwh = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    ActualValueKwh = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    VarianceKwh = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    VariancePct = table.Column<decimal>(type: "numeric(9,2)", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    EvaluatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnergyValidationResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnergyValidationResults_Consumers_ConsumerId",
                        column: x => x.ConsumerId,
                        principalTable: "Consumers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EnergyValidationResults_Meters_MeterId",
                        column: x => x.MeterId,
                        principalTable: "Meters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InstantaneousReadings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerId = table.Column<Guid>(type: "uuid", nullable: false),
                    MeterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VoltageVolts = table.Column<decimal>(type: "numeric(9,2)", nullable: false),
                    CurrentAmps = table.Column<decimal>(type: "numeric(9,2)", nullable: false),
                    PowerKw = table.Column<decimal>(type: "numeric(9,3)", nullable: false),
                    PowerFactor = table.Column<decimal>(type: "numeric(4,3)", nullable: false),
                    FrequencyHz = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    RelayStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstantaneousReadings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstantaneousReadings_Consumers_ConsumerId",
                        column: x => x.ConsumerId,
                        principalTable: "Consumers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InstantaneousReadings_Meters_MeterId",
                        column: x => x.MeterId,
                        principalTable: "Meters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LoadSurveyIntervals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerId = table.Column<Guid>(type: "uuid", nullable: false),
                    MeterId = table.Column<Guid>(type: "uuid", nullable: false),
                    IntervalStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IntervalEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ImportKwh = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
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

            migrationBuilder.CreateTable(
                name: "MeterAlarms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerId = table.Column<Guid>(type: "uuid", nullable: false),
                    MeterId = table.Column<Guid>(type: "uuid", nullable: false),
                    AlarmCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RaisedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AcknowledgedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcknowledgedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolutionNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeterAlarms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MeterAlarms_Consumers_ConsumerId",
                        column: x => x.ConsumerId,
                        principalTable: "Consumers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MeterAlarms_Meters_MeterId",
                        column: x => x.MeterId,
                        principalTable: "Meters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MeterEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerId = table.Column<Guid>(type: "uuid", nullable: false),
                    MeterId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    EventTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeterEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MeterEvents_Consumers_ConsumerId",
                        column: x => x.ConsumerId,
                        principalTable: "Consumers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MeterEvents_Meters_MeterId",
                        column: x => x.MeterId,
                        principalTable: "Meters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RegisterReadings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerId = table.Column<Guid>(type: "uuid", nullable: false),
                    MeterId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReadingTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CumulativeImportKwh = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegisterReadings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RegisterReadings_Consumers_ConsumerId",
                        column: x => x.ConsumerId,
                        principalTable: "Consumers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RegisterReadings_Meters_MeterId",
                        column: x => x.MeterId,
                        principalTable: "Meters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EnergyValidationResults_ConsumerId_MeterId_ValidationDate_R~",
                table: "EnergyValidationResults",
                columns: new[] { "ConsumerId", "MeterId", "ValidationDate", "Rule" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EnergyValidationResults_MeterId",
                table: "EnergyValidationResults",
                column: "MeterId");

            migrationBuilder.CreateIndex(
                name: "IX_EnergyValidationResults_Status",
                table: "EnergyValidationResults",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_InstantaneousReadings_ConsumerId",
                table: "InstantaneousReadings",
                column: "ConsumerId");

            migrationBuilder.CreateIndex(
                name: "IX_InstantaneousReadings_MeterId_Timestamp",
                table: "InstantaneousReadings",
                columns: new[] { "MeterId", "Timestamp" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoadSurveyIntervals_ConsumerId_MeterId_IntervalStart",
                table: "LoadSurveyIntervals",
                columns: new[] { "ConsumerId", "MeterId", "IntervalStart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoadSurveyIntervals_MeterId",
                table: "LoadSurveyIntervals",
                column: "MeterId");

            migrationBuilder.CreateIndex(
                name: "IX_MeterAlarms_ConsumerId",
                table: "MeterAlarms",
                column: "ConsumerId");

            migrationBuilder.CreateIndex(
                name: "IX_MeterAlarms_MeterId_AlarmCode_RaisedAt",
                table: "MeterAlarms",
                columns: new[] { "MeterId", "AlarmCode", "RaisedAt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MeterAlarms_Status",
                table: "MeterAlarms",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MeterEvents_ConsumerId",
                table: "MeterEvents",
                column: "ConsumerId");

            migrationBuilder.CreateIndex(
                name: "IX_MeterEvents_MeterId_EventCode_EventTimestamp",
                table: "MeterEvents",
                columns: new[] { "MeterId", "EventCode", "EventTimestamp" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RegisterReadings_ConsumerId_MeterId_ReadingTimestamp",
                table: "RegisterReadings",
                columns: new[] { "ConsumerId", "MeterId", "ReadingTimestamp" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RegisterReadings_MeterId",
                table: "RegisterReadings",
                column: "MeterId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EnergyValidationResults");

            migrationBuilder.DropTable(
                name: "InstantaneousReadings");

            migrationBuilder.DropTable(
                name: "LoadSurveyIntervals");

            migrationBuilder.DropTable(
                name: "MeterAlarms");

            migrationBuilder.DropTable(
                name: "MeterEvents");

            migrationBuilder.DropTable(
                name: "RegisterReadings");
        }
    }
}
