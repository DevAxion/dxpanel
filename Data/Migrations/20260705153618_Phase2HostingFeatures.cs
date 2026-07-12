using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRXPanel.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase2HostingFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Databases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    DomainId = table.Column<int>(type: "INTEGER", nullable: true),
                    DbName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DbUser = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DbPasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    DbSize = table.Column<double>(type: "REAL", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Databases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Databases_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Databases_Domains_DomainId",
                        column: x => x.DomainId,
                        principalTable: "Domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "DnsZones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DomainId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DnsZones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DnsZones_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DnsZones_Domains_DomainId",
                        column: x => x.DomainId,
                        principalTable: "Domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmailAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    DomainId = table.Column<int>(type: "INTEGER", nullable: true),
                    EmailAddress = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    QuotaMB = table.Column<long>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailAccounts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmailAccounts_Domains_DomainId",
                        column: x => x.DomainId,
                        principalTable: "Domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "EmailForwarders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    DomainId = table.Column<int>(type: "INTEGER", nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Destination = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailForwarders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailForwarders_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmailForwarders_Domains_DomainId",
                        column: x => x.DomainId,
                        principalTable: "Domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "FtpAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    DomainId = table.Column<int>(type: "INTEGER", nullable: true),
                    Username = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    HomeDirectory = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    QuotaMB = table.Column<long>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FtpAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FtpAccounts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FtpAccounts_Domains_DomainId",
                        column: x => x.DomainId,
                        principalTable: "Domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    IsRead = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DedupeKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SslCertificates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DomainId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CertificatePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    KeyPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SslCertificates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SslCertificates_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SslCertificates_Domains_DomainId",
                        column: x => x.DomainId,
                        principalTable: "Domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DnsRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ZoneId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    TTL = table.Column<int>(type: "INTEGER", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DnsRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DnsRecords_DnsZones_ZoneId",
                        column: x => x.ZoneId,
                        principalTable: "DnsZones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Databases_DbName",
                table: "Databases",
                column: "DbName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Databases_DomainId",
                table: "Databases",
                column: "DomainId");

            migrationBuilder.CreateIndex(
                name: "IX_Databases_UserId",
                table: "Databases",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_DnsRecords_ZoneId",
                table: "DnsRecords",
                column: "ZoneId");

            migrationBuilder.CreateIndex(
                name: "IX_DnsZones_DomainId",
                table: "DnsZones",
                column: "DomainId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DnsZones_UserId",
                table: "DnsZones",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailAccounts_DomainId",
                table: "EmailAccounts",
                column: "DomainId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailAccounts_EmailAddress",
                table: "EmailAccounts",
                column: "EmailAddress",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailAccounts_UserId",
                table: "EmailAccounts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailForwarders_DomainId",
                table: "EmailForwarders",
                column: "DomainId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailForwarders_UserId",
                table: "EmailForwarders",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_FtpAccounts_DomainId",
                table: "FtpAccounts",
                column: "DomainId");

            migrationBuilder.CreateIndex(
                name: "IX_FtpAccounts_UserId",
                table: "FtpAccounts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_FtpAccounts_Username",
                table: "FtpAccounts",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_IsRead",
                table: "Notifications",
                columns: new[] { "UserId", "IsRead" });

            migrationBuilder.CreateIndex(
                name: "IX_SslCertificates_DomainId",
                table: "SslCertificates",
                column: "DomainId");

            migrationBuilder.CreateIndex(
                name: "IX_SslCertificates_UserId",
                table: "SslCertificates",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Databases");

            migrationBuilder.DropTable(
                name: "DnsRecords");

            migrationBuilder.DropTable(
                name: "EmailAccounts");

            migrationBuilder.DropTable(
                name: "EmailForwarders");

            migrationBuilder.DropTable(
                name: "FtpAccounts");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "SslCertificates");

            migrationBuilder.DropTable(
                name: "DnsZones");
        }
    }
}
