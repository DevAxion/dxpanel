using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRXPanel.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase9Security : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BlockedIPs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IP = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    BlockedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsManual = table.Column<bool>(type: "INTEGER", nullable: false),
                    UnblockedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Country = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockedIPs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailSecurities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DomainId = table.Column<int>(type: "INTEGER", nullable: false),
                    DkimEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    DkimSelector = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    DkimPublicKey = table.Column<string>(type: "TEXT", nullable: true),
                    DkimPrivateKey = table.Column<string>(type: "TEXT", nullable: true),
                    SpfRecord = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    DmarcPolicy = table.Column<int>(type: "INTEGER", nullable: false),
                    DmarcPercentage = table.Column<int>(type: "INTEGER", nullable: false),
                    DmarcEmail = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    LastCheckedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DkimValid = table.Column<bool>(type: "INTEGER", nullable: false),
                    SpfValid = table.Column<bool>(type: "INTEGER", nullable: false),
                    DmarcValid = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailSecurities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailSecurities_Domains_DomainId",
                        column: x => x.DomainId,
                        principalTable: "Domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IpAccessRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Value = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IpAccessRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LoginAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IP = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Success = table.Column<bool>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UserAgent = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Country = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoginAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MalwareScanResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    ThreatType = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Severity = table.Column<int>(type: "INTEGER", nullable: false),
                    Details = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MalwareScanResults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ModSecurityAlerts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DomainId = table.Column<int>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IP = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Method = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    URI = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    RuleId = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    RuleMessage = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModSecurityAlerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModSecurityAlerts_Domains_DomainId",
                        column: x => x.DomainId,
                        principalTable: "Domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuarantinedFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    OriginalPath = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    QuarantinePath = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    ThreatName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    QuarantinedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RestoredAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuarantinedFiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScanResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ThreatName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ScannedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Action = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanResults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SecuritySettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BruteForceMaxAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                    BruteForceBlockMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    ProtectPanel = table.Column<bool>(type: "INTEGER", nullable: false),
                    ProtectFtp = table.Column<bool>(type: "INTEGER", nullable: false),
                    ProtectSmtp = table.Column<bool>(type: "INTEGER", nullable: false),
                    CrsVersion = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AvScheduleEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AvSchedule = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AvScanOnUpload = table.Column<bool>(type: "INTEGER", nullable: false),
                    ClamAvDefinitionsDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MalwareScheduleEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    MalwareSchedule = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AlertOnCritical = table.Column<bool>(type: "INTEGER", nullable: false),
                    RateLimitPerMinute = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecuritySettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WafConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DomainId = table.Column<int>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Mode = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WafConfigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WafConfigs_Domains_DomainId",
                        column: x => x.DomainId,
                        principalTable: "Domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WafCustomRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DomainId = table.Column<int>(type: "INTEGER", nullable: false),
                    RuleNumber = table.Column<long>(type: "INTEGER", nullable: false),
                    RuleText = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WafCustomRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WafIpRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DomainId = table.Column<int>(type: "INTEGER", nullable: true),
                    IP = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Action = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WafIpRules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BlockedIPs_IP",
                table: "BlockedIPs",
                column: "IP");

            migrationBuilder.CreateIndex(
                name: "IX_EmailSecurities_DomainId",
                table: "EmailSecurities",
                column: "DomainId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IpAccessRules_Kind_Value",
                table: "IpAccessRules",
                columns: new[] { "Kind", "Value" });

            migrationBuilder.CreateIndex(
                name: "IX_LoginAttempts_IP_Timestamp",
                table: "LoginAttempts",
                columns: new[] { "IP", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_MalwareScanResults_UserId_Status",
                table: "MalwareScanResults",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ModSecurityAlerts_DomainId_Timestamp",
                table: "ModSecurityAlerts",
                columns: new[] { "DomainId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_QuarantinedFiles_UserId_IsDeleted",
                table: "QuarantinedFiles",
                columns: new[] { "UserId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_ScanResults_UserId_ScannedAt",
                table: "ScanResults",
                columns: new[] { "UserId", "ScannedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WafConfigs_DomainId",
                table: "WafConfigs",
                column: "DomainId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WafCustomRules_DomainId",
                table: "WafCustomRules",
                column: "DomainId");

            migrationBuilder.CreateIndex(
                name: "IX_WafIpRules_DomainId_IP",
                table: "WafIpRules",
                columns: new[] { "DomainId", "IP" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlockedIPs");

            migrationBuilder.DropTable(
                name: "EmailSecurities");

            migrationBuilder.DropTable(
                name: "IpAccessRules");

            migrationBuilder.DropTable(
                name: "LoginAttempts");

            migrationBuilder.DropTable(
                name: "MalwareScanResults");

            migrationBuilder.DropTable(
                name: "ModSecurityAlerts");

            migrationBuilder.DropTable(
                name: "QuarantinedFiles");

            migrationBuilder.DropTable(
                name: "ScanResults");

            migrationBuilder.DropTable(
                name: "SecuritySettings");

            migrationBuilder.DropTable(
                name: "WafConfigs");

            migrationBuilder.DropTable(
                name: "WafCustomRules");

            migrationBuilder.DropTable(
                name: "WafIpRules");
        }
    }
}
