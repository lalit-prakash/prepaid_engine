using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrepaidEngine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTariffClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EnergyUnit",
                table: "Tariffs",
                type: "character varying(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "Kwh");

            migrationBuilder.AddColumn<string>(
                name: "FixedChargeBasis",
                table: "Tariffs",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "PerKw");

            migrationBuilder.AddColumn<decimal>(
                name: "InitialCreditSinglePhase",
                table: "Tariffs",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "InitialCreditThreePhase",
                table: "Tariffs",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinimumChargeableDemand",
                table: "Tariffs",
                type: "numeric(18,3)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScheduleCode",
                table: "Tariffs",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoltageLevel",
                table: "Tariffs",
                type: "character varying(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "LT");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnergyUnit",
                table: "Tariffs");

            migrationBuilder.DropColumn(
                name: "FixedChargeBasis",
                table: "Tariffs");

            migrationBuilder.DropColumn(
                name: "InitialCreditSinglePhase",
                table: "Tariffs");

            migrationBuilder.DropColumn(
                name: "InitialCreditThreePhase",
                table: "Tariffs");

            migrationBuilder.DropColumn(
                name: "MinimumChargeableDemand",
                table: "Tariffs");

            migrationBuilder.DropColumn(
                name: "ScheduleCode",
                table: "Tariffs");

            migrationBuilder.DropColumn(
                name: "VoltageLevel",
                table: "Tariffs");
        }
    }
}
