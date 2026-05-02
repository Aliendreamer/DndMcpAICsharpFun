using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DndMcpAICsharpFun.Migrations
{
    /// <inheritdoc />
    public partial class AddTocPageToIngestionRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TocPage",
                table: "IngestionRecords",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TocPage",
                table: "IngestionRecords");
        }
    }
}
