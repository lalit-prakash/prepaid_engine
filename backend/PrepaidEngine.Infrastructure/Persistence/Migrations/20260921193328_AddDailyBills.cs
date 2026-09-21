using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrepaidEngine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyBills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DailyBills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TariffId = table.Column<Guid>(type: "uuid", nullable: false),
                    BillDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    IsProvisional = table.Column<bool>(type: "boolean", nullable: false),
                    Kwh = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    MonthToDateKwhBefore = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    GrossEnergyCharge = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    PrepaidRebate = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    FixedCharge = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    ElectricityDuty = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    LtSideMeteringSurcharge = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Tmc = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Cpmc = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    FppasShare = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Notes = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyBills", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyBills_ConsumerId_BillDate",
                table: "DailyBills",
                columns: new[] { "ConsumerId", "BillDate" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyBills_Reference",
                table: "DailyBills",
                column: "Reference",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyBills");
        }
    }
}
