using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRXPanel.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase5CustomerPortal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoresponderEnabled",
                table: "EmailAccounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AutoresponderMessage",
                table: "EmailAccounts",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "SpamThreshold",
                table: "EmailAccounts",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<bool>(
                name: "AutoRenewSsl",
                table: "Domains",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DirectoryListing",
                table: "Domains",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Error404Path",
                table: "Domains",
                type: "TEXT",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Error500Path",
                table: "Domains",
                type: "TEXT",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ForceHttps",
                table: "Domains",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "NotifyDiskUsage",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyInvoices",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NotifySslExpiry",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NotifySupport",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TimeZone",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ApiKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Prefix = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    KeyHash = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApiKeys_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Backups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Backups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Backups_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BackupSchedules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Frequency = table.Column<int>(type: "INTEGER", nullable: false),
                    Retention = table.Column<int>(type: "INTEGER", nullable: false),
                    Destination = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    S3Bucket = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastRunAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupSchedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CannedResponses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 5000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CannedResponses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DomainRedirects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DomainId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Target = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DomainRedirects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DomainRedirects_Domains_DomainId",
                        column: x => x.DomainId,
                        principalTable: "Domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Subdomains",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DomainId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    DocumentRoot = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subdomains", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subdomains_Domains_DomainId",
                        column: x => x.DomainId,
                        principalTable: "Domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Tickets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    AssignedToId = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tickets_AspNetUsers_AssignedToId",
                        column: x => x.AssignedToId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Tickets_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TicketReplies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TicketId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 5000, nullable: false),
                    IsStaff = table.Column<bool>(type: "INTEGER", nullable: false),
                    Attachments = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketReplies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TicketReplies_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TicketReplies_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_Prefix",
                table: "ApiKeys",
                column: "Prefix");

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_UserId",
                table: "ApiKeys",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Backups_UserId_CreatedAt",
                table: "Backups",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BackupSchedules_UserId",
                table: "BackupSchedules",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DomainRedirects_DomainId",
                table: "DomainRedirects",
                column: "DomainId");

            migrationBuilder.CreateIndex(
                name: "IX_Subdomains_DomainId_Name",
                table: "Subdomains",
                columns: new[] { "DomainId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TicketReplies_TicketId",
                table: "TicketReplies",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketReplies_UserId",
                table: "TicketReplies",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_AssignedToId",
                table: "Tickets",
                column: "AssignedToId");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_UserId_Status",
                table: "Tickets",
                columns: new[] { "UserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiKeys");

            migrationBuilder.DropTable(
                name: "Backups");

            migrationBuilder.DropTable(
                name: "BackupSchedules");

            migrationBuilder.DropTable(
                name: "CannedResponses");

            migrationBuilder.DropTable(
                name: "DomainRedirects");

            migrationBuilder.DropTable(
                name: "Subdomains");

            migrationBuilder.DropTable(
                name: "TicketReplies");

            migrationBuilder.DropTable(
                name: "Tickets");

            migrationBuilder.DropColumn(
                name: "AutoresponderEnabled",
                table: "EmailAccounts");

            migrationBuilder.DropColumn(
                name: "AutoresponderMessage",
                table: "EmailAccounts");

            migrationBuilder.DropColumn(
                name: "SpamThreshold",
                table: "EmailAccounts");

            migrationBuilder.DropColumn(
                name: "AutoRenewSsl",
                table: "Domains");

            migrationBuilder.DropColumn(
                name: "DirectoryListing",
                table: "Domains");

            migrationBuilder.DropColumn(
                name: "Error404Path",
                table: "Domains");

            migrationBuilder.DropColumn(
                name: "Error500Path",
                table: "Domains");

            migrationBuilder.DropColumn(
                name: "ForceHttps",
                table: "Domains");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "NotifyDiskUsage",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "NotifyInvoices",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "NotifySslExpiry",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "NotifySupport",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TimeZone",
                table: "AspNetUsers");
        }
    }
}
