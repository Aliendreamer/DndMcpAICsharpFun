using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DndMcpAICsharpFun.Migrations
{
    /// <inheritdoc />
    public partial class AddStructuredFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChoiceSetRows",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CanonicalId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    OptionsJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChoiceSetRows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StructuredTableRows",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TableId = table.Column<long>(type: "bigint", nullable: false),
                    RowIndex = table.Column<int>(type: "integer", nullable: false),
                    CellsJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StructuredTableRows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StructuredTables",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CanonicalId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ColumnsJson = table.Column<string>(type: "text", nullable: false),
                    SourceBook = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StructuredTables", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChoiceSetRows_CanonicalId",
                table: "ChoiceSetRows",
                column: "CanonicalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StructuredTableRows_TableId_RowIndex",
                table: "StructuredTableRows",
                columns: new[] { "TableId", "RowIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StructuredTables_CanonicalId",
                table: "StructuredTables",
                column: "CanonicalId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChoiceSetRows");

            migrationBuilder.DropTable(
                name: "StructuredTableRows");

            migrationBuilder.DropTable(
                name: "StructuredTables");
        }
    }
}
