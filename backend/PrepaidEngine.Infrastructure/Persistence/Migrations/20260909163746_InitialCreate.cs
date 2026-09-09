using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrepaidEngine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Meters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MeterNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Phase = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LastReadingKwh = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    LastReadingAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Meters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tariffs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FixedChargePerUnitPerMonth = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PrepaidEnergyRebatePercent = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    EmergencyCreditLimit = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MinVendAmountSinglePhase = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    MaxVendAmountSinglePhase = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    MinVendAmountThreePhase = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    MaxVendAmountThreePhase = table.Column<decimal>(type: "decimal(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tariffs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Consumers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ServiceAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ConnectionStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    MeterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConnectedLoadKw = table.Column<decimal>(type: "decimal(18,3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Consumers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Consumers_Meters_MeterId",
                        column: x => x.MeterId,
                        principalTable: "Meters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TariffSlabs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromKwh = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    UpToKwh = table.Column<decimal>(type: "decimal(18,3)", nullable: true),
                    RatePerKwh = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TariffId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TariffSlabs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TariffSlabs_Tariffs_TariffId",
                        column: x => x.TariffId,
                        principalTable: "Tariffs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConsumptionReadings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConsumerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConsumptionKwh = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsumptionReadings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsumptionReadings_Consumers_ConsumerId",
                        column: x => x.ConsumerId,
                        principalTable: "Consumers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PrepaidWallets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConsumerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    EmergencyCreditLimit = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrepaidWallets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrepaidWallets_Consumers_ConsumerId",
                        column: x => x.ConsumerId,
                        principalTable: "Consumers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RechargeTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConsumerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RmsReferenceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    InitiatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RechargeTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RechargeTransactions_Consumers_ConsumerId",
                        column: x => x.ConsumerId,
                        principalTable: "Consumers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PrepaidBills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConsumerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConsumptionReadingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TariffId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AmountPaid = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrepaidBills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrepaidBills_Consumers_ConsumerId",
                        column: x => x.ConsumerId,
                        principalTable: "Consumers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrepaidBills_ConsumptionReadings_ConsumptionReadingId",
                        column: x => x.ConsumptionReadingId,
                        principalTable: "ConsumptionReadings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrepaidBills_Tariffs_TariffId",
                        column: x => x.TariffId,
                        principalTable: "Tariffs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WalletTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WalletId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WalletTransactions_PrepaidWallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "PrepaidWallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Consumers_AccountNumber",
                table: "Consumers",
                column: "AccountNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Consumers_MeterId",
                table: "Consumers",
                column: "MeterId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConsumptionReadings_ConsumerId",
                table: "ConsumptionReadings",
                column: "ConsumerId");

            migrationBuilder.CreateIndex(
                name: "IX_Meters_MeterNumber",
                table: "Meters",
                column: "MeterNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrepaidBills_ConsumerId",
                table: "PrepaidBills",
                column: "ConsumerId");

            migrationBuilder.CreateIndex(
                name: "IX_PrepaidBills_ConsumptionReadingId",
                table: "PrepaidBills",
                column: "ConsumptionReadingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrepaidBills_TariffId",
                table: "PrepaidBills",
                column: "TariffId");

            migrationBuilder.CreateIndex(
                name: "IX_PrepaidWallets_ConsumerId",
                table: "PrepaidWallets",
                column: "ConsumerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RechargeTransactions_ConsumerId",
                table: "RechargeTransactions",
                column: "ConsumerId");

            migrationBuilder.CreateIndex(
                name: "IX_RechargeTransactions_RmsReferenceId",
                table: "RechargeTransactions",
                column: "RmsReferenceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tariffs_Name",
                table: "Tariffs",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TariffSlabs_TariffId",
                table: "TariffSlabs",
                column: "TariffId");

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_WalletId",
                table: "WalletTransactions",
                column: "WalletId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrepaidBills");

            migrationBuilder.DropTable(
                name: "RechargeTransactions");

            migrationBuilder.DropTable(
                name: "TariffSlabs");

            migrationBuilder.DropTable(
                name: "WalletTransactions");

            migrationBuilder.DropTable(
                name: "ConsumptionReadings");

            migrationBuilder.DropTable(
                name: "Tariffs");

            migrationBuilder.DropTable(
                name: "PrepaidWallets");

            migrationBuilder.DropTable(
                name: "Consumers");

            migrationBuilder.DropTable(
                name: "Meters");
        }
    }
}
