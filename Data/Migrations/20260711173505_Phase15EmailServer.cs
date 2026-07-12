using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRXPanel.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase15EmailServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BlacklistChecks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DomainId = table.Column<int>(type: "INTEGER", nullable: true),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    CheckType = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ListedOn = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CheckedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlacklistChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BlacklistChecks_Domains_DomainId",
                        column: x => x.DomainId,
                        principalTable: "Domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BlacklistEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    BlacklistName = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    IsListed = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastCheckedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FirstDetectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsResolved = table.Column<bool>(type: "INTEGER", nullable: false),
                    DomainId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlacklistEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BlacklistEntries_Domains_DomainId",
                        column: x => x.DomainId,
                        principalTable: "Domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "EmailBounces",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DomainId = table.Column<int>(type: "INTEGER", nullable: false),
                    EmailAddress = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    BounceType = table.Column<int>(type: "INTEGER", nullable: false),
                    BounceReason = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsBlacklisted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailBounces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailBounces_Domains_DomainId",
                        column: x => x.DomainId,
                        principalTable: "Domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmailLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    DomainId = table.Column<int>(type: "INTEGER", nullable: true),
                    FromAddress = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    ToAddress = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    MessageId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    SpamScore = table.Column<double>(type: "REAL", nullable: false),
                    DeliveredAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailLogs_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmailLogs_Domains_DomainId",
                        column: x => x.DomainId,
                        principalTable: "Domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "EmailQueues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    DomainId = table.Column<int>(type: "INTEGER", nullable: true),
                    FromAddress = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    ToAddress = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SentAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailQueues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailQueues_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmailQueues_Domains_DomainId",
                        column: x => x.DomainId,
                        principalTable: "Domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "EmailQueueStats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DomainId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TotalSent = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalFailed = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalDeferred = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalBounced = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalSpam = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailQueueStats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailQueueStats_Domains_DomainId",
                        column: x => x.DomainId,
                        principalTable: "Domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MailServerConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DomainId = table.Column<int>(type: "INTEGER", nullable: false),
                    SmtpHost = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SmtpPort = table.Column<int>(type: "INTEGER", nullable: false),
                    ImapHost = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ImapPort = table.Column<int>(type: "INTEGER", nullable: false),
                    Pop3Host = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Pop3Port = table.Column<int>(type: "INTEGER", nullable: false),
                    RequireAuth = table.Column<bool>(type: "INTEGER", nullable: false),
                    MaxMailboxSize = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxAttachmentSize = table.Column<int>(type: "INTEGER", nullable: false),
                    SpamThreshold = table.Column<double>(type: "REAL", nullable: false),
                    SpamRetentionDays = table.Column<int>(type: "INTEGER", nullable: false),
                    QuarantineEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    QueuePaused = table.Column<bool>(type: "INTEGER", nullable: false),
                    BlacklistAutoCheck = table.Column<bool>(type: "INTEGER", nullable: false),
                    BlacklistSchedule = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AlertOnBlacklist = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastBlacklistCheckAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AutoBlacklistBounces = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailServerConfigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MailServerConfigs_Domains_DomainId",
                        column: x => x.DomainId,
                        principalTable: "Domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BlacklistChecks_DomainId_CheckedAt",
                table: "BlacklistChecks",
                columns: new[] { "DomainId", "CheckedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BlacklistEntries_DomainId",
                table: "BlacklistEntries",
                column: "DomainId");

            migrationBuilder.CreateIndex(
                name: "IX_BlacklistEntries_Value_BlacklistName",
                table: "BlacklistEntries",
                columns: new[] { "Value", "BlacklistName" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailBounces_DomainId_OccurredAt",
                table: "EmailBounces",
                columns: new[] { "DomainId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailLogs_DomainId",
                table: "EmailLogs",
                column: "DomainId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailLogs_MessageId",
                table: "EmailLogs",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailLogs_UserId_CreatedAt",
                table: "EmailLogs",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailQueues_CreatedAt",
                table: "EmailQueues",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_EmailQueues_DomainId",
                table: "EmailQueues",
                column: "DomainId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailQueues_UserId_Status",
                table: "EmailQueues",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailQueueStats_DomainId_Date",
                table: "EmailQueueStats",
                columns: new[] { "DomainId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_MailServerConfigs_DomainId",
                table: "MailServerConfigs",
                column: "DomainId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlacklistChecks");

            migrationBuilder.DropTable(
                name: "BlacklistEntries");

            migrationBuilder.DropTable(
                name: "EmailBounces");

            migrationBuilder.DropTable(
                name: "EmailLogs");

            migrationBuilder.DropTable(
                name: "EmailQueues");

            migrationBuilder.DropTable(
                name: "EmailQueueStats");

            migrationBuilder.DropTable(
                name: "MailServerConfigs");
        }
    }
}
