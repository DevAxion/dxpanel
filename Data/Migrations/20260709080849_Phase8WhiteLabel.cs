using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRXPanel.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase8WhiteLabel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LogoPath",
                table: "FrontendSettings",
                type: "TEXT",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowPoweredBy",
                table: "FrontendSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LogoPath",
                table: "FrontendSettings");

            migrationBuilder.DropColumn(
                name: "ShowPoweredBy",
                table: "FrontendSettings");
        }
    }
}
