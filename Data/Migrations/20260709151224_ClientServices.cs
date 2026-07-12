using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRXPanel.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClientServices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClientServices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    ReferenceId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    ResourceSummary = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    BillingCycle = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentPeriodStart = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CurrentPeriodEnd = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CancelledAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientServices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientServices_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClientServices_UserId_Status",
                table: "ClientServices",
                columns: new[] { "UserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientServices");
        }
    }
}
