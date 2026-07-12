using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRXPanel.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase3CommandLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommandLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Command = table.Column<string>(type: "TEXT", nullable: false),
                    Output = table.Column<string>(type: "TEXT", nullable: false),
                    ExitCode = table.Column<int>(type: "INTEGER", nullable: false),
                    Simulated = table.Column<bool>(type: "INTEGER", nullable: false),
                    Service = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ExecutedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TriggeredBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommandLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommandLogs_ExecutedAt",
                table: "CommandLogs",
                column: "ExecutedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommandLogs");
        }
    }
}
