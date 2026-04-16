using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Indigo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PriceTicks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Ticker = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    Volume = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    Timestamp = table.Column<long>(type: "bigint", nullable: false),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DuplicateCheckHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RawData = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceTicks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PriceTicks_CreatedAt",
                table: "PriceTicks",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PriceTicks_DuplicateCheckHash",
                table: "PriceTicks",
                column: "DuplicateCheckHash");

            migrationBuilder.CreateIndex(
                name: "IX_PriceTicks_Source",
                table: "PriceTicks",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "IX_PriceTicks_Source_Timestamp",
                table: "PriceTicks",
                columns: new[] { "Source", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_PriceTicks_Ticker",
                table: "PriceTicks",
                column: "Ticker");

            migrationBuilder.CreateIndex(
                name: "IX_PriceTicks_Timestamp",
                table: "PriceTicks",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.DropTable(
                name: "PriceTicks");
        }
    }
}
