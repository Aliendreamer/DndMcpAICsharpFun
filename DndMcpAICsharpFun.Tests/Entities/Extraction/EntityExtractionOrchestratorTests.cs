using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction;

public class EntityExtractionOrchestratorTests
{
    [Fact]
    public void Type_resolves_to_concrete_orchestrator()
    {
        // Smoke test — happy-path test is deferred (needs fake Docling output).
        // Confirms the type is wired and the IEntityExtractionOrchestrator interface exists.
        typeof(IEntityExtractionOrchestrator).Should().NotBeNull();
        typeof(EntityExtractionOrchestrator).Should().Implement<IEntityExtractionOrchestrator>();
    }

    [Fact]
    public async Task NoSchema_candidate_is_written_to_errors_json_with_no_schema_kind()
    {
        // Arrange
        var canonicalDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var schemasDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(canonicalDir);
        Directory.CreateDirectory(schemasDir);    // intentionally empty — no schema files

        try
        {
            const int bookId = 42;
            const string displayName = "Test Book";
            const string version = "5e";

            var record = new DndMcpAICsharpFun.Infrastructure.Sqlite.IngestionRecord
            {
                Id = bookId,
                FilePath = "/dev/null",
                FileName = "test.pdf",
                FileHash = "abc123",
                Version = version,
                DisplayName = displayName,
            };

            // Fake tracker: returns the record and accepts all mark calls.
            var tracker = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Tracking.IIngestionTracker>();
            tracker.GetByIdAsync(bookId, Arg.Any<CancellationToken>()).Returns(record);

            // Fake docling: returns one text item at page 1.
            var docling = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Pdf.IDoclingPdfConverter>();
            var doclingDoc = new DndMcpAICsharpFun.Features.Ingestion.Pdf.DoclingDocument(
                "# Aboleth\nA monster.",
                new List<DndMcpAICsharpFun.Features.Ingestion.Pdf.DoclingItem>
                {
                    new("text", "Aboleth — a slimy aberration.", 1, null),
                });
            docling.ConvertAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(doclingDoc);

            // Fake bookmark reader: returns one bookmark that maps page 1 to Monsters.
            var bookmarkReader = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Pdf.IPdfBookmarkReader>();
            bookmarkReader.ReadBookmarks(Arg.Any<string>()).Returns(
                new List<DndMcpAICsharpFun.Features.Ingestion.Pdf.PdfBookmark>
                {
                    new("Monsters", 1),
                });

            // Fake LLM client — should never be called (no schema means we short-circuit).
            var llm = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.IEntityExtractionLlmClient>();

            var opts = Options.Create(new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityExtractionOptions
            {
                CanonicalDirectory = canonicalDir,
                SchemasDirectory = schemasDir,   // empty → LoadSchemas returns {}
            });
            var ollamaOpts = Options.Create(new DndMcpAICsharpFun.Infrastructure.Ollama.OllamaOptions());

            var orchestrator = new EntityExtractionOrchestrator(
                tracker: tracker,
                docling: docling,
                bookmarks: bookmarkReader,
                llm: llm,
                promptBuilder: new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionPromptBuilder(),
                scanner: new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityCandidateScanner(),
                writer: new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.CanonicalJsonWriter(),
                errorsFile: new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionErrorsFile(),
                warningsFile: new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionWarningsFile(),
                refResolver: new DndMcpAICsharpFun.Features.Entities.EntityReferenceResolver(),
                retry: new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionRetryPolicy { MaxAttempts = 1 },
                options: opts,
                ollamaOpts: ollamaOpts,
                logger: NullLogger<EntityExtractionOrchestrator>.Instance);

            // Act
            await orchestrator.ExtractAsync(bookId, force: true, ct: CancellationToken.None);

            // Assert: errors file must exist and contain a no_schema entry.
            var bookSlug = DndMcpAICsharpFun.Domain.Entities.EntityIdSlug
                .For(displayName, DndMcpAICsharpFun.Domain.Entities.EntityType.Class, "x")
                .Split('.')[0];
            var errorsPath = Path.Combine(canonicalDir, bookSlug + ".errors.json");

            File.Exists(errorsPath).Should().BeTrue("errors.json must be written when a no_schema skip occurs");

            var json = await File.ReadAllTextAsync(errorsPath);
            var errors = System.Text.Json.JsonSerializer.Deserialize<
                List<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionErrorEntry>>(
                json,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

            errors.Should().NotBeNullOrEmpty();
            errors!.Should().ContainSingle(e => e.ErrorKind == "no_schema",
                "one candidate was present and had no schema");
            var entry = errors!.Single(e => e.ErrorKind == "no_schema");
            entry.FieldPath.Should().Be("(type)");
            entry.MissingTargetId.Should().Be(string.Empty);
        }
        finally
        {
            Directory.Delete(canonicalDir, true);
            Directory.Delete(schemasDir, true);
        }
    }
}
