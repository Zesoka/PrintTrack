using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrintTrack.Server.Migrations
{
    /// <inheritdoc />
    public partial class JobLogAutoPull : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PrinterJobLogConfigs",
                columns: table => new
                {
                    PrinterId = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    BaseUrl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ReportPath = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ExportFormatId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Username = table.Column<string>(type: "text", nullable: true),
                    Password = table.Column<string>(type: "text", nullable: true),
                    LastPolledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    LastImported = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrinterJobLogConfigs", x => x.PrinterId);
                    table.ForeignKey(
                        name: "FK_PrinterJobLogConfigs_Printers_PrinterId",
                        column: x => x.PrinterId,
                        principalTable: "Printers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrinterJobLogConfigs");
        }
    }
}
