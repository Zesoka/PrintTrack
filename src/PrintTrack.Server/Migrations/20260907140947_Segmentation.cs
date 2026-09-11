using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PrintTrack.Server.Migrations
{
    /// <inheritdoc />
    public partial class Segmentation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Department",
                table: "EndUsers");

            migrationBuilder.AddColumn<int>(
                name: "SiteId",
                table: "PrintJobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DepartmentId",
                table: "EndUsers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SiteId",
                table: "AgentApiKeys",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sites",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sites", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrintJobs_SiteId",
                table: "PrintJobs",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_EndUsers_DepartmentId",
                table: "EndUsers",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentApiKeys_SiteId",
                table: "AgentApiKeys",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_Name",
                table: "Departments",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sites_Name",
                table: "Sites",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AgentApiKeys_Sites_SiteId",
                table: "AgentApiKeys",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_EndUsers_Departments_DepartmentId",
                table: "EndUsers",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_PrintJobs_Sites_SiteId",
                table: "PrintJobs",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AgentApiKeys_Sites_SiteId",
                table: "AgentApiKeys");

            migrationBuilder.DropForeignKey(
                name: "FK_EndUsers_Departments_DepartmentId",
                table: "EndUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_PrintJobs_Sites_SiteId",
                table: "PrintJobs");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropTable(
                name: "Sites");

            migrationBuilder.DropIndex(
                name: "IX_PrintJobs_SiteId",
                table: "PrintJobs");

            migrationBuilder.DropIndex(
                name: "IX_EndUsers_DepartmentId",
                table: "EndUsers");

            migrationBuilder.DropIndex(
                name: "IX_AgentApiKeys_SiteId",
                table: "AgentApiKeys");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "PrintJobs");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "EndUsers");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "AgentApiKeys");

            migrationBuilder.AddColumn<string>(
                name: "Department",
                table: "EndUsers",
                type: "text",
                nullable: true);
        }
    }
}
