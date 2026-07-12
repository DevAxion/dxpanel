using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRXPanel.Data.Migrations
{
    /// <inheritdoc />
    public partial class StoreSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Addons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<long>(type: "INTEGER", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    BillingCycle = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Addons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CartItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    ReferenceId = table.Column<int>(type: "INTEGER", nullable: false),
                    DomainName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Label = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CartItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DomainRegistrations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    DomainName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Tld = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Registrar = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    RegisteredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AutoRenew = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DomainRegistrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DomainRegistrations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClientAddons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    AddonId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    PurchasedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientAddons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientAddons_Addons_AddonId",
                        column: x => x.AddonId,
                        principalTable: "Addons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClientAddons_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Addons_SortOrder",
                table: "Addons",
                column: "SortOrder");

            migrationBuilder.CreateIndex(
                name: "IX_CartItems_UserId",
                table: "CartItems",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientAddons_AddonId",
                table: "ClientAddons",
                column: "AddonId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientAddons_UserId_IsActive",
                table: "ClientAddons",
                columns: new[] { "UserId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_DomainRegistrations_DomainName",
                table: "DomainRegistrations",
                column: "DomainName");

            migrationBuilder.CreateIndex(
                name: "IX_DomainRegistrations_UserId",
                table: "DomainRegistrations",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CartItems");

            migrationBuilder.DropTable(
                name: "ClientAddons");

            migrationBuilder.DropTable(
                name: "DomainRegistrations");

            migrationBuilder.DropTable(
                name: "Addons");
        }
    }
}
