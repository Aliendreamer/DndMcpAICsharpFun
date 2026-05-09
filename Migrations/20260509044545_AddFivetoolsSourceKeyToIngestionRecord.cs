using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DndMcpAICsharpFun.Migrations
{
    /// <inheritdoc />
    public partial class AddFivetoolsSourceKeyToIngestionRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FivetoolsSourceKey",
                table: "IngestionRecords",
                type: "TEXT",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FivetoolsSourceKey",
                table: "IngestionRecords");
        }
    }
}
