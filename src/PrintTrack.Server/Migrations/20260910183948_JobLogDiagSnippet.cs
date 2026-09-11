using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintTrack.Server.Migrations
{
    /// <inheritdoc />
    public partial class JobLogDiagSnippet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastResponseSnippet",
                table: "PrinterJobLogConfigs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastResponseSnippet",
                table: "PrinterJobLogConfigs");
        }
    }
}
