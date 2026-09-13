using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiEcommerce.Migrations
{
    /// <inheritdoc />
    public partial class FeaturedCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FeaturedPosition",
                table: "Categories",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_FeaturedPosition",
                table: "Categories",
                column: "FeaturedPosition",
                unique: true,
                filter: "[FeaturedPosition] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Categories_FeaturedPosition",
                table: "Categories",
                sql: "[FeaturedPosition] BETWEEN 1 AND 3");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Categories_FeaturedPosition",
                table: "Categories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Categories_FeaturedPosition",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "FeaturedPosition",
                table: "Categories");
        }
    }
}
