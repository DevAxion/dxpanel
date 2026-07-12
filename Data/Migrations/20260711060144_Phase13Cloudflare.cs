using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRXPanel.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase13Cloudflare : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CloudflareAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ApiToken = table.Column<string>(type: "TEXT", nullable: false),
                    AccountId = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    AccountName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    TokenScopes = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastValidatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CloudflareAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CloudflareAccounts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CloudflareDomains",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DomainId = table.Column<int>(type: "INTEGER", nullable: false),
                    CloudflareAccountId = table.Column<int>(type: "INTEGER", nullable: false),
                    ZoneId = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    NameServer1 = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    NameServer2 = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    DevelopmentMode = table.Column<bool>(type: "INTEGER", nullable: false),
                    UnderAttackMode = table.Column<bool>(type: "INTEGER", nullable: false),
                    AlwaysUseHttps = table.Column<bool>(type: "INTEGER", nullable: false),
                    SslMode = table.Column<int>(type: "INTEGER", nullable: false),
                    MinTlsVersion = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Tls13 = table.Column<bool>(type: "INTEGER", nullable: false),
                    OpportunisticEncryption = table.Column<bool>(type: "INTEGER", nullable: false),
                    MinifyCss = table.Column<bool>(type: "INTEGER", nullable: false),
                    MinifyJs = table.Column<bool>(type: "INTEGER", nullable: false),
                    MinifyHtml = table.Column<bool>(type: "INTEGER", nullable: false),
                    Brotli = table.Column<bool>(type: "INTEGER", nullable: false),
                    Http2 = table.Column<bool>(type: "INTEGER", nullable: false),
                    Http3 = table.Column<bool>(type: "INTEGER", nullable: false),
                    RocketLoader = table.Column<bool>(type: "INTEGER", nullable: false),
                    EarlyHints = table.Column<bool>(type: "INTEGER", nullable: false),
                    Polish = table.Column<int>(type: "INTEGER", nullable: false),
                    WebpConversion = table.Column<bool>(type: "INTEGER", nullable: false),
                    CacheLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    BrowserCacheTtl = table.Column<int>(type: "INTEGER", nullable: false),
                    SecurityLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    BotFightMode = table.Column<bool>(type: "INTEGER", nullable: false),
                    ChallengePassageTtl = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SyncedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CloudflareDomains", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CloudflareDomains_CloudflareAccounts_CloudflareAccountId",
                        column: x => x.CloudflareAccountId,
                        principalTable: "CloudflareAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CloudflareDomains_Domains_DomainId",
                        column: x => x.DomainId,
                        principalTable: "Domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CloudflareTunnels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CloudflareAccountId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    TunnelId = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Secret = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Hostnames = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CloudflareTunnels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CloudflareTunnels_CloudflareAccounts_CloudflareAccountId",
                        column: x => x.CloudflareAccountId,
                        principalTable: "CloudflareAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CloudflareAnalytics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CloudflareDomainId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Requests = table.Column<long>(type: "INTEGER", nullable: false),
                    Bandwidth = table.Column<long>(type: "INTEGER", nullable: false),
                    Threats = table.Column<long>(type: "INTEGER", nullable: false),
                    PageViews = table.Column<long>(type: "INTEGER", nullable: false),
                    UniqueVisitors = table.Column<long>(type: "INTEGER", nullable: false),
                    CachedRequests = table.Column<long>(type: "INTEGER", nullable: false),
                    CachedBandwidth = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CloudflareAnalytics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CloudflareAnalytics_CloudflareDomains_CloudflareDomainId",
                        column: x => x.CloudflareDomainId,
                        principalTable: "CloudflareDomains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CloudflareCaches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CloudflareDomainId = table.Column<int>(type: "INTEGER", nullable: false),
                    LastPurgedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PurgeType = table.Column<int>(type: "INTEGER", nullable: false),
                    Detail = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    PurgedBy = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CloudflareCaches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CloudflareCaches_CloudflareDomains_CloudflareDomainId",
                        column: x => x.CloudflareDomainId,
                        principalTable: "CloudflareDomains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CloudflareRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CloudflareDomainId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    RuleId = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Expression = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Hits = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CloudflareRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CloudflareRules_CloudflareDomains_CloudflareDomainId",
                        column: x => x.CloudflareDomainId,
                        principalTable: "CloudflareDomains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CloudflareAccounts_UserId",
                table: "CloudflareAccounts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CloudflareAnalytics_CloudflareDomainId_Date",
                table: "CloudflareAnalytics",
                columns: new[] { "CloudflareDomainId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CloudflareCaches_CloudflareDomainId_LastPurgedAt",
                table: "CloudflareCaches",
                columns: new[] { "CloudflareDomainId", "LastPurgedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CloudflareDomains_CloudflareAccountId",
                table: "CloudflareDomains",
                column: "CloudflareAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CloudflareDomains_DomainId",
                table: "CloudflareDomains",
                column: "DomainId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CloudflareDomains_ZoneId",
                table: "CloudflareDomains",
                column: "ZoneId");

            migrationBuilder.CreateIndex(
                name: "IX_CloudflareRules_CloudflareDomainId_Type",
                table: "CloudflareRules",
                columns: new[] { "CloudflareDomainId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_CloudflareTunnels_CloudflareAccountId",
                table: "CloudflareTunnels",
                column: "CloudflareAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CloudflareTunnels_UserId",
                table: "CloudflareTunnels",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CloudflareAnalytics");

            migrationBuilder.DropTable(
                name: "CloudflareCaches");

            migrationBuilder.DropTable(
                name: "CloudflareRules");

            migrationBuilder.DropTable(
                name: "CloudflareTunnels");

            migrationBuilder.DropTable(
                name: "CloudflareDomains");

            migrationBuilder.DropTable(
                name: "CloudflareAccounts");
        }
    }
}
