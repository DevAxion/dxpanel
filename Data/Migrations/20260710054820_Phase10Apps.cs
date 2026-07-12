using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRXPanel.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase10Apps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Label",
                table: "Backups",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    IconPath = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    MinDiskMB = table.Column<int>(type: "INTEGER", nullable: false),
                    MinPhpVersion = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    DownloadUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    InstallScript = table.Column<string>(type: "TEXT", nullable: true),
                    RequiresDatabase = table.Column<bool>(type: "INTEGER", nullable: false),
                    Rating = table.Column<double>(type: "REAL", nullable: false),
                    InstallCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Features = table.Column<string>(type: "TEXT", nullable: true),
                    Requirements = table.Column<string>(type: "TEXT", nullable: true),
                    Changelog = table.Column<string>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppInstallJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InstallationId = table.Column<int>(type: "INTEGER", nullable: true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Progress = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentStep = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Log = table.Column<string>(type: "TEXT", nullable: false),
                    AppName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppInstallJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppUpdateSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AutoUpdateMinor = table.Column<bool>(type: "INTEGER", nullable: false),
                    NotifyMajorOnly = table.Column<bool>(type: "INTEGER", nullable: false),
                    Schedule = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    EmailClientOnUpdate = table.Column<bool>(type: "INTEGER", nullable: false),
                    KeepRestorePoints = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppUpdateSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppInstallations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    DomainId = table.Column<int>(type: "INTEGER", nullable: false),
                    AppDefinitionId = table.Column<int>(type: "INTEGER", nullable: false),
                    InstalledVersion = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    InstallPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    DatabaseName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    DatabaseUser = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    TablePrefix = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    SiteUrl = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    AdminUrl = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    SiteTitle = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AdminUser = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    AdminEmail = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PhpVersion = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Language = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    AvailableVersion = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    AutoUpdate = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsStaging = table.Column<bool>(type: "INTEGER", nullable: false),
                    InstalledAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppInstallations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppInstallations_AppDefinitions_AppDefinitionId",
                        column: x => x.AppDefinitionId,
                        principalTable: "AppDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppInstallations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AppInstallations_Domains_DomainId",
                        column: x => x.DomainId,
                        principalTable: "Domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WpAssets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InstallationId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    LatestVersion = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdateAvailable = table.Column<bool>(type: "INTEGER", nullable: false),
                    InstalledAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WpAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WpAssets_AppInstallations_InstallationId",
                        column: x => x.InstallationId,
                        principalTable: "AppInstallations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppDefinitions_Slug",
                table: "AppDefinitions",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppInstallations_AppDefinitionId",
                table: "AppInstallations",
                column: "AppDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_AppInstallations_DomainId",
                table: "AppInstallations",
                column: "DomainId");

            migrationBuilder.CreateIndex(
                name: "IX_AppInstallations_UserId_Status",
                table: "AppInstallations",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AppInstallJobs_UserId_StartedAt",
                table: "AppInstallJobs",
                columns: new[] { "UserId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WpAssets_InstallationId_Type",
                table: "WpAssets",
                columns: new[] { "InstallationId", "Type" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppInstallJobs");

            migrationBuilder.DropTable(
                name: "AppUpdateSettings");

            migrationBuilder.DropTable(
                name: "WpAssets");

            migrationBuilder.DropTable(
                name: "AppInstallations");

            migrationBuilder.DropTable(
                name: "AppDefinitions");

            migrationBuilder.DropColumn(
                name: "Label",
                table: "Backups");
        }
    }
}
