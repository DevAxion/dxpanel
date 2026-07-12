using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRXPanel.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase6AReseller : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ResellerPackageId",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuspensionReason",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ImpersonationSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ImpersonatorId = table.Column<string>(type: "TEXT", nullable: false),
                    ImpersonatorName = table.Column<string>(type: "TEXT", nullable: false),
                    TargetUserId = table.Column<string>(type: "TEXT", nullable: false),
                    TargetUserName = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImpersonationSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ResellerBrandings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ResellerId = table.Column<string>(type: "TEXT", nullable: false),
                    PanelTitle = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    LogoPath = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    FaviconPath = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    PrimaryColor = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SecondaryColor = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AccentColor = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LoginBackground = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    FooterText = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    CustomDomain = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    EmailSenderName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    EmailSenderAddress = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResellerBrandings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResellerBrandings_AspNetUsers_ResellerId",
                        column: x => x.ResellerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ResellerPackages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ResellerId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    DiskQuotaMB = table.Column<long>(type: "INTEGER", nullable: false),
                    BandwidthQuotaMB = table.Column<long>(type: "INTEGER", nullable: false),
                    MaxDomains = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxEmails = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxDatabases = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxFtpAccounts = table.Column<int>(type: "INTEGER", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BillingCycle = table.Column<int>(type: "INTEGER", nullable: false),
                    IsPublic = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResellerPackages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResellerPackages_AspNetUsers_ResellerId",
                        column: x => x.ResellerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ResellerProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    CompanyName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    DiskQuotaMB = table.Column<long>(type: "INTEGER", nullable: false),
                    BandwidthQuotaMB = table.Column<long>(type: "INTEGER", nullable: false),
                    MaxClients = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxDomains = table.Column<int>(type: "INTEGER", nullable: false),
                    AllowEmail = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowDns = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowBackups = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowCustomPhp = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResellerProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResellerProfiles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_ResellerPackageId",
                table: "AspNetUsers",
                column: "ResellerPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_ImpersonationSessions_ImpersonatorId_IsActive",
                table: "ImpersonationSessions",
                columns: new[] { "ImpersonatorId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ResellerBrandings_CustomDomain",
                table: "ResellerBrandings",
                column: "CustomDomain");

            migrationBuilder.CreateIndex(
                name: "IX_ResellerBrandings_ResellerId",
                table: "ResellerBrandings",
                column: "ResellerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResellerPackages_ResellerId",
                table: "ResellerPackages",
                column: "ResellerId");

            migrationBuilder.CreateIndex(
                name: "IX_ResellerProfiles_UserId",
                table: "ResellerProfiles",
                column: "UserId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_ResellerPackages_ResellerPackageId",
                table: "AspNetUsers",
                column: "ResellerPackageId",
                principalTable: "ResellerPackages",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_ResellerPackages_ResellerPackageId",
                table: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "ImpersonationSessions");

            migrationBuilder.DropTable(
                name: "ResellerBrandings");

            migrationBuilder.DropTable(
                name: "ResellerPackages");

            migrationBuilder.DropTable(
                name: "ResellerProfiles");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_ResellerPackageId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ResellerPackageId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SuspensionReason",
                table: "AspNetUsers");
        }
    }
}
