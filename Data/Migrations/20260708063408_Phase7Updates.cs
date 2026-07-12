using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRXPanel.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase7Updates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoUpdate",
                table: "PlatformSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "UpdateChannel",
                table: "PlatformSettings",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "UpdateHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FromVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ToVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    Simulated = table.Column<bool>(type: "INTEGER", nullable: false),
                    TriggeredBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UpdateHistory", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UpdateHistory_CreatedAt",
                table: "UpdateHistory",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UpdateHistory");

            migrationBuilder.DropColumn(
                name: "AutoUpdate",
                table: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "UpdateChannel",
                table: "PlatformSettings");
        }
    }
}
