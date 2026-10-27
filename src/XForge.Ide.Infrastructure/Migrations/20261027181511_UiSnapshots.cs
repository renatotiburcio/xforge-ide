using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XForge.Ide.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UiSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UiSnapshots",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Route = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Json = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UiSnapshots", x => x.Key);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UiSnapshots");
        }
    }
}
