using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DndMcpAICsharpFun.Migrations
{
    /// <inheritdoc />
    public partial class AddBm25CorpusStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Bm25BookStats",
                columns: table => new
                {
                    FileHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DocumentCount = table.Column<long>(type: "bigint", nullable: false),
                    TotalTokenLength = table.Column<long>(type: "bigint", nullable: false),
                    TermDfJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bm25BookStats", x => x.FileHash);
                });

            migrationBuilder.CreateTable(
                name: "Bm25CorpusStats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    DocumentCount = table.Column<long>(type: "bigint", nullable: false),
                    TotalTokenLength = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bm25CorpusStats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Bm25TermStats",
                columns: table => new
                {
                    Term = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DocumentFrequency = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bm25TermStats", x => x.Term);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Bm25BookStats");

            migrationBuilder.DropTable(
                name: "Bm25CorpusStats");

            migrationBuilder.DropTable(
                name: "Bm25TermStats");
        }
    }
}
