using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiEcommerce.Migrations
{
    /// <inheritdoc />
    public partial class OutboxPendingIndexIncludesAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_Pending",
                table: "OutboxMessages");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Pending",
                table: "OutboxMessages",
                column: "Sequence",
                filter: "[ProcessedAt] IS NULL")
                .Annotation("SqlServer:Include", new[] { "Attempts" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_Pending",
                table: "OutboxMessages");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Pending",
                table: "OutboxMessages",
                column: "Sequence",
                filter: "[ProcessedAt] IS NULL");
        }
    }
}
