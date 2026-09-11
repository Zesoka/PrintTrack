using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PrintTrack.Server.Migrations
{
    /// <inheritdoc />
    public partial class SnmpMeters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MeterReadings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PrinterId = table.Column<int>(type: "integer", nullable: false),
                    TakenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    Total = table.Column<long>(type: "bigint", nullable: false),
                    Mono = table.Column<long>(type: "bigint", nullable: true),
                    Color = table.Column<long>(type: "bigint", nullable: true),
                    Note = table.Column<string>(type: "text", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeterReadings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MeterReadings_Printers_PrinterId",
                        column: x => x.PrinterId,
                        principalTable: "Printers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PrinterMeterConfigs",
                columns: table => new
                {
                    PrinterId = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Host = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Community = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SecurityName = table.Column<string>(type: "text", nullable: true),
                    AuthProtocol = table.Column<string>(type: "text", nullable: true),
                    AuthPassword = table.Column<string>(type: "text", nullable: true),
                    PrivProtocol = table.Column<string>(type: "text", nullable: true),
                    PrivPassword = table.Column<string>(type: "text", nullable: true),
                    OidTotal = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OidMono = table.Column<string>(type: "text", nullable: true),
                    OidColor = table.Column<string>(type: "text", nullable: true),
                    DeviceName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DeviceDescr = table.Column<string>(type: "text", nullable: true),
                    LastPolledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrinterMeterConfigs", x => x.PrinterId);
                    table.ForeignKey(
                        name: "FK_PrinterMeterConfigs_Printers_PrinterId",
                        column: x => x.PrinterId,
                        principalTable: "Printers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MeterReadings_PrinterId_TakenAt",
                table: "MeterReadings",
                columns: new[] { "PrinterId", "TakenAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MeterReadings");

            migrationBuilder.DropTable(
                name: "PrinterMeterConfigs");
        }
    }
}
