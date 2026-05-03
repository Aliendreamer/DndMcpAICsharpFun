using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DndMcpAICsharpFun.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSourceNameFromIngestionRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SourceName",
                table: "IngestionRecords");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceName",
                table: "IngestionRecords",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");
        }
    }
}
