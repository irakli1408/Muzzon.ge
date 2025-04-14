using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Muzzon.ge.Migrations
{
    /// <inheritdoc />
    public partial class addedIpAddressToLogTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IpAddress",
                table: "ErrorLogs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IpAddress",
                table: "DownloadLogs",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IpAddress",
                table: "ErrorLogs");

            migrationBuilder.DropColumn(
                name: "IpAddress",
                table: "DownloadLogs");
        }
    }
}
