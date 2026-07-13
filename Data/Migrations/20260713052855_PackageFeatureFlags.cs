using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRXPanel.Data.Migrations
{
    /// <inheritdoc />
    public partial class PackageFeatureFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowAdvancedMail",
                table: "ResellerPackages",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowAppHosting",
                table: "ResellerPackages",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowCloudflare",
                table: "ResellerPackages",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowDeveloperTools",
                table: "ResellerPackages",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowVpsStore",
                table: "ResellerPackages",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowAdvancedMail",
                table: "Packages",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowAppHosting",
                table: "Packages",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowCloudflare",
                table: "Packages",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowDeveloperTools",
                table: "Packages",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowVpsStore",
                table: "Packages",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowAdvancedMail",
                table: "ResellerPackages");

            migrationBuilder.DropColumn(
                name: "AllowAppHosting",
                table: "ResellerPackages");

            migrationBuilder.DropColumn(
                name: "AllowCloudflare",
                table: "ResellerPackages");

            migrationBuilder.DropColumn(
                name: "AllowDeveloperTools",
                table: "ResellerPackages");

            migrationBuilder.DropColumn(
                name: "AllowVpsStore",
                table: "ResellerPackages");

            migrationBuilder.DropColumn(
                name: "AllowAdvancedMail",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "AllowAppHosting",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "AllowCloudflare",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "AllowDeveloperTools",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "AllowVpsStore",
                table: "Packages");
        }
    }
}
