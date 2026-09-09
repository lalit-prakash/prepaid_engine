using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrepaidEngine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTouPeriodsToTariff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TouPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    StartTime = table.Column<TimeSpan>(type: "interval", nullable: false),
                    EndTime = table.Column<TimeSpan>(type: "interval", nullable: false),
                    RatePerKvah = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    TariffId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TouPeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TouPeriods_Tariffs_TariffId",
                        column: x => x.TariffId,
                        principalTable: "Tariffs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TouPeriods_TariffId",
                table: "TouPeriods",
                column: "TariffId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TouPeriods");
        }
    }
}
