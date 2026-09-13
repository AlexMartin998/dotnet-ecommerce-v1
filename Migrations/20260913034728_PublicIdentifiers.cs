using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiEcommerce.Migrations
{
    /// <inheritdoc />
    public partial class PublicIdentifiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OrderPublicId",
                table: "Payments",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "PublicId",
                table: "Payments",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "PublicId",
                table: "Orders",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "Categories",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            // EF deja todas las filas que ya existian con Guid.Empty y '', y los indices
            // UNICOS de abajo no se podrian crear. El relleno va ANTES de ellos.
            //
            // Las filas viejas se quedan con un v4 (NEWID): lo publico no se ordena, y T-SQL
            // no genera v7. Las nuevas las pone el constructor de la entidad.
            migrationBuilder.Sql("UPDATE Orders SET PublicId = NEWID();");
            migrationBuilder.Sql("UPDATE Payments SET PublicId = NEWID();");

            // Copiado de la orden igual que OrderNumber: el cliente solo conoce este.
            migrationBuilder.Sql(@"
                UPDATE p SET p.OrderPublicId = o.PublicId
                FROM Payments p INNER JOIN Orders o ON o.Id = p.OrderId;");

            // El mismo slug que Slugs.From para lo que admite el DTO (letras, digitos y
            // espacios): minusculas y un guion por tanda de espacios. El truco de '<>'/'><'
            // colapsa las tandas sin expresiones regulares. Se comprobo antes de aplicarla que
            // ningun nombre existente tiene otros caracteres ni dos dan el mismo slug.
            migrationBuilder.Sql(@"
                UPDATE Categories
                SET Slug = LOWER(REPLACE(REPLACE(REPLACE(
                             LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(Name, CHAR(9), ' '), CHAR(10), ' '), CHAR(13), ' '))),
                             ' ', '<>'), '><', ''), '<>', '-'));");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PublicId",
                table: "Payments",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PublicId",
                table: "Orders",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Slug",
                table: "Categories",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payments_PublicId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Orders_PublicId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Categories_Slug",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "OrderPublicId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "Categories");
        }
    }
}
