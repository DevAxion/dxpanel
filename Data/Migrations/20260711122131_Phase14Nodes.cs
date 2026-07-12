using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRXPanel.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase14Nodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LoadBalancerSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AutoBalance = table.Column<bool>(type: "INTEGER", nullable: false),
                    CpuThreshold = table.Column<int>(type: "INTEGER", nullable: false),
                    GeoRouting = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoadBalancerSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServerNodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Hostname = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IpAddress = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    SshPort = table.Column<int>(type: "INTEGER", nullable: false),
                    SshUsername = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    SshKeyPath = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    SshPassword = table.Column<string>(type: "TEXT", nullable: true),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CpuCores = table.Column<int>(type: "INTEGER", nullable: false),
                    RamGB = table.Column<int>(type: "INTEGER", nullable: false),
                    DiskGB = table.Column<int>(type: "INTEGER", nullable: false),
                    Os = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Location = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Weight = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastPingAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LatencyMs = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CpuThreshold = table.Column<int>(type: "INTEGER", nullable: false),
                    RamThreshold = table.Column<int>(type: "INTEGER", nullable: false),
                    DiskThreshold = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerNodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DatabaseNodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DatabaseId = table.Column<int>(type: "INTEGER", nullable: false),
                    NodeId = table.Column<int>(type: "INTEGER", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatabaseNodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DatabaseNodes_Databases_DatabaseId",
                        column: x => x.DatabaseId,
                        principalTable: "Databases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DatabaseNodes_ServerNodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "ServerNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DomainNodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DomainId = table.Column<int>(type: "INTEGER", nullable: false),
                    NodeId = table.Column<int>(type: "INTEGER", nullable: false),
                    IsPrimary = table.Column<bool>(type: "INTEGER", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MigratedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DomainNodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DomainNodes_Domains_DomainId",
                        column: x => x.DomainId,
                        principalTable: "Domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DomainNodes_ServerNodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "ServerNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NodeAlerts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NodeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Severity = table.Column<int>(type: "INTEGER", nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    IsAcknowledged = table.Column<bool>(type: "INTEGER", nullable: false),
                    AcknowledgedBy = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    Escalated = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AcknowledgedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DedupeKey = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NodeAlerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NodeAlerts_ServerNodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "ServerNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ServerMetrics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NodeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CpuPercent = table.Column<double>(type: "REAL", nullable: false),
                    RamPercent = table.Column<double>(type: "REAL", nullable: false),
                    DiskPercent = table.Column<double>(type: "REAL", nullable: false),
                    NetworkInMbps = table.Column<double>(type: "REAL", nullable: false),
                    NetworkOutMbps = table.Column<double>(type: "REAL", nullable: false),
                    LoadAverage1 = table.Column<double>(type: "REAL", nullable: false),
                    LoadAverage5 = table.Column<double>(type: "REAL", nullable: false),
                    LoadAverage15 = table.Column<double>(type: "REAL", nullable: false),
                    ActiveConnections = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerMetrics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServerMetrics_ServerNodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "ServerNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ServerServices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NodeId = table.Column<int>(type: "INTEGER", nullable: false),
                    ServiceType = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    Port = table.Column<int>(type: "INTEGER", nullable: true),
                    LastCheckedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerServices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServerServices_ServerNodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "ServerNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserNodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    NodeId = table.Column<int>(type: "INTEGER", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserNodes_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserNodes_ServerNodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "ServerNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseNodes_DatabaseId",
                table: "DatabaseNodes",
                column: "DatabaseId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseNodes_NodeId",
                table: "DatabaseNodes",
                column: "NodeId");

            migrationBuilder.CreateIndex(
                name: "IX_DomainNodes_DomainId",
                table: "DomainNodes",
                column: "DomainId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DomainNodes_NodeId",
                table: "DomainNodes",
                column: "NodeId");

            migrationBuilder.CreateIndex(
                name: "IX_NodeAlerts_IsAcknowledged",
                table: "NodeAlerts",
                column: "IsAcknowledged");

            migrationBuilder.CreateIndex(
                name: "IX_NodeAlerts_NodeId_CreatedAt",
                table: "NodeAlerts",
                columns: new[] { "NodeId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ServerMetrics_NodeId_Timestamp",
                table: "ServerMetrics",
                columns: new[] { "NodeId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_ServerNodes_Name",
                table: "ServerNodes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServerServices_NodeId_ServiceType",
                table: "ServerServices",
                columns: new[] { "NodeId", "ServiceType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserNodes_NodeId",
                table: "UserNodes",
                column: "NodeId");

            migrationBuilder.CreateIndex(
                name: "IX_UserNodes_UserId",
                table: "UserNodes",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DatabaseNodes");

            migrationBuilder.DropTable(
                name: "DomainNodes");

            migrationBuilder.DropTable(
                name: "LoadBalancerSettings");

            migrationBuilder.DropTable(
                name: "NodeAlerts");

            migrationBuilder.DropTable(
                name: "ServerMetrics");

            migrationBuilder.DropTable(
                name: "ServerServices");

            migrationBuilder.DropTable(
                name: "UserNodes");

            migrationBuilder.DropTable(
                name: "ServerNodes");
        }
    }
}
