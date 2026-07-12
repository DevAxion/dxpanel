using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SRXPanel.Data;

#nullable disable

namespace SRXPanel.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260712120000_PlanBackupsAndOffsite")]
    public partial class PlanBackupsAndOffsite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Plan-based backup limit (0 = unlimited); default 1 for existing rows.
            migrationBuilder.AddColumn<int>(
                name: "MaxBackups",
                table: "Packages",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            // Off-site (S3/Backblaze) backup tracking.
            migrationBuilder.AddColumn<bool>(
                name: "OffsiteStored",
                table: "Backups",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "OffsiteUploadedAt",
                table: "Backups",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "MaxBackups", table: "Packages");
            migrationBuilder.DropColumn(name: "OffsiteStored", table: "Backups");
            migrationBuilder.DropColumn(name: "OffsiteUploadedAt", table: "Backups");
        }
    }
}
