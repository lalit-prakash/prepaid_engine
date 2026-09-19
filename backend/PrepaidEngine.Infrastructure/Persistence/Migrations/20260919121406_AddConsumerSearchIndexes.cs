using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrepaidEngine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConsumerSearchIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateIndex(
                name: "IX_Meters_MeterNumber_trgm",
                table: "Meters",
                column: "MeterNumber")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Consumers_AccountNumber_trgm",
                table: "Consumers",
                column: "AccountNumber")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Consumers_MobileNumber_trgm",
                table: "Consumers",
                column: "MobileNumber")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Consumers_Name_trgm",
                table: "Consumers",
                column: "Name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Meters_MeterNumber_trgm",
                table: "Meters");

            migrationBuilder.DropIndex(
                name: "IX_Consumers_AccountNumber_trgm",
                table: "Consumers");

            migrationBuilder.DropIndex(
                name: "IX_Consumers_MobileNumber_trgm",
                table: "Consumers");

            migrationBuilder.DropIndex(
                name: "IX_Consumers_Name_trgm",
                table: "Consumers");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }
    }
}
