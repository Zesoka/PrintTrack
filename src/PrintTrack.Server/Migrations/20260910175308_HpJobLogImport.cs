using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintTrack.Server.Migrations
{
    /// <inheritdoc />
    public partial class HpJobLogImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PrintJobs_PrinterId",
                table: "PrintJobs");

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "PrintJobs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "PrintJobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SiteId",
                table: "Printers",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrintJobs_PrinterId_ExternalId",
                table: "PrintJobs",
                columns: new[] { "PrinterId", "ExternalId" },
                unique: true,
                filter: "\"ExternalId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Printers_SiteId",
                table: "Printers",
                column: "SiteId");

            migrationBuilder.AddForeignKey(
                name: "FK_Printers_Sites_SiteId",
                table: "Printers",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Printers_Sites_SiteId",
                table: "Printers");

            migrationBuilder.DropIndex(
                name: "IX_PrintJobs_PrinterId_ExternalId",
                table: "PrintJobs");

            migrationBuilder.DropIndex(
                name: "IX_Printers_SiteId",
                table: "Printers");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "PrintJobs");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "PrintJobs");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "Printers");

            migrationBuilder.CreateIndex(
                name: "IX_PrintJobs_PrinterId",
                table: "PrintJobs",
                column: "PrinterId");
        }
    }
}
