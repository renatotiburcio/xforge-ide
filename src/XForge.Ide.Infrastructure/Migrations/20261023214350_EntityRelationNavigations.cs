using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XForge.Ide.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EntityRelationNavigations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_EntityRelations_FromEntityId",
                table: "EntityRelations",
                column: "FromEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_EntityRelations_ToEntityId",
                table: "EntityRelations",
                column: "ToEntityId");

            migrationBuilder.AddForeignKey(
                name: "FK_EntityRelations_Entities_FromEntityId",
                table: "EntityRelations",
                column: "FromEntityId",
                principalTable: "Entities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EntityRelations_Entities_ToEntityId",
                table: "EntityRelations",
                column: "ToEntityId",
                principalTable: "Entities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EntityRelations_Entities_FromEntityId",
                table: "EntityRelations");

            migrationBuilder.DropForeignKey(
                name: "FK_EntityRelations_Entities_ToEntityId",
                table: "EntityRelations");

            migrationBuilder.DropIndex(
                name: "IX_EntityRelations_FromEntityId",
                table: "EntityRelations");

            migrationBuilder.DropIndex(
                name: "IX_EntityRelations_ToEntityId",
                table: "EntityRelations");
        }
    }
}
