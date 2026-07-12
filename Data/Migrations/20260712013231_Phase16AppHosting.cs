using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRXPanel.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase16AppHosting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppRuntimes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    BinaryPath = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppRuntimes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HostedApps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    DomainId = table.Column<int>(type: "INTEGER", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    RuntimeId = table.Column<int>(type: "INTEGER", nullable: true),
                    AppPath = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    EntryPoint = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    StartCommand = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    ProcessCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Pm2Id = table.Column<int>(type: "INTEGER", nullable: true),
                    Pid = table.Column<int>(type: "INTEGER", nullable: true),
                    Uptime = table.Column<long>(type: "INTEGER", nullable: false),
                    RestartCount = table.Column<int>(type: "INTEGER", nullable: false),
                    MemoryMB = table.Column<double>(type: "REAL", nullable: false),
                    CpuPercent = table.Column<double>(type: "REAL", nullable: false),
                    AutoRestart = table.Column<bool>(type: "INTEGER", nullable: false),
                    ClusterMode = table.Column<bool>(type: "INTEGER", nullable: false),
                    WatchMode = table.Column<bool>(type: "INTEGER", nullable: false),
                    MaxMemoryRestartMB = table.Column<int>(type: "INTEGER", nullable: false),
                    VirtualenvCreated = table.Column<bool>(type: "INTEGER", nullable: false),
                    PythonVersion = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    LastHealthCheckAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Healthy = table.Column<bool>(type: "INTEGER", nullable: false),
                    AutoRestartsThisHour = table.Column<int>(type: "INTEGER", nullable: false),
                    AutoRestartWindowStart = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedApps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedApps_AppRuntimes_RuntimeId",
                        column: x => x.RuntimeId,
                        principalTable: "AppRuntimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_HostedApps_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HostedApps_Domains_DomainId",
                        column: x => x.DomainId,
                        principalTable: "Domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "HostedAppDeploys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostedAppId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CommitHash = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Output = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedAppDeploys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedAppDeploys_HostedApps_HostedAppId",
                        column: x => x.HostedAppId,
                        principalTable: "HostedApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HostedAppEnvs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostedAppId = table.Column<int>(type: "INTEGER", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    IsSecret = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedAppEnvs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedAppEnvs_HostedApps_HostedAppId",
                        column: x => x.HostedAppId,
                        principalTable: "HostedApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HostedAppHealthIncidents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostedAppId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedAppHealthIncidents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedAppHealthIncidents_HostedApps_HostedAppId",
                        column: x => x.HostedAppId,
                        principalTable: "HostedApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HostedAppLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostedAppId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedAppLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedAppLogs_HostedApps_HostedAppId",
                        column: x => x.HostedAppId,
                        principalTable: "HostedApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HostedAppMetrics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostedAppId = table.Column<int>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CpuPercent = table.Column<double>(type: "REAL", nullable: false),
                    MemoryMB = table.Column<double>(type: "REAL", nullable: false),
                    RequestsPerSec = table.Column<double>(type: "REAL", nullable: false),
                    ResponseTimeMs = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedAppMetrics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedAppMetrics_HostedApps_HostedAppId",
                        column: x => x.HostedAppId,
                        principalTable: "HostedApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppRuntimes_Type_Version",
                table: "AppRuntimes",
                columns: new[] { "Type", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostedAppDeploys_HostedAppId_CreatedAt",
                table: "HostedAppDeploys",
                columns: new[] { "HostedAppId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HostedAppEnvs_HostedAppId_Key",
                table: "HostedAppEnvs",
                columns: new[] { "HostedAppId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostedAppHealthIncidents_HostedAppId_StartedAt",
                table: "HostedAppHealthIncidents",
                columns: new[] { "HostedAppId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HostedAppLogs_HostedAppId_Timestamp",
                table: "HostedAppLogs",
                columns: new[] { "HostedAppId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_HostedAppMetrics_HostedAppId_Timestamp",
                table: "HostedAppMetrics",
                columns: new[] { "HostedAppId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_HostedApps_DomainId",
                table: "HostedApps",
                column: "DomainId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedApps_Port",
                table: "HostedApps",
                column: "Port");

            migrationBuilder.CreateIndex(
                name: "IX_HostedApps_RuntimeId",
                table: "HostedApps",
                column: "RuntimeId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedApps_Status",
                table: "HostedApps",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_HostedApps_UserId",
                table: "HostedApps",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HostedAppDeploys");

            migrationBuilder.DropTable(
                name: "HostedAppEnvs");

            migrationBuilder.DropTable(
                name: "HostedAppHealthIncidents");

            migrationBuilder.DropTable(
                name: "HostedAppLogs");

            migrationBuilder.DropTable(
                name: "HostedAppMetrics");

            migrationBuilder.DropTable(
                name: "HostedApps");

            migrationBuilder.DropTable(
                name: "AppRuntimes");
        }
    }
}
