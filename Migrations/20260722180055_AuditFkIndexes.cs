using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DndMcpAICsharpFun.Migrations
{
    /// <inheritdoc />
    public partial class AuditFkIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_HeroSnapshots_HeroId",
                table: "HeroSnapshots",
                column: "HeroId");

            migrationBuilder.CreateIndex(
                name: "IX_Heroes_CampaignId",
                table: "Heroes",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_UserId",
                table: "Campaigns",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HeroSnapshots_HeroId",
                table: "HeroSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_Heroes_CampaignId",
                table: "Heroes");

            migrationBuilder.DropIndex(
                name: "IX_Campaigns_UserId",
                table: "Campaigns");
        }
    }
}
