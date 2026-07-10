using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DndMcpAICsharpFun.Migrations
{
    /// <inheritdoc />
    public partial class AddActiveCombatUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Combats_CampaignId_ActiveUnique",
                table: "Combats",
                column: "CampaignId",
                unique: true,
                filter: "\"Status\" = 'Active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Combats_CampaignId_ActiveUnique",
                table: "Combats");
        }
    }
}
