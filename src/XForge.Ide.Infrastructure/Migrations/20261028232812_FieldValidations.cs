using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XForge.Ide.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FieldValidations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FieldValidations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FieldId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Data = table.Column<string>(type: "TEXT", nullable: true),
                    Message = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FieldValidations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FieldValidations_EntityFields_FieldId",
                        column: x => x.FieldId,
                        principalTable: "EntityFields",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FieldValidations_FieldId",
                table: "FieldValidations",
                column: "FieldId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FieldValidations");
        }
    }
}
