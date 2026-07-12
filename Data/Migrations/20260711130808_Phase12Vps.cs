using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRXPanel.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase12Vps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NodeId",
                table: "VpsPlans",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TemplateIds",
                table: "VpsPlans",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ProxmoxNodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Host = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    TokenId = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    TokenSecret = table.Column<string>(type: "TEXT", nullable: true),
                    VerifySsl = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    MaxVms = table.Column<int>(type: "INTEGER", nullable: false),
                    Storage = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Network = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Location = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProxmoxNodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VpsInstances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    PlanId = table.Column<int>(type: "INTEGER", nullable: false),
                    NodeId = table.Column<int>(type: "INTEGER", nullable: false),
                    VmId = table.Column<int>(type: "INTEGER", nullable: false),
                    Hostname = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    IpAddress = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Ipv6Address = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    MacAddress = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    ReverseDns = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    OsTemplate = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    CpuCores = table.Column<int>(type: "INTEGER", nullable: false),
                    RamMB = table.Column<int>(type: "INTEGER", nullable: false),
                    DiskGB = table.Column<int>(type: "INTEGER", nullable: false),
                    BandwidthGB = table.Column<int>(type: "INTEGER", nullable: false),
                    BandwidthUsed = table.Column<double>(type: "REAL", nullable: false),
                    RootPassword = table.Column<string>(type: "TEXT", nullable: true),
                    SshPort = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    NotifyBandwidth = table.Column<bool>(type: "INTEGER", nullable: false),
                    NotifyPower = table.Column<bool>(type: "INTEGER", nullable: false),
                    BandwidthSuspended = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SuspendedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    BandwidthCycleStart = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VpsInstances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VpsInstances_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VpsInstances_ProxmoxNodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "ProxmoxNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VpsInstances_VpsPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "VpsPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VpsIpAddresses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NodeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Address = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    IsIpv6 = table.Column<bool>(type: "INTEGER", nullable: false),
                    Gateway = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Prefix = table.Column<int>(type: "INTEGER", nullable: false),
                    AssignedInstanceId = table.Column<int>(type: "INTEGER", nullable: true),
                    IsReserved = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VpsIpAddresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VpsIpAddresses_ProxmoxNodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "ProxmoxNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VpsTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NodeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    OsType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    TemplateId = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    MinDiskGB = table.Column<int>(type: "INTEGER", nullable: false),
                    MinRamMB = table.Column<int>(type: "INTEGER", nullable: false),
                    MinCpuCores = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VpsTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VpsTemplates_ProxmoxNodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "ProxmoxNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VpsActions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VpsInstanceId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Action = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Output = table.Column<string>(type: "TEXT", nullable: true),
                    TaskId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VpsActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VpsActions_VpsInstances_VpsInstanceId",
                        column: x => x.VpsInstanceId,
                        principalTable: "VpsInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VpsBackups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VpsInstanceId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    SizeMB = table.Column<long>(type: "INTEGER", nullable: false),
                    StoragePath = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VpsBackups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VpsBackups_VpsInstances_VpsInstanceId",
                        column: x => x.VpsInstanceId,
                        principalTable: "VpsInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VpsConsoleSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VpsInstanceId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Token = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VpsConsoleSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VpsConsoleSessions_VpsInstances_VpsInstanceId",
                        column: x => x.VpsInstanceId,
                        principalTable: "VpsInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VpsFirewallRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VpsInstanceId = table.Column<int>(type: "INTEGER", nullable: false),
                    Action = table.Column<int>(type: "INTEGER", nullable: false),
                    Protocol = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Comment = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VpsFirewallRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VpsFirewallRules_VpsInstances_VpsInstanceId",
                        column: x => x.VpsInstanceId,
                        principalTable: "VpsInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VpsMetrics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VpsInstanceId = table.Column<int>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CpuPercent = table.Column<double>(type: "REAL", nullable: false),
                    RamPercent = table.Column<double>(type: "REAL", nullable: false),
                    DiskPercent = table.Column<double>(type: "REAL", nullable: false),
                    NetworkInMbps = table.Column<double>(type: "REAL", nullable: false),
                    NetworkOutMbps = table.Column<double>(type: "REAL", nullable: false),
                    DiskReadMbps = table.Column<double>(type: "REAL", nullable: false),
                    DiskWriteMbps = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VpsMetrics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VpsMetrics_VpsInstances_VpsInstanceId",
                        column: x => x.VpsInstanceId,
                        principalTable: "VpsInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VpsSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VpsInstanceId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VpsSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VpsSnapshots_VpsInstances_VpsInstanceId",
                        column: x => x.VpsInstanceId,
                        principalTable: "VpsInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProxmoxNodes_Name",
                table: "ProxmoxNodes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VpsActions_VpsInstanceId_StartedAt",
                table: "VpsActions",
                columns: new[] { "VpsInstanceId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_VpsBackups_VpsInstanceId",
                table: "VpsBackups",
                column: "VpsInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_VpsConsoleSessions_Token",
                table: "VpsConsoleSessions",
                column: "Token");

            migrationBuilder.CreateIndex(
                name: "IX_VpsConsoleSessions_VpsInstanceId",
                table: "VpsConsoleSessions",
                column: "VpsInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_VpsFirewallRules_VpsInstanceId",
                table: "VpsFirewallRules",
                column: "VpsInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_VpsInstances_NodeId",
                table: "VpsInstances",
                column: "NodeId");

            migrationBuilder.CreateIndex(
                name: "IX_VpsInstances_PlanId",
                table: "VpsInstances",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_VpsInstances_Status",
                table: "VpsInstances",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_VpsInstances_UserId",
                table: "VpsInstances",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_VpsIpAddresses_Address",
                table: "VpsIpAddresses",
                column: "Address",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VpsIpAddresses_NodeId",
                table: "VpsIpAddresses",
                column: "NodeId");

            migrationBuilder.CreateIndex(
                name: "IX_VpsMetrics_VpsInstanceId_Timestamp",
                table: "VpsMetrics",
                columns: new[] { "VpsInstanceId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_VpsSnapshots_VpsInstanceId",
                table: "VpsSnapshots",
                column: "VpsInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_VpsTemplates_NodeId",
                table: "VpsTemplates",
                column: "NodeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VpsActions");

            migrationBuilder.DropTable(
                name: "VpsBackups");

            migrationBuilder.DropTable(
                name: "VpsConsoleSessions");

            migrationBuilder.DropTable(
                name: "VpsFirewallRules");

            migrationBuilder.DropTable(
                name: "VpsIpAddresses");

            migrationBuilder.DropTable(
                name: "VpsMetrics");

            migrationBuilder.DropTable(
                name: "VpsSnapshots");

            migrationBuilder.DropTable(
                name: "VpsTemplates");

            migrationBuilder.DropTable(
                name: "VpsInstances");

            migrationBuilder.DropTable(
                name: "ProxmoxNodes");

            migrationBuilder.DropColumn(
                name: "NodeId",
                table: "VpsPlans");

            migrationBuilder.DropColumn(
                name: "TemplateIds",
                table: "VpsPlans");
        }
    }
}
