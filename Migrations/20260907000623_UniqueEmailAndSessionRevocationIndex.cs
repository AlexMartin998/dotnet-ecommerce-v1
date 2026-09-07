using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiEcommerce.Migrations
{
    /// <inheritdoc />
    public partial class UniqueEmailAndSessionRevocationIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "EmailIndex",
                table: "AspNetUsers");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_User_RevokedAt",
                table: "RefreshTokens",
                columns: new[] { "UserId", "RevokedAt" });

            // Sin esto el índice no se crea si la carrera ya dejó emails repetidos. No se borra
            // ninguna cuenta: se libera el email de las copias y la más antigua se lo queda.
            migrationBuilder.Sql("""
                WITH duplicated AS (
                    SELECT Id,
                           ROW_NUMBER() OVER (PARTITION BY NormalizedEmail ORDER BY CreatedAt, Id) AS Copy
                    FROM AspNetUsers
                    WHERE NormalizedEmail IS NOT NULL
                )
                UPDATE u
                SET u.Email = NULL, u.NormalizedEmail = NULL, u.EmailConfirmed = 0
                FROM AspNetUsers u
                INNER JOIN duplicated d ON d.Id = u.Id
                WHERE d.Copy > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail",
                unique: true,
                filter: "[NormalizedEmail] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_User_RevokedAt",
                table: "RefreshTokens");

            migrationBuilder.DropIndex(
                name: "EmailIndex",
                table: "AspNetUsers");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");
        }
    }
}
