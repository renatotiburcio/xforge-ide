using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XForge.Ide.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IdeSessionAndIntellisense : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sessions",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    TabsJson = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    ActivePath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    PanelHeight = table.Column<double>(type: "REAL", nullable: false),
                    PanelOpen = table.Column<bool>(type: "INTEGER", nullable: false),
                    TerminalCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Key);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sessions");
        }
    }
}
