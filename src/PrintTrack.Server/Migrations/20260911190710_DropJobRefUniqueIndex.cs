using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintTrack.Server.Migrations
{
    /// <inheritdoc />
    public partial class DropJobRefUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PrintJobs_JobRef",
                table: "PrintJobs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PrintJobs_JobRef",
                table: "PrintJobs",
                column: "JobRef",
                unique: true);
        }
    }
}
