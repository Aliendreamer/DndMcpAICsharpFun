using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DndMcpAICsharpFun.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignEditionAndBooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AllowedBookKeys",
                table: "Campaigns",
                type: "text",
                nullable: false,
                // "[]" not "" — the column holds JSON, and an empty string is not valid JSON. The
                // reader contains a parse failure to an empty list rather than throwing, but storing
                // an unparseable value would make every existing row silently "corrupt but tolerated".
                defaultValue: "[]");

            migrationBuilder.AddColumn<int>(
                name: "Edition",
                table: "Campaigns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Seed EXISTING campaigns with the books they can reach today, so their behaviour is
            // unchanged by this migration. The core-only default that new campaigns receive is
            // deliberately NOT applied here: retroactively it would silently remove thirteen books —
            // including all five setting books — from campaigns already using them.
            //
            // The key list is hardcoded ON PURPOSE. A migration is a frozen historical snapshot; it
            // must keep applying the 2026-07-27 state forever, so reading a live catalog here would
            // make old databases migrate differently from new ones. Only the Edition2014 books are
            // seeded — the revised core (XPHB/XDMG/XMM) is registered but not yet acquired, and
            // adding empty 2024 books to a 2014 campaign would widen its scope, not preserve it.
            migrationBuilder.Sql("""
                UPDATE "Campaigns"
                SET "AllowedBookKeys" = '["PHB","MM","DMG","XGE","TCE","ERLW","VRGR","FTD","BGG","EGW","SCC","GGR","SCAG","MTF","MPMM","EEPC"]',
                    "Edition" = 0
                WHERE "AllowedBookKeys" = '[]' OR "AllowedBookKeys" = '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowedBookKeys",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "Edition",
                table: "Campaigns");
        }
    }
}
