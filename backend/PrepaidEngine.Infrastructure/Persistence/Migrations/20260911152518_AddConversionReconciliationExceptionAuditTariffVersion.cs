using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrepaidEngine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConversionReconciliationExceptionAuditTariffVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BillingMode",
                table: "Consumers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsNetMeter",
                table: "Consumers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OldValue = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    NewValue = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Details = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConversionRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MeterSerialNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ConsumerNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RequestType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ConsumerType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    InitialReading = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    InitialReadingDateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConversionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DecisionNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversionRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConversionRequests_Consumers_ConsumerId",
                        column: x => x.ConsumerId,
                        principalTable: "Consumers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OperationalExceptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ResolutionNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationalExceptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OperationalExceptions_Consumers_ConsumerId",
                        column: x => x.ConsumerId,
                        principalTable: "Consumers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReconciliationAdjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    PaymentMode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ReconciliationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    AppliedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationAdjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReconciliationAdjustments_Consumers_ConsumerId",
                        column: x => x.ConsumerId,
                        principalTable: "Consumers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TariffVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TariffId = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OldValue = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NewValue = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ChangeNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    EffectiveDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TariffVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TariffVersions_Tariffs_TariffId",
                        column: x => x.TariffId,
                        principalTable: "Tariffs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_EntityType",
                table: "AuditEntries",
                column: "EntityType");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_OccurredAt",
                table: "AuditEntries",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_ConversionRequests_ConsumerId",
                table: "ConversionRequests",
                column: "ConsumerId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversionRequests_TransactionId",
                table: "ConversionRequests",
                column: "TransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperationalExceptions_ConsumerId",
                table: "OperationalExceptions",
                column: "ConsumerId");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalExceptions_SourceType_SourceId",
                table: "OperationalExceptions",
                columns: new[] { "SourceType", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalExceptions_Status",
                table: "OperationalExceptions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationAdjustments_ConsumerId",
                table: "ReconciliationAdjustments",
                column: "ConsumerId");

            migrationBuilder.CreateIndex(
                name: "IX_TariffVersions_TariffId",
                table: "TariffVersions",
                column: "TariffId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEntries");

            migrationBuilder.DropTable(
                name: "ConversionRequests");

            migrationBuilder.DropTable(
                name: "OperationalExceptions");

            migrationBuilder.DropTable(
                name: "ReconciliationAdjustments");

            migrationBuilder.DropTable(
                name: "TariffVersions");

            migrationBuilder.DropColumn(
                name: "BillingMode",
                table: "Consumers");

            migrationBuilder.DropColumn(
                name: "IsNetMeter",
                table: "Consumers");
        }
    }
}
