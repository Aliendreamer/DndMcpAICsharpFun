using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DndMcpAICsharpFun.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceCombatTurnIndexWithCombatantId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentTurnIndex",
                table: "Combats");

            migrationBuilder.AddColumn<long>(
                name: "CurrentTurnCombatantId",
                table: "Combats",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentTurnCombatantId",
                table: "Combats");

            migrationBuilder.AddColumn<int>(
                name: "CurrentTurnIndex",
                table: "Combats",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
