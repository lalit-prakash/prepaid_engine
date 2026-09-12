using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrepaidEngine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLsDlpBillingPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MobileNumber",
                table: "Consumers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TariffId",
                table: "Consumers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BillingRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    BillingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ConsumerCount = table.Column<int>(type: "integer", nullable: false),
                    ExceptionCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DailyLoadProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerId = table.Column<Guid>(type: "uuid", nullable: false),
                    MeterId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileDate = table.Column<DateOnly>(type: "date", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartCumulativeKwh = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    EndCumulativeKwh = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    TotalKwh = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsProvisional = table.Column<bool>(type: "boolean", nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyLoadProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyLoadProfiles_Consumers_ConsumerId",
                        column: x => x.ConsumerId,
                        principalTable: "Consumers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DailyLoadProfiles_Meters_MeterId",
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
                    CumulativeKwh = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    IntervalKwh = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    Quality = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
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
                name: "MeterAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerId = table.Column<Guid>(type: "uuid", nullable: false),
                    OldMeterId = table.Column<Guid>(type: "uuid", nullable: true),
                    NewMeterId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OldMeterClosingReadingKwh = table.Column<decimal>(type: "numeric(18,3)", nullable: true),
                    NewMeterOpeningReadingKwh = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeterAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MeterAssignments_Consumers_ConsumerId",
                        column: x => x.ConsumerId,
                        principalTable: "Consumers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MeterBillingControls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerId = table.Column<Guid>(type: "uuid", nullable: false),
                    MeterId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActualBillingBlocked = table.Column<bool>(type: "boolean", nullable: false),
                    BlockReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    BlockedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClearedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeterBillingControls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MeterBillingControls_Consumers_ConsumerId",
                        column: x => x.ConsumerId,
                        principalTable: "Consumers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MeterBillingControls_Meters_MeterId",
                        column: x => x.MeterId,
                        principalTable: "Meters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProviderReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationEvents_Consumers_ConsumerId",
                        column: x => x.ConsumerId,
                        principalTable: "Consumers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillingRuns_RunType_BillingDate",
                table: "BillingRuns",
                columns: new[] { "RunType", "BillingDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyLoadProfiles_ConsumerId_MeterId_ProfileDate",
                table: "DailyLoadProfiles",
                columns: new[] { "ConsumerId", "MeterId", "ProfileDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyLoadProfiles_MeterId",
                table: "DailyLoadProfiles",
                column: "MeterId");

            migrationBuilder.CreateIndex(
                name: "IX_LoadSurveyIntervals_ConsumerId",
                table: "LoadSurveyIntervals",
                column: "ConsumerId");

            migrationBuilder.CreateIndex(
                name: "IX_LoadSurveyIntervals_MeterId_IntervalStart_IntervalEnd",
                table: "LoadSurveyIntervals",
                columns: new[] { "MeterId", "IntervalStart", "IntervalEnd" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MeterAssignments_ConsumerId",
                table: "MeterAssignments",
                column: "ConsumerId");

            migrationBuilder.CreateIndex(
                name: "IX_MeterBillingControls_ConsumerId_MeterId",
                table: "MeterBillingControls",
                columns: new[] { "ConsumerId", "MeterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MeterBillingControls_MeterId",
                table: "MeterBillingControls",
                column: "MeterId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationEvents_ConsumerId",
                table: "NotificationEvents",
                column: "ConsumerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillingRuns");

            migrationBuilder.DropTable(
                name: "DailyLoadProfiles");

            migrationBuilder.DropTable(
                name: "LoadSurveyIntervals");

            migrationBuilder.DropTable(
                name: "MeterAssignments");

            migrationBuilder.DropTable(
                name: "MeterBillingControls");

            migrationBuilder.DropTable(
                name: "NotificationEvents");

            migrationBuilder.DropColumn(
                name: "MobileNumber",
                table: "Consumers");

            migrationBuilder.DropColumn(
                name: "TariffId",
                table: "Consumers");
        }
    }
}
