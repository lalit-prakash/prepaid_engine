using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrepaidEngine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConsumerBillingFactsAndFppasRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CtPtMaintenanceOptedIn",
                table: "Consumers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CtPtWiring",
                table: "Consumers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MeteredOnLtSide",
                table: "Consumers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SupplyVoltage",
                table: "Consumers",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TransformerCapacityKva",
                table: "Consumers",
                type: "numeric(18,3)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TransformerMaintenanceOptedIn",
                table: "Consumers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "FppasRateNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RateFraction = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    NotifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ApplicableBillingMonth = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FppasRateNotifications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FppasRateNotifications_ApplicableBillingMonth",
                table: "FppasRateNotifications",
                column: "ApplicableBillingMonth",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FppasRateNotifications");

            migrationBuilder.DropColumn(
                name: "CtPtMaintenanceOptedIn",
                table: "Consumers");

            migrationBuilder.DropColumn(
                name: "CtPtWiring",
                table: "Consumers");

            migrationBuilder.DropColumn(
                name: "MeteredOnLtSide",
                table: "Consumers");

            migrationBuilder.DropColumn(
                name: "SupplyVoltage",
                table: "Consumers");

            migrationBuilder.DropColumn(
                name: "TransformerCapacityKva",
                table: "Consumers");

            migrationBuilder.DropColumn(
                name: "TransformerMaintenanceOptedIn",
                table: "Consumers");
        }
    }
}
