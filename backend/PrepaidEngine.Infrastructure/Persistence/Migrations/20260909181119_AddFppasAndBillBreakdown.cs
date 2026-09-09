using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrepaidEngine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFppasAndBillBreakdown : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ElectricityDutyAmount",
                table: "PrepaidBills",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "EnergyChargeGross",
                table: "PrepaidBills",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FixedCharge",
                table: "PrepaidBills",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FppasAmount",
                table: "PrepaidBills",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "FppasChargeId",
                table: "PrepaidBills",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PrepaidRebateAmount",
                table: "PrepaidBills",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "FppasCharges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceEnergyCharge = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    RateFraction = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    NotifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FppasCharges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrepaidBills_FppasChargeId",
                table: "PrepaidBills",
                column: "FppasChargeId");

            migrationBuilder.AddForeignKey(
                name: "FK_PrepaidBills_FppasCharges_FppasChargeId",
                table: "PrepaidBills",
                column: "FppasChargeId",
                principalTable: "FppasCharges",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PrepaidBills_FppasCharges_FppasChargeId",
                table: "PrepaidBills");

            migrationBuilder.DropTable(
                name: "FppasCharges");

            migrationBuilder.DropIndex(
                name: "IX_PrepaidBills_FppasChargeId",
                table: "PrepaidBills");

            migrationBuilder.DropColumn(
                name: "ElectricityDutyAmount",
                table: "PrepaidBills");

            migrationBuilder.DropColumn(
                name: "EnergyChargeGross",
                table: "PrepaidBills");

            migrationBuilder.DropColumn(
                name: "FixedCharge",
                table: "PrepaidBills");

            migrationBuilder.DropColumn(
                name: "FppasAmount",
                table: "PrepaidBills");

            migrationBuilder.DropColumn(
                name: "FppasChargeId",
                table: "PrepaidBills");

            migrationBuilder.DropColumn(
                name: "PrepaidRebateAmount",
                table: "PrepaidBills");
        }
    }
}
