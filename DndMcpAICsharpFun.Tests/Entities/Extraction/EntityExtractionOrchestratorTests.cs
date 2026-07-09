using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Tests.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.Logging;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction;

public class EntityExtractionOrchestratorTests
{
    [Fact]
    public void Type_resolves_to_concrete_orchestrator()
    {
        // Smoke test — happy-path test is deferred (needs fake converter output).
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

            var record = new DndMcpAICsharpFun.Domain.IngestionRecord
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

            // Fake converter: returns one text item at page 1.
            var converter = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Pdf.IPdfStructureConverter>();
            var converterDoc = new DndMcpAICsharpFun.Features.Ingestion.Pdf.PdfStructureDocument(
                "# Aboleth\nA monster.",
                new List<DndMcpAICsharpFun.Features.Ingestion.Pdf.PdfStructureItem>
                {
                    new("text", "Aboleth — a slimy aberration.", 1, null),
                });
            converter.ConvertAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(converterDoc);

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

            var sharedMatcher = new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityNameMatcher(
                new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityNameIndex(
                    Path.Combine(Path.GetTempPath(), "__nonexistent_5etools__")));
            var orchestrator = new EntityExtractionOrchestrator(
                tracker: tracker,
                registry: new DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.BookSourceRegistry(
                    Path.Combine(Path.GetTempPath(), "__nonexistent_books__.json")),
                converter: converter,
                candidateBuilder: new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityCandidateBuilder(
                    bookmarks: bookmarkReader,
                    scanner: new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityCandidateScanner(NullLogger<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityCandidateScanner>.Instance),
                    statBlockScanner: new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.StatBlockScanner(),
                    logger: NullLogger<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityCandidateBuilder>.Instance,
                    matcher: sharedMatcher),
                writer: new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.CanonicalJsonWriter(),
                errorsFile: new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionErrorsFile(),
                warningsFile: new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionWarningsFile(),
                declinedFile: new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionDeclinedFile(),
                refResolver: new DndMcpAICsharpFun.Features.Entities.EntityReferenceResolver(),
                schemaProvider: new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntitySchemaProvider(
                    opts, NullLogger<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntitySchemaProvider>.Instance),
                checkpointStore: new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionCheckpointStore(),
                runner: new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityExtractionRunner(
                    candidateExtractor: BuildCandidateExtractor(llm, opts),
                    logger: NullLogger<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityExtractionRunner>.Instance,
                    cascade: GroundingCascadeTestFactory.Inert(),
                    matcher: sharedMatcher),
                options: opts,
                logger: NullLogger<EntityExtractionOrchestrator>.Instance,
                matcher: sharedMatcher);

            // Act
            await orchestrator.ExtractAsync(bookId, force: true, errorsOnly: false, ct: CancellationToken.None);

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

    // ─────────────────────────────────────────────────────────────────────────
    //  Task 4 — errorsOnly orchestrator tests
    // ─────────────────────────────────────────────────────────────────────────

    private static (
        string canonicalDir,
        string schemasDir,
        DndMcpAICsharpFun.Domain.IngestionRecord record,
        DndMcpAICsharpFun.Features.Ingestion.Tracking.IIngestionTracker tracker,
        DndMcpAICsharpFun.Features.Ingestion.Pdf.IPdfStructureConverter converter,
        DndMcpAICsharpFun.Features.Ingestion.Pdf.IPdfBookmarkReader bookmarkReader,
        DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.IEntityExtractionLlmClient llm)
        BuildTwoMonsterHarness(int bookId, string displayName)
    {
        var canonicalDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var schemasDir   = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(canonicalDir);
        Directory.CreateDirectory(schemasDir);

        // Write a minimal Monster schema so LLM is invoked for Monster candidates.
        File.WriteAllText(
            Path.Combine(schemasDir, "MonsterFields.schema.json"),
            "{ \"type\": \"object\" }");

        var record = new DndMcpAICsharpFun.Domain.IngestionRecord
        {
            Id          = bookId,
            FilePath    = "/dev/null",
            FileName    = "test.pdf",
            FileHash    = "abc123",
            Version     = "5e",
            DisplayName = displayName,
        };

        var tracker = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Tracking.IIngestionTracker>();
        tracker.GetByIdAsync(bookId, Arg.Any<CancellationToken>()).Returns(record);

        // Two heading/text pairs → two candidates: "Aboleth" and "Beholder".
        var converter = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Pdf.IPdfStructureConverter>();
        var converterDoc = new DndMcpAICsharpFun.Features.Ingestion.Pdf.PdfStructureDocument(
            "doc",
            new List<DndMcpAICsharpFun.Features.Ingestion.Pdf.PdfStructureItem>
            {
                new("heading", "Aboleth", 1, null),
                new("text",    "Aboleth — a slimy aberration.", 1, null),
                new("heading", "Beholder", 2, null),
                new("text",    "Beholder — a tyrant.", 2, null),
            });
        converter.ConvertAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(converterDoc);

        var bookmarkReader = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Pdf.IPdfBookmarkReader>();
        bookmarkReader.ReadBookmarks(Arg.Any<string>()).Returns(
            new List<DndMcpAICsharpFun.Features.Ingestion.Pdf.PdfBookmark>
            {
                new("Monsters", 1),
                new("Monsters", 2),
            });

        var llm = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.IEntityExtractionLlmClient>();

        return (canonicalDir, schemasDir, record, tracker, converter, bookmarkReader, llm);
    }

    private static EntityExtractionOrchestrator BuildOrchestrator(
        string canonicalDir,
        string schemasDir,
        DndMcpAICsharpFun.Features.Ingestion.Tracking.IIngestionTracker tracker,
        DndMcpAICsharpFun.Features.Ingestion.Pdf.IPdfStructureConverter converter,
        DndMcpAICsharpFun.Features.Ingestion.Pdf.IPdfBookmarkReader bookmarkReader,
        DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.IEntityExtractionLlmClient llm,
        DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.BookSourceRegistry? registry = null,
        DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityNameMatcher? matcher = null,
        ILogger<EntityExtractionOrchestrator>? orchestratorLogger = null,
        ILogger<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityCandidateBuilder>? builderLogger = null)
    {
        var opts = Options.Create(new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityExtractionOptions
        {
            CanonicalDirectory = canonicalDir,
            SchemasDirectory   = schemasDir,
        });
        // Use an empty registry (no books.json) when caller does not supply one.
        var effectiveRegistry = registry
            ?? new DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.BookSourceRegistry(
                   Path.Combine(Path.GetTempPath(), "__nonexistent_books__.json"));
        // Use an empty-index matcher (no 5etools data) when caller does not supply one.
        var effectiveMatcher = matcher
            ?? new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityNameMatcher(
                   new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityNameIndex(
                       Path.Combine(Path.GetTempPath(), "__nonexistent_5etools__")));

        return new EntityExtractionOrchestrator(
            tracker:            tracker,
            registry:           effectiveRegistry,
            converter:          converter,
            candidateBuilder:   new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityCandidateBuilder(
                bookmarks:        bookmarkReader,
                scanner:          new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityCandidateScanner(NullLogger<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityCandidateScanner>.Instance),
                statBlockScanner: new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.StatBlockScanner(),
                logger:           builderLogger ?? NullLogger<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityCandidateBuilder>.Instance,
                matcher:          effectiveMatcher),
            writer:             new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.CanonicalJsonWriter(),
            errorsFile:         new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionErrorsFile(),
            warningsFile:       new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionWarningsFile(),
            declinedFile:       new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionDeclinedFile(),
            refResolver:        new DndMcpAICsharpFun.Features.Entities.EntityReferenceResolver(),
            schemaProvider:     new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntitySchemaProvider(
                opts, NullLogger<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntitySchemaProvider>.Instance),
            checkpointStore:    new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionCheckpointStore(),
            runner:             new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityExtractionRunner(
                candidateExtractor: BuildCandidateExtractor(llm, opts),
                logger:             NullLogger<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityExtractionRunner>.Instance,
                cascade:            GroundingCascadeTestFactory.Inert(),
                matcher:            effectiveMatcher),
            options:            opts,
            logger:             orchestratorLogger ?? NullLogger<EntityExtractionOrchestrator>.Instance,
            matcher:            effectiveMatcher);
    }

    private static DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.CandidateExtractor BuildCandidateExtractor(
        DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.IEntityExtractionLlmClient llm,
        Microsoft.Extensions.Options.IOptions<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityExtractionOptions> opts)
    {
        var ollamaOpts = Options.Create(new DndMcpAICsharpFun.Infrastructure.Ollama.OllamaOptions());
        return new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.CandidateExtractor(
            llm:           llm,
            promptBuilder: new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionPromptBuilder(),
            chunker:       new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.SemanticChunker(),
            merger:        new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityFieldMerger(),
            retry:         new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionRetryPolicy { MaxAttempts = 1 },
            options:       opts,
            ollamaOpts:    ollamaOpts,
            logger:        NullLogger<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.CandidateExtractor>.Instance);
    }

    [Fact]
    public async Task ErrorsOnly_skips_candidates_not_in_retry_set()
    {
        const int bookId = 100;
        const string displayName = "Test Book";

        var (canonicalDir, schemasDir, record, tracker, converter, bookmarkReader, llm)
            = BuildTwoMonsterHarness(bookId, displayName);

        try
        {
            var bookSlug      = "test-book";
            var canonicalPath = Path.Combine(canonicalDir, bookSlug + ".json");
            var errorsPath    = Path.Combine(canonicalDir, bookSlug + ".errors.json");

            // Pre-existing canonical with zero entities.
            var existing = new DndMcpAICsharpFun.Domain.Entities.CanonicalJsonFile(
                SchemaVersion: DndMcpAICsharpFun.Domain.Entities.CanonicalJsonSchema.CurrentVersion,
                Book: new DndMcpAICsharpFun.Domain.Entities.CanonicalBookMetadata(
                    SourceBook: displayName, Edition: "5e", FileHash: "abc123", DisplayName: displayName),
                Entities: new List<DndMcpAICsharpFun.Domain.Entities.EntityEnvelope>());

            await File.WriteAllTextAsync(
                canonicalPath,
                System.Text.Json.JsonSerializer.Serialize(existing,
                    new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
                    {
                        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                    }));

            // Errors file with ONLY the Aboleth ID — Beholder is not in the retry set.
            var aboleth = "test-book.monster.aboleth";
            var prevErrors = new List<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionErrorEntry>
            {
                new(SourceEntityId: aboleth,
                    FieldPath: "(extraction)",
                    MissingTargetId: string.Empty,
                    ErrorKind: "extraction_failure",
                    Detail: "previous failure"),
            };
            await File.WriteAllTextAsync(
                errorsPath,
                System.Text.Json.JsonSerializer.Serialize(prevErrors,
                    new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));

            // LLM returns success — but should only be called once.
            using var fields = System.Text.Json.JsonDocument.Parse("{}");
            llm.ExtractAsync(Arg.Any<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionRequest>(),
                             Arg.Any<CancellationToken>())
               .Returns(new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionResponse(
                   Success: true,
                   ToolInput: fields.RootElement.Clone(),
                   StopReason: "tool_use",
                   InputTokens: 0,
                   OutputTokens: 0,
                   ErrorMessage: null,
                   RawJson: null));

            var orchestrator = BuildOrchestrator(canonicalDir, schemasDir, tracker, converter, bookmarkReader, llm);

            // Act
            await orchestrator.ExtractAsync(bookId, force: false, errorsOnly: true, ct: CancellationToken.None);

            // Assert: LLM was called exactly once.
            await llm.Received(1).ExtractAsync(
                Arg.Any<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionRequest>(),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            try { Directory.Delete(canonicalDir, true); } catch { }
            try { Directory.Delete(schemasDir,   true); } catch { }
        }
    }

    [Fact]
    public async Task ErrorsOnly_merges_new_entity_into_existing_canonical()
    {
        const int bookId = 101;
        const string displayName = "Test Book";

        var (canonicalDir, schemasDir, record, tracker, converter, bookmarkReader, llm)
            = BuildTwoMonsterHarness(bookId, displayName);

        try
        {
            var bookSlug      = "test-book";
            var canonicalPath = Path.Combine(canonicalDir, bookSlug + ".json");
            var errorsPath    = Path.Combine(canonicalDir, bookSlug + ".errors.json");

            // Existing canonical contains 1 entity (Beholder).
            using var emptyFields = System.Text.Json.JsonDocument.Parse("{}");
            var existingEntity = new DndMcpAICsharpFun.Domain.Entities.EntityEnvelope(
                Id:              "test-book.monster.beholder",
                Type:            DndMcpAICsharpFun.Domain.Entities.EntityType.Monster,
                Name:            "Beholder",
                SourceBook:      displayName,
                Edition:         "5e",
                Page:            2,
                FirstAppearedIn: new DndMcpAICsharpFun.Domain.Entities.FirstAppearance(displayName, "5e", 2),
                RevisedIn:       Array.Empty<DndMcpAICsharpFun.Domain.Entities.Revision>(),
                SettingTags:     Array.Empty<string>(),
                CanonicalText:   string.Empty,
                Fields:          emptyFields.RootElement.Clone());

            var existing = new DndMcpAICsharpFun.Domain.Entities.CanonicalJsonFile(
                SchemaVersion: DndMcpAICsharpFun.Domain.Entities.CanonicalJsonSchema.CurrentVersion,
                Book: new DndMcpAICsharpFun.Domain.Entities.CanonicalBookMetadata(
                    SourceBook: displayName, Edition: "5e", FileHash: "abc123", DisplayName: displayName),
                Entities: new List<DndMcpAICsharpFun.Domain.Entities.EntityEnvelope> { existingEntity });

            var jsonOpts = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
            {
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            };
            await File.WriteAllTextAsync(canonicalPath, System.Text.Json.JsonSerializer.Serialize(existing, jsonOpts));

            // errors.json points at Aboleth — that one will be re-extracted.
            var prevErrors = new List<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionErrorEntry>
            {
                new(SourceEntityId: "test-book.monster.aboleth",
                    FieldPath: "(extraction)",
                    MissingTargetId: string.Empty,
                    ErrorKind: "extraction_failure",
                    Detail: null),
            };
            await File.WriteAllTextAsync(errorsPath, System.Text.Json.JsonSerializer.Serialize(prevErrors,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));

            // LLM returns success for the retried Aboleth.
            using var fields = System.Text.Json.JsonDocument.Parse("{}");
            llm.ExtractAsync(Arg.Any<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionRequest>(),
                             Arg.Any<CancellationToken>())
               .Returns(new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionResponse(
                   Success: true,
                   ToolInput: fields.RootElement.Clone(),
                   StopReason: "tool_use",
                   InputTokens: 0,
                   OutputTokens: 0,
                   ErrorMessage: null,
                   RawJson: null));

            var orchestrator = BuildOrchestrator(canonicalDir, schemasDir, tracker, converter, bookmarkReader, llm);

            // Act
            await orchestrator.ExtractAsync(bookId, force: false, errorsOnly: true, ct: CancellationToken.None);

            // Assert: canonical now contains 2 entities (Beholder + the re-extracted Aboleth).
            var mergedJson = await File.ReadAllTextAsync(canonicalPath);
            var merged = System.Text.Json.JsonSerializer.Deserialize<DndMcpAICsharpFun.Domain.Entities.CanonicalJsonFile>(
                mergedJson,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
                {
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                });

            merged.Should().NotBeNull();
            merged!.Entities.Should().HaveCount(2);
            merged.Entities.Should().Contain(e => e.Id == "test-book.monster.beholder");
            merged.Entities.Should().Contain(e => e.Id == "test-book.monster.aboleth");
        }
        finally
        {
            try { Directory.Delete(canonicalDir, true); } catch { }
            try { Directory.Delete(schemasDir,   true); } catch { }
        }
    }

    [Fact]
    public async Task ErrorsOnly_with_no_errors_file_returns_early_without_calling_llm()
    {
        const int bookId = 102;
        const string displayName = "Test Book";

        var (canonicalDir, schemasDir, record, tracker, converter, bookmarkReader, llm)
            = BuildTwoMonsterHarness(bookId, displayName);

        try
        {
            var bookSlug      = "test-book";
            var canonicalPath = Path.Combine(canonicalDir, bookSlug + ".json");
            var errorsPath    = Path.Combine(canonicalDir, bookSlug + ".errors.json");

            var existing = new DndMcpAICsharpFun.Domain.Entities.CanonicalJsonFile(
                SchemaVersion: DndMcpAICsharpFun.Domain.Entities.CanonicalJsonSchema.CurrentVersion,
                Book: new DndMcpAICsharpFun.Domain.Entities.CanonicalBookMetadata(
                    SourceBook: displayName, Edition: "5e", FileHash: "abc123", DisplayName: displayName),
                Entities: new List<DndMcpAICsharpFun.Domain.Entities.EntityEnvelope>());

            var jsonOpts = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
            {
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            };
            var canonicalBefore = System.Text.Json.JsonSerializer.Serialize(existing, jsonOpts);
            await File.WriteAllTextAsync(canonicalPath, canonicalBefore);

            File.Exists(errorsPath).Should().BeFalse("test pre-condition: no errors file");

            var orchestrator = BuildOrchestrator(canonicalDir, schemasDir, tracker, converter, bookmarkReader, llm);

            // Act — should not throw.
            await orchestrator.ExtractAsync(bookId, force: false, errorsOnly: true, ct: CancellationToken.None);

            // Assert: LLM never called.
            await llm.DidNotReceive().ExtractAsync(
                Arg.Any<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionRequest>(),
                Arg.Any<CancellationToken>());

            // Canonical content unchanged.
            var canonicalAfter = await File.ReadAllTextAsync(canonicalPath);
            canonicalAfter.Should().Be(canonicalBefore);
        }
        finally
        {
            try { Directory.Delete(canonicalDir, true); } catch { }
            try { Directory.Delete(schemasDir,   true); } catch { }
        }
    }

    [Fact]
    public async Task ErrorsOnly_with_no_canonical_throws_invalid_operation()
    {
        const int bookId = 103;
        const string displayName = "Test Book";

        var (canonicalDir, schemasDir, record, tracker, converter, bookmarkReader, llm)
            = BuildTwoMonsterHarness(bookId, displayName);

        try
        {
            var bookSlug      = "test-book";
            var canonicalPath = Path.Combine(canonicalDir, bookSlug + ".json");
            File.Exists(canonicalPath).Should().BeFalse("test pre-condition: no canonical file");

            var orchestrator = BuildOrchestrator(canonicalDir, schemasDir, tracker, converter, bookmarkReader, llm);

            // Act + Assert
            var act = async () => await orchestrator.ExtractAsync(
                bookId, force: false, errorsOnly: true, ct: CancellationToken.None);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*run full extraction first*");
        }
        finally
        {
            try { Directory.Delete(canonicalDir, true); } catch { }
            try { Directory.Delete(schemasDir,   true); } catch { }
        }
    }

    [Fact]
    public async Task ErrorsOnly_preserves_preexisting_warnings_not_in_retry_set()
    {
        const int bookId = 104;
        const string displayName = "Test Book";

        var (canonicalDir, schemasDir, record, tracker, converter, bookmarkReader, llm)
            = BuildTwoMonsterHarness(bookId, displayName);

        try
        {
            var bookSlug      = "test-book";
            var canonicalPath = Path.Combine(canonicalDir, bookSlug + ".json");
            var errorsPath    = Path.Combine(canonicalDir, bookSlug + ".errors.json");
            var warningsPath  = Path.Combine(canonicalDir, bookSlug + ".warnings.json");

            // Pre-existing canonical with one entity (Beholder — not in the retry set).
            using var emptyFields = System.Text.Json.JsonDocument.Parse("{}");
            var existingEntity = new DndMcpAICsharpFun.Domain.Entities.EntityEnvelope(
                Id:              "test-book.monster.beholder",
                Type:            DndMcpAICsharpFun.Domain.Entities.EntityType.Monster,
                Name:            "Beholder",
                SourceBook:      displayName,
                Edition:         "5e",
                Page:            2,
                FirstAppearedIn: new DndMcpAICsharpFun.Domain.Entities.FirstAppearance(displayName, "5e", 2),
                RevisedIn:       Array.Empty<DndMcpAICsharpFun.Domain.Entities.Revision>(),
                SettingTags:     Array.Empty<string>(),
                CanonicalText:   string.Empty,
                Fields:          emptyFields.RootElement.Clone());

            var existing = new DndMcpAICsharpFun.Domain.Entities.CanonicalJsonFile(
                SchemaVersion: DndMcpAICsharpFun.Domain.Entities.CanonicalJsonSchema.CurrentVersion,
                Book: new DndMcpAICsharpFun.Domain.Entities.CanonicalBookMetadata(
                    SourceBook: displayName, Edition: "5e", FileHash: "abc123", DisplayName: displayName),
                Entities: new List<DndMcpAICsharpFun.Domain.Entities.EntityEnvelope> { existingEntity });

            var jsonOpts = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
            {
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            };
            await File.WriteAllTextAsync(canonicalPath, System.Text.Json.JsonSerializer.Serialize(existing, jsonOpts));

            // Pre-seed a warnings file with one inter-book warning for the Beholder — NOT in the retry set.
            var preExistingWarning = new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionWarningEntry(
                SourceEntityId:  "test-book.monster.beholder",
                FieldPath:       "fields.someRef",
                MissingTargetId: "other-book.spell.fireball",
                WarningKind:     "inter_book_dangling_ref");

            var webOpts = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
            await File.WriteAllTextAsync(
                warningsPath,
                System.Text.Json.JsonSerializer.Serialize(
                    new List<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionWarningEntry> { preExistingWarning },
                    webOpts));

            // Errors file points only at Aboleth — Beholder is NOT in the retry set.
            var prevErrors = new List<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionErrorEntry>
            {
                new(SourceEntityId: "test-book.monster.aboleth",
                    FieldPath: "(extraction)",
                    MissingTargetId: string.Empty,
                    ErrorKind: "extraction_failure",
                    Detail: null),
            };
            await File.WriteAllTextAsync(errorsPath, System.Text.Json.JsonSerializer.Serialize(prevErrors, webOpts));

            // LLM returns success for the retried Aboleth.
            using var fields = System.Text.Json.JsonDocument.Parse("{}");
            llm.ExtractAsync(Arg.Any<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionRequest>(),
                             Arg.Any<CancellationToken>())
               .Returns(new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionResponse(
                   Success: true,
                   ToolInput: fields.RootElement.Clone(),
                   StopReason: "tool_use",
                   InputTokens: 0,
                   OutputTokens: 0,
                   ErrorMessage: null,
                   RawJson: null));

            var orchestrator = BuildOrchestrator(canonicalDir, schemasDir, tracker, converter, bookmarkReader, llm);

            // Act
            await orchestrator.ExtractAsync(bookId, force: false, errorsOnly: true, ct: CancellationToken.None);

            // Assert: warnings file must still contain the pre-existing Beholder warning.
            File.Exists(warningsPath).Should().BeTrue("warnings file must exist after errorsOnly run");

            var warningsJson = await File.ReadAllTextAsync(warningsPath);
            var warnings = System.Text.Json.JsonSerializer.Deserialize<
                List<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionWarningEntry>>(
                warningsJson, webOpts);

            warnings.Should().NotBeNull();
            warnings!.Should().Contain(
                w => w.SourceEntityId == "test-book.monster.beholder"
                  && w.FieldPath      == "fields.someRef"
                  && w.MissingTargetId == "other-book.spell.fireball",
                "pre-existing warning for a non-retried entity must be preserved");
        }
        finally
        {
            try { Directory.Delete(canonicalDir, true); } catch { }
            try { Directory.Delete(schemasDir,   true); } catch { }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Task 7 — source key + edition propagation
    // ─────────────────────────────────────────────────────────────────────────

    // ─────────────────────────────────────────────────────────────────────────
    //  Task 6 — chunked split-and-merge extraction loop
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExtractAsync_OversizedCandidate_SplitsIntoChunksAndMergesResults()
    {
        // Arrange
        const int bookId = 300;
        const string displayName = "Test Book";

        var canonicalDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var schemasDir   = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(canonicalDir);
        Directory.CreateDirectory(schemasDir);

        // Write a minimal Trap schema so LLM is invoked for that type.
        File.WriteAllText(
            Path.Combine(schemasDir, "TrapFields.schema.json"),
            "{ \"type\": \"object\" }");

        try
        {
            var record = new DndMcpAICsharpFun.Domain.IngestionRecord
            {
                Id          = bookId,
                FilePath    = "/dev/null",
                FileName    = "test.pdf",
                FileHash    = "abc123",
                Version     = "5e",
                DisplayName = displayName,
            };

            var tracker = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Tracking.IIngestionTracker>();
            tracker.GetByIdAsync(bookId, Arg.Any<CancellationToken>()).Returns(record);

            // Two text items under the same heading produce a candidate whose Text is:
            //   new string('a', 400) + "\n\n" + new string('b', 400)   (~802 chars)
            // SemanticChunker with MaxTokensPerChunk=150 (≈600 chars) splits that into 2 chunks.
            var part1 = new string('a', 400);
            var part2 = new string('b', 400);

            var converter = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Pdf.IPdfStructureConverter>();
            var converterDoc = new DndMcpAICsharpFun.Features.Ingestion.Pdf.PdfStructureDocument(
                "doc",
                new List<DndMcpAICsharpFun.Features.Ingestion.Pdf.PdfStructureItem>
                {
                    new("heading", "Pit Trap", 1, null),
                    new("text",    part1,       1, null),
                    new("text",    part2,       1, null),
                });
            converter.ConvertAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(converterDoc);

            // Bookmark maps page 1 → "Traps and Hazards" section → Trap entity type.
            var bookmarkReader = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Pdf.IPdfBookmarkReader>();
            bookmarkReader.ReadBookmarks(Arg.Any<string>()).Returns(
                new List<DndMcpAICsharpFun.Features.Ingestion.Pdf.PdfBookmark>
                {
                    new("Traps and Hazards", 1),
                });

            var llm = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.IEntityExtractionLlmClient>();

            // First call returns partial with "name" and first entries array.
            using var partial1Doc = System.Text.Json.JsonDocument.Parse(
                "{\"name\":\"Pit Trap\",\"entries\":[\"from chunk one\"]}");
            // Second call returns partial with second entries array and trapHazType.
            using var partial2Doc = System.Text.Json.JsonDocument.Parse(
                "{\"entries\":[\"from chunk two\"],\"trapHazType\":\"MECH\"}");

            var callCount = 0;
            llm.ExtractAsync(
                    Arg.Any<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionRequest>(),
                    Arg.Any<CancellationToken>())
               .Returns(_ =>
               {
                   callCount++;
                   var toolInput = callCount == 1
                       ? partial1Doc.RootElement.Clone()
                       : partial2Doc.RootElement.Clone();
                   return new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionResponse(
                       Success: true,
                       ToolInput: toolInput,
                       StopReason: "tool_use",
                       InputTokens: 0,
                       OutputTokens: 0,
                       ErrorMessage: null,
                       RawJson: null);
               });

            // Options: MaxTokensPerChunk = 150 so the 800-char text is split.
            var opts = Options.Create(new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityExtractionOptions
            {
                CanonicalDirectory = canonicalDir,
                SchemasDirectory   = schemasDir,
                MaxTokensPerChunk  = 150,
            });
            var registry   = new DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.BookSourceRegistry(
                Path.Combine(Path.GetTempPath(), "__nonexistent_books__.json"));

            var sharedMatcher = new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityNameMatcher(
                new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityNameIndex(
                    Path.Combine(Path.GetTempPath(), "__nonexistent_5etools__")));
            var orchestrator = new EntityExtractionOrchestrator(
                tracker:            tracker,
                registry:           registry,
                converter:          converter,
                candidateBuilder:   new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityCandidateBuilder(
                    bookmarks:        bookmarkReader,
                    scanner:          new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityCandidateScanner(NullLogger<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityCandidateScanner>.Instance),
                    statBlockScanner: new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.StatBlockScanner(),
                    logger:           NullLogger<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityCandidateBuilder>.Instance,
                    matcher:          sharedMatcher),
                writer:             new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.CanonicalJsonWriter(),
                errorsFile:         new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionErrorsFile(),
                warningsFile:       new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionWarningsFile(),
                declinedFile:       new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionDeclinedFile(),
                refResolver:        new DndMcpAICsharpFun.Features.Entities.EntityReferenceResolver(),
                schemaProvider:     new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntitySchemaProvider(
                    opts, NullLogger<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntitySchemaProvider>.Instance),
                checkpointStore:    new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionCheckpointStore(),
                runner:             new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityExtractionRunner(
                    candidateExtractor: BuildCandidateExtractor(llm, opts),
                    logger:             NullLogger<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityExtractionRunner>.Instance,
                    cascade:            GroundingCascadeTestFactory.Inert(),
                    matcher:            sharedMatcher),
                options:            opts,
                logger:             NullLogger<EntityExtractionOrchestrator>.Instance,
                matcher:            sharedMatcher);

            // Act
            await orchestrator.ExtractAsync(bookId, force: true, errorsOnly: false, ct: CancellationToken.None);

            // Assert: LLM was called exactly 2 times (once per chunk).
            callCount.Should().Be(2, "the oversized candidate must be split into 2 chunks");

            // Assert: canonical JSON was written with one entity whose fields contain merged data.
            var bookSlug = DndMcpAICsharpFun.Domain.Entities.EntityIdSlug
                .For(displayName, DndMcpAICsharpFun.Domain.Entities.EntityType.Class, "x")
                .Split('.')[0];
            var canonicalPath = Path.Combine(canonicalDir, bookSlug + ".json");

            File.Exists(canonicalPath).Should().BeTrue("canonical JSON must be written");

            var json = await File.ReadAllTextAsync(canonicalPath);
            var canonical = System.Text.Json.JsonSerializer.Deserialize<
                DndMcpAICsharpFun.Domain.Entities.CanonicalJsonFile>(
                json,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
                {
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
                });

            canonical.Should().NotBeNull();
            canonical!.Entities.Should().HaveCount(1, "one trap candidate was successfully extracted");

            var fieldsJson = canonical.Entities[0].Fields.GetRawText();
            fieldsJson.Should().Contain("from chunk one",  "merged fields must include chunk-1 entry");
            fieldsJson.Should().Contain("from chunk two",  "merged fields must include chunk-2 entry");
            fieldsJson.Should().Contain("trapHazType",     "merged fields must include trapHazType from chunk 2");
            fieldsJson.Should().Contain("MECH",            "merged fields must include MECH value from chunk 2");
        }
        finally
        {
            try { Directory.Delete(canonicalDir, true); } catch { }
            try { Directory.Delete(schemasDir,   true); } catch { }
        }
    }

    [Fact]
    public async Task FivetoolsSourceKey_propagates_source_and_edition_to_canonical_json()
    {
        // Arrange — use the real books.json (PHB: published 2014 → Edition2014).
        var booksJsonPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../../5etools/books.json"));

        var registry = new DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.BookSourceRegistry(booksJsonPath);

        // Verify pre-condition: PHB is in the registry and was published before 2024.
        var phbInfo = registry.TryGetBook("PHB");
        phbInfo.Should().NotBeNull("PHB must be in 5etools/books.json for this test to be meaningful");
        phbInfo!.PublishedYear.Should().BeLessThan(2024);

        const int bookId = 200;
        const string displayName = "Player's Handbook";

        var canonicalDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var schemasDir   = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(canonicalDir);
        Directory.CreateDirectory(schemasDir);

        // Write a minimal Monster schema so LLM is invoked.
        File.WriteAllText(
            Path.Combine(schemasDir, "MonsterFields.schema.json"),
            "{ \"type\": \"object\" }");

        try
        {
            // Record has FivetoolsSourceKey = "PHB".
            var record = new DndMcpAICsharpFun.Domain.IngestionRecord
            {
                Id                 = bookId,
                FilePath           = "/dev/null",
                FileName           = "phb.pdf",
                FileHash           = "phbhash",
                Version            = "5e",
                DisplayName        = displayName,
                FivetoolsSourceKey = "PHB",
            };

            var tracker = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Tracking.IIngestionTracker>();
            tracker.GetByIdAsync(bookId, Arg.Any<CancellationToken>()).Returns(record);

            // One monster candidate so LLM is called once.
            var converter = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Pdf.IPdfStructureConverter>();
            var converterDoc = new DndMcpAICsharpFun.Features.Ingestion.Pdf.PdfStructureDocument(
                "doc",
                new List<DndMcpAICsharpFun.Features.Ingestion.Pdf.PdfStructureItem>
                {
                    new("heading", "Aboleth", 1, null),
                    new("text",    "Aboleth — a slimy aberration.", 1, null),
                });
            converter.ConvertAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(converterDoc);

            var bookmarkReader = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Pdf.IPdfBookmarkReader>();
            bookmarkReader.ReadBookmarks(Arg.Any<string>()).Returns(
                new List<DndMcpAICsharpFun.Features.Ingestion.Pdf.PdfBookmark>
                {
                    new("Monsters", 1),
                });

            var llm = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.IEntityExtractionLlmClient>();
            using var fields = System.Text.Json.JsonDocument.Parse("{}");
            llm.ExtractAsync(
                    Arg.Any<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionRequest>(),
                    Arg.Any<CancellationToken>())
               .Returns(new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionResponse(
                   Success: true,
                   ToolInput: fields.RootElement.Clone(),
                   StopReason: "tool_use",
                   InputTokens: 0,
                   OutputTokens: 0,
                   ErrorMessage: null,
                   RawJson: null));

            // Real 5etools index — loads "Aboleth" as a Monster from the repo's 5etools/ directory.
            // Without this, the allowlist gate declines the candidate (official + no 5etools match + no stat block).
            var realMatcher = new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityNameMatcher(
                new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityNameIndex(
                    TestPaths.RepoFile("5etools")));

            var orchestrator = BuildOrchestrator(
                canonicalDir, schemasDir, tracker, converter, bookmarkReader, llm, registry,
                matcher: realMatcher);

            // Act
            await orchestrator.ExtractAsync(bookId, force: true, errorsOnly: false, ct: CancellationToken.None);

            // Assert: canonical JSON must exist and carry source="PHB", edition="Edition2014".
            // The slug derives from the source key (PHB -> phb14), not the display name.
            var bookSlug = DndMcpAICsharpFun.Domain.Entities.EntityIdSlug
                .For(record.FivetoolsSourceKey!, DndMcpAICsharpFun.Domain.Entities.EntityType.Class, "x")
                .Split('.')[0];
            var canonicalPath = Path.Combine(canonicalDir, bookSlug + ".json");

            File.Exists(canonicalPath).Should().BeTrue("canonical JSON must be written");

            var json = await File.ReadAllTextAsync(canonicalPath);
            var canonical = System.Text.Json.JsonSerializer.Deserialize<
                DndMcpAICsharpFun.Domain.Entities.CanonicalJsonFile>(
                json,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
                {
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                });

            canonical.Should().NotBeNull();
            canonical!.Entities.Should().HaveCount(1, "one monster candidate was successfully extracted");

            var entity = canonical.Entities[0];
            entity.SourceBook.Should().Be("PHB",
                "FivetoolsSourceKey should override DisplayName as the source book");
            entity.Edition.Should().Be("Edition2014",
                "PHB was published in 2014, so edition must be Edition2014");
            entity.FirstAppearedIn.Book.Should().Be("PHB");
            entity.FirstAppearedIn.Edition.Should().Be("Edition2014");
        }
        finally
        {
            try { Directory.Delete(canonicalDir, true); } catch { }
            try { Directory.Delete(schemasDir,   true); } catch { }
        }
    }

    [Fact]
    public async Task Non_entity_named_candidate_is_dropped_without_llm_call()
    {
        // Arrange — single candidate whose name "ACTIONS" is all-caps with no space,
        // so IsEntityLikeName returns false → DeterministicTypeResolver.Resolve → Drop.
        // The LLM must never be called because the candidate is filtered before extraction.
        var canonicalDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var schemasDir   = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(canonicalDir);
        Directory.CreateDirectory(schemasDir);

        // Write a Monster schema so the LLM *would* be called if the candidate weren't dropped.
        File.WriteAllText(
            Path.Combine(schemasDir, "MonsterFields.schema.json"),
            "{ \"type\": \"object\" }");

        try
        {
            const int bookId = 301;
            const string displayName = "Drop Test Book";

            var record = new DndMcpAICsharpFun.Domain.IngestionRecord
            {
                Id          = bookId,
                FilePath    = "/dev/null",
                FileName    = "drop-test.pdf",
                FileHash    = "drop123",
                Version     = "5e",
                DisplayName = displayName,
            };

            var tracker = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Tracking.IIngestionTracker>();
            tracker.GetByIdAsync(bookId, Arg.Any<CancellationToken>()).Returns(record);

            // Single heading "ACTIONS" (all-caps, no space, ≥4 chars → IsEntityLikeName=false → Drop)
            // with stat-block-like text so it *would* have been a Monster candidate if not dropped.
            var converter = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Pdf.IPdfStructureConverter>();
            var converterDoc = new DndMcpAICsharpFun.Features.Ingestion.Pdf.PdfStructureDocument(
                "doc",
                new List<DndMcpAICsharpFun.Features.Ingestion.Pdf.PdfStructureItem>
                {
                    new("heading", "ACTIONS", 1, null),
                    new("text",    "Armor Class 14 Hit Points 30 Challenge 1 (200 XP)", 1, null),
                });
            converter.ConvertAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(converterDoc);

            var bookmarkReader = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Pdf.IPdfBookmarkReader>();
            bookmarkReader.ReadBookmarks(Arg.Any<string>()).Returns(
                new List<DndMcpAICsharpFun.Features.Ingestion.Pdf.PdfBookmark>
                {
                    new("Monsters", 1),
                });

            var llm = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.IEntityExtractionLlmClient>();

            var orchestrator = BuildOrchestrator(
                canonicalDir, schemasDir, tracker, converter, bookmarkReader, llm);

            // Act
            await orchestrator.ExtractAsync(bookId, force: true, errorsOnly: false, ct: CancellationToken.None);

            // Assert: the LLM was never called because "ACTIONS" was dropped before extraction.
            await llm.DidNotReceive().ExtractAsync(
                Arg.Any<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionRequest>(),
                Arg.Any<CancellationToken>());

            // Also assert no entity named "ACTIONS" was written to canonical JSON.
            var bookSlug = DndMcpAICsharpFun.Domain.Entities.EntityIdSlug
                .For(displayName, DndMcpAICsharpFun.Domain.Entities.EntityType.Class, "x")
                .Split('.')[0];
            var canonicalPath = Path.Combine(canonicalDir, bookSlug + ".json");

            if (File.Exists(canonicalPath))
            {
                var json = await File.ReadAllTextAsync(canonicalPath);
                var canonical = System.Text.Json.JsonSerializer.Deserialize<
                    DndMcpAICsharpFun.Domain.Entities.CanonicalJsonFile>(
                    json,
                    new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
                    {
                        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                    });
                canonical?.Entities.Should().NotContain(
                    e => e.Name == "ACTIONS",
                    "the ACTIONS heading must be dropped before extraction");
            }
        }
        finally
        {
            try { Directory.Delete(canonicalDir, true); } catch { }
            try { Directory.Delete(schemasDir,   true); } catch { }
        }
    }

    [Fact]
    public async Task Magic_item_candidate_is_typed_MagicItem()
    {
        // Arrange — single candidate "Vorpal Sword" whose text contains "requires attunement"
        // → IsMagicItem=true → DeterministicTypeResolver.Resolve → ForceType(MagicItem).
        var canonicalDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var schemasDir   = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(canonicalDir);
        Directory.CreateDirectory(schemasDir);

        // Write a MagicItem schema so the forced extraction path finds it.
        File.WriteAllText(
            Path.Combine(schemasDir, "MagicItemFields.schema.json"),
            "{ \"type\": \"object\" }");

        try
        {
            const int bookId = 302;
            const string displayName = "Magic Item Test Book";

            var record = new DndMcpAICsharpFun.Domain.IngestionRecord
            {
                Id          = bookId,
                FilePath    = "/dev/null",
                FileName    = "magic-items.pdf",
                FileHash    = "mi123",
                Version     = "5e",
                DisplayName = displayName,
            };

            var tracker = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Tracking.IIngestionTracker>();
            tracker.GetByIdAsync(bookId, Arg.Any<CancellationToken>()).Returns(record);

            // "Vorpal Sword" with "requires attunement" text → IsMagicItem=true → ForceType(MagicItem).
            var converter = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Pdf.IPdfStructureConverter>();
            var converterDoc = new DndMcpAICsharpFun.Features.Ingestion.Pdf.PdfStructureDocument(
                "doc",
                new List<DndMcpAICsharpFun.Features.Ingestion.Pdf.PdfStructureItem>
                {
                    new("heading", "Vorpal Sword", 1, null),
                    new("text",    "Weapon (any sword that deals slashing damage), legendary (requires attunement)", 1, null),
                });
            converter.ConvertAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(converterDoc);

            // Bookmark the page under "Magic Items" so the scanner emits a MagicItem prior.
            var bookmarkReader = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Pdf.IPdfBookmarkReader>();
            bookmarkReader.ReadBookmarks(Arg.Any<string>()).Returns(
                new List<DndMcpAICsharpFun.Features.Ingestion.Pdf.PdfBookmark>
                {
                    new("Magic Items", 1),
                });

            var llm = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.IEntityExtractionLlmClient>();
            // LLM returns success with empty fields (the forced-type path calls ExtractFieldsAsync).
            using var fields = System.Text.Json.JsonDocument.Parse("{}");
            llm.ExtractAsync(
                    Arg.Any<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionRequest>(),
                    Arg.Any<CancellationToken>())
               .Returns(new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionResponse(
                   Success: true,
                   ToolInput: fields.RootElement.Clone(),
                   StopReason: "tool_use",
                   InputTokens: 0,
                   OutputTokens: 0,
                   ErrorMessage: null,
                   RawJson: null));

            var orchestrator = BuildOrchestrator(
                canonicalDir, schemasDir, tracker, converter, bookmarkReader, llm);

            // Act
            await orchestrator.ExtractAsync(bookId, force: true, errorsOnly: false, ct: CancellationToken.None);

            // Assert: canonical JSON must exist and contain exactly one entity of type MagicItem.
            var bookSlug = DndMcpAICsharpFun.Domain.Entities.EntityIdSlug
                .For(displayName, DndMcpAICsharpFun.Domain.Entities.EntityType.Class, "x")
                .Split('.')[0];
            var canonicalPath = Path.Combine(canonicalDir, bookSlug + ".json");

            File.Exists(canonicalPath).Should().BeTrue("canonical JSON must be written");

            var json = await File.ReadAllTextAsync(canonicalPath);
            var canonical = System.Text.Json.JsonSerializer.Deserialize<
                DndMcpAICsharpFun.Domain.Entities.CanonicalJsonFile>(
                json,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
                {
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                });

            canonical.Should().NotBeNull();
            canonical!.Entities.Should().HaveCount(1, "one magic item candidate was extracted");

            var entity = canonical.Entities[0];
            entity.Type.Should().Be(DndMcpAICsharpFun.Domain.Entities.EntityType.MagicItem,
                "DeterministicTypeResolver.Resolve should force MagicItem for 'requires attunement' text");
        }
        finally
        {
            try { Directory.Delete(canonicalDir, true); } catch { }
            try { Directory.Delete(schemasDir,   true); } catch { }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Task 6 — 5etools matcher integration
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FivetoolsMatcher_forces_spell_type_and_canonical_name_for_fireball()
    {
        // Arrange — two candidates:
        //   "FIREBALL" (page 1, under Spells bookmark) → 5etools match → ForceType(Spell, "Fireball")
        //   "ACTIONS"  (page 2, under Monsters bookmark) → not in index → IsEntityLikeName=false → Drop
        // The orchestrator is wired with the real 5etools index, so FIREBALL resolves to the
        // canonical name "Fireball" with type Spell and ACTIONS is dropped before the LLM is called.
        var canonicalDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var schemasDir   = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(canonicalDir);
        Directory.CreateDirectory(schemasDir);

        // Spell schema must be present so the ForceType(Spell) branch finds it.
        File.WriteAllText(
            Path.Combine(schemasDir, "SpellFields.schema.json"),
            "{ \"type\": \"object\" }");

        try
        {
            const int bookId = 400;
            const string displayName = "Spell Test Book";

            var record = new DndMcpAICsharpFun.Domain.IngestionRecord
            {
                Id          = bookId,
                FilePath    = "/dev/null",
                FileName    = "spell-test.pdf",
                FileHash    = "spell123",
                Version     = "5e",
                DisplayName = displayName,
            };

            var tracker = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Tracking.IIngestionTracker>();
            tracker.GetByIdAsync(bookId, Arg.Any<CancellationToken>()).Returns(record);

            // Page 1: "FIREBALL" heading under Spells; page 2: "ACTIONS" heading under Monsters.
            var converter = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Pdf.IPdfStructureConverter>();
            var converterDoc = new DndMcpAICsharpFun.Features.Ingestion.Pdf.PdfStructureDocument(
                "doc",
                new List<DndMcpAICsharpFun.Features.Ingestion.Pdf.PdfStructureItem>
                {
                    new("heading", "FIREBALL", 1, null),
                    new("text",    "A bright streak of fire.", 1, null),
                    new("heading", "ACTIONS", 2, null),
                    new("text",    "Armor Class 14 Hit Points 30 Challenge 1 (200 XP)", 2, null),
                });
            converter.ConvertAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(converterDoc);

            // Bookmark page 1 under "Spells", page 2 under "Monsters".
            var bookmarkReader = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Pdf.IPdfBookmarkReader>();
            bookmarkReader.ReadBookmarks(Arg.Any<string>()).Returns(
                new List<DndMcpAICsharpFun.Features.Ingestion.Pdf.PdfBookmark>
                {
                    new("Spells",   1),
                    new("Monsters", 2),
                });

            // LLM returns success with empty spell fields (the ForceType path calls ExtractFieldsAsync).
            var llm = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.IEntityExtractionLlmClient>();
            using var fields = System.Text.Json.JsonDocument.Parse("{}");
            llm.ExtractAsync(
                    Arg.Any<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionRequest>(),
                    Arg.Any<CancellationToken>())
               .Returns(new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionResponse(
                   Success: true,
                   ToolInput: fields.RootElement.Clone(),
                   StopReason: "tool_use",
                   InputTokens: 0,
                   OutputTokens: 0,
                   ErrorMessage: null,
                   RawJson: null));

            // Real 5etools index — loads "Fireball" as a Spell from the repo's 5etools/spells/ directory.
            var realMatcher = new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityNameMatcher(
                new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityNameIndex(
                    TestPaths.RepoFile("5etools")));

            var orchestrator = BuildOrchestrator(
                canonicalDir, schemasDir, tracker, converter, bookmarkReader, llm,
                matcher: realMatcher);

            // Act
            await orchestrator.ExtractAsync(bookId, force: true, errorsOnly: false, ct: CancellationToken.None);

            // Assert: LLM was called exactly once (for FIREBALL; ACTIONS was dropped before extraction).
            await llm.Received(1).ExtractAsync(
                Arg.Any<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionRequest>(),
                Arg.Any<CancellationToken>());

            // Assert: canonical JSON must contain exactly one entity — a Spell named "Fireball".
            var bookSlug = DndMcpAICsharpFun.Domain.Entities.EntityIdSlug
                .For(displayName, DndMcpAICsharpFun.Domain.Entities.EntityType.Class, "x")
                .Split('.')[0];
            var canonicalPath = Path.Combine(canonicalDir, bookSlug + ".json");

            File.Exists(canonicalPath).Should().BeTrue("canonical JSON must be written");

            var json = await File.ReadAllTextAsync(canonicalPath);
            var canonical = System.Text.Json.JsonSerializer.Deserialize<
                DndMcpAICsharpFun.Domain.Entities.CanonicalJsonFile>(
                json,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
                {
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                });

            canonical.Should().NotBeNull();
            canonical!.Entities.Should().HaveCount(1,
                "FIREBALL should be extracted as Spell; ACTIONS should be dropped before extraction");

            var spellEntity = canonical.Entities[0];
            spellEntity.Type.Should().Be(DndMcpAICsharpFun.Domain.Entities.EntityType.Spell,
                "5etools matcher should force FIREBALL to type Spell");
            spellEntity.Name.Should().Be("Fireball",
                "canonical name from 5etools index should replace the raw all-caps heading");
        }
        finally
        {
            try { Directory.Delete(canonicalDir, true); } catch { }
            try { Directory.Delete(schemasDir,   true); } catch { }
        }
    }

    [Fact]
    public async Task ErrorsOnly_retry_finds_5etools_matched_failed_candidate()
    {
        // Arrange: FIREBALL under "Monsters" bookmark → primary prior = Monster.
        // MatchOfType("FIREBALL", Monster) = null (no "Fireball" monster in 5etools) → Step 1 skipped.
        // Match("FIREBALL") = Spell "Fireball" → cross-type Force → canonical id = bookSlug.spell.fireball.
        // The errors file records the CANONICAL id. errorsOnly membership must use that canonical id
        // so FIREBALL is retried. RecordedEntityId returns the canonical (forced) id → retry happens.
        var canonicalDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var schemasDir   = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(canonicalDir);
        Directory.CreateDirectory(schemasDir);

        // Both Monster and Spell schemas: TypePrior=[Monster,...] for the candidate,
        // Spell schema needed for the forced extraction path.
        File.WriteAllText(Path.Combine(schemasDir, "MonsterFields.schema.json"), "{ \"type\": \"object\" }");
        File.WriteAllText(Path.Combine(schemasDir, "SpellFields.schema.json"),   "{ \"type\": \"object\" }");

        try
        {
            const int bookId = 500;
            const string displayName = "Fireball Test Book";

            var record = new DndMcpAICsharpFun.Domain.IngestionRecord
            {
                Id          = bookId,
                FilePath    = "/dev/null",
                FileName    = "fireball-test.pdf",
                FileHash    = "fbhash1",
                Version     = "5e",
                DisplayName = displayName,
            };

            var tracker = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Tracking.IIngestionTracker>();
            tracker.GetByIdAsync(bookId, Arg.Any<CancellationToken>()).Returns(record);

            // FIREBALL under "Monsters" bookmark → scanner assigns primary Type=Monster.
            // No "Fireball" monster in 5etools → cross-type Force → Spell "Fireball".
            var converter = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Pdf.IPdfStructureConverter>();
            var converterDoc = new DndMcpAICsharpFun.Features.Ingestion.Pdf.PdfStructureDocument(
                "doc",
                new List<DndMcpAICsharpFun.Features.Ingestion.Pdf.PdfStructureItem>
                {
                    new("heading", "FIREBALL", 1, null),
                    new("text",    "A bright streak of fire.", 1, null),
                });
            converter.ConvertAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(converterDoc);

            var bookmarkReader = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Pdf.IPdfBookmarkReader>();
            bookmarkReader.ReadBookmarks(Arg.Any<string>()).Returns(
                new List<DndMcpAICsharpFun.Features.Ingestion.Pdf.PdfBookmark>
                {
                    new("Monsters", 1),
                });

            var llm = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.IEntityExtractionLlmClient>();
            using var fields = System.Text.Json.JsonDocument.Parse("{}");
            llm.ExtractAsync(
                    Arg.Any<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionRequest>(),
                    Arg.Any<CancellationToken>())
               .Returns(new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionResponse(
                   Success: true,
                   ToolInput: fields.RootElement.Clone(),
                   StopReason: "tool_use",
                   InputTokens: 0,
                   OutputTokens: 0,
                   ErrorMessage: null,
                   RawJson: null));

            // Real 5etools index so "FIREBALL" → ForceType(Spell, "Fireball").
            var realMatcher = new EntityNameMatcher(
                new EntityNameIndex(TestPaths.RepoFile("5etools")));

            // Compute the canonical Fireball id dynamically (what RecordedEntityId yields post-fix).
            var firebailMatch = realMatcher.Match("FIREBALL")!.Value;
            var canonicalFireballId = DndMcpAICsharpFun.Domain.Entities.EntityIdSlug.For(
                displayName, firebailMatch.Type, firebailMatch.Canonical);

            // Derive bookSlug the same way the orchestrator does.
            var bookSlug = DndMcpAICsharpFun.Domain.Entities.EntityIdSlug
                .For(displayName, DndMcpAICsharpFun.Domain.Entities.EntityType.Class, "x")
                .Split('.')[0];
            var canonicalPath = Path.Combine(canonicalDir, bookSlug + ".json");
            var errorsPath    = Path.Combine(canonicalDir, bookSlug + ".errors.json");

            var jsonOpts = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
            {
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            };

            // Pre-seed canonical JSON (empty entities) — errorsOnly requires it to exist.
            var emptyCanonical = new DndMcpAICsharpFun.Domain.Entities.CanonicalJsonFile(
                SchemaVersion: DndMcpAICsharpFun.Domain.Entities.CanonicalJsonSchema.CurrentVersion,
                Book: new DndMcpAICsharpFun.Domain.Entities.CanonicalBookMetadata(
                    SourceBook: displayName, Edition: "5e", FileHash: "fbhash1", DisplayName: displayName),
                Entities: new List<DndMcpAICsharpFun.Domain.Entities.EntityEnvelope>());
            await File.WriteAllTextAsync(canonicalPath,
                System.Text.Json.JsonSerializer.Serialize(emptyCanonical, jsonOpts));

            // Pre-seed errors file with the CANONICAL Fireball id (what RunErrorsOnlyAsync records).
            var prevErrors = new List<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionErrorEntry>
            {
                new(SourceEntityId: canonicalFireballId,
                    FieldPath: "(extraction)",
                    MissingTargetId: string.Empty,
                    ErrorKind: "extraction_failure",
                    Detail: "previous failure"),
            };
            await File.WriteAllTextAsync(errorsPath,
                System.Text.Json.JsonSerializer.Serialize(prevErrors,
                    new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));

            var orchestrator = BuildOrchestrator(
                canonicalDir, schemasDir, tracker, converter, bookmarkReader, llm,
                matcher: realMatcher);

            // Act — errors-only retry.
            await orchestrator.ExtractAsync(bookId, force: false, errorsOnly: true, ct: CancellationToken.None);

            // Assert: LLM called once (FIREBALL was retried, not skipped).
            await llm.Received(1).ExtractAsync(
                Arg.Any<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionRequest>(),
                Arg.Any<CancellationToken>());

            // Assert: merged canonical contains Fireball as a Spell.
            var mergedJson = await File.ReadAllTextAsync(canonicalPath);
            var merged = System.Text.Json.JsonSerializer.Deserialize<
                DndMcpAICsharpFun.Domain.Entities.CanonicalJsonFile>(mergedJson, jsonOpts);
            merged.Should().NotBeNull();
            merged!.Entities.Should().HaveCount(1);
            merged.Entities[0].Type.Should().Be(DndMcpAICsharpFun.Domain.Entities.EntityType.Spell);
            merged.Entities[0].Name.Should().Be("Fireball");
        }
        finally
        {
            try { Directory.Delete(canonicalDir, true); } catch { }
            try { Directory.Delete(schemasDir,   true); } catch { }
        }
    }

    [Fact]
    public async Task CheckpointResume_does_not_duplicate_5etools_matched_entity()
    {
        // Arrange: checkpoint holds FIREBALL (Monsters bookmark → Monster prior) with its CANONICAL id.
        // MatchOfType("FIREBALL", Monster) = null → cross-type Force to Spell → canonical id computed.
        // RecordedEntityId returns that canonical id, so the doneIds membership check hits → the
        // already-checkpointed entity is skipped → exactly one entity, no duplicate.
        var canonicalDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var schemasDir   = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(canonicalDir);
        Directory.CreateDirectory(schemasDir);

        File.WriteAllText(Path.Combine(schemasDir, "MonsterFields.schema.json"), "{ \"type\": \"object\" }");
        File.WriteAllText(Path.Combine(schemasDir, "SpellFields.schema.json"),   "{ \"type\": \"object\" }");

        try
        {
            const int bookId = 501;
            const string displayName = "Fireball Test Book";

            var record = new DndMcpAICsharpFun.Domain.IngestionRecord
            {
                Id          = bookId,
                FilePath    = "/dev/null",
                FileName    = "fireball-ckpt.pdf",
                FileHash    = "fbhash2",
                Version     = "5e",
                DisplayName = displayName,
            };

            var tracker = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Tracking.IIngestionTracker>();
            tracker.GetByIdAsync(bookId, Arg.Any<CancellationToken>()).Returns(record);

            var converter = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Pdf.IPdfStructureConverter>();
            var converterDoc = new DndMcpAICsharpFun.Features.Ingestion.Pdf.PdfStructureDocument(
                "doc",
                new List<DndMcpAICsharpFun.Features.Ingestion.Pdf.PdfStructureItem>
                {
                    new("heading", "FIREBALL", 1, null),
                    new("text",    "A bright streak of fire.", 1, null),
                });
            converter.ConvertAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(converterDoc);

            var bookmarkReader = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Pdf.IPdfBookmarkReader>();
            bookmarkReader.ReadBookmarks(Arg.Any<string>()).Returns(
                new List<DndMcpAICsharpFun.Features.Ingestion.Pdf.PdfBookmark>
                {
                    new("Monsters", 1),
                });

            var llm = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.IEntityExtractionLlmClient>();
            using var fields = System.Text.Json.JsonDocument.Parse("{}");
            llm.ExtractAsync(
                    Arg.Any<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionRequest>(),
                    Arg.Any<CancellationToken>())
               .Returns(new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionResponse(
                   Success: true,
                   ToolInput: fields.RootElement.Clone(),
                   StopReason: "tool_use",
                   InputTokens: 0,
                   OutputTokens: 0,
                   ErrorMessage: null,
                   RawJson: null));

            var realMatcher = new EntityNameMatcher(
                new EntityNameIndex(TestPaths.RepoFile("5etools")));

            // Canonical Fireball id (what ExtractOneAsync records in the checkpoint on first pass).
            var firebailMatch = realMatcher.Match("FIREBALL")!.Value;
            var canonicalFireballId = DndMcpAICsharpFun.Domain.Entities.EntityIdSlug.For(
                displayName, firebailMatch.Type, firebailMatch.Canonical);

            var bookSlug = DndMcpAICsharpFun.Domain.Entities.EntityIdSlug
                .For(displayName, DndMcpAICsharpFun.Domain.Entities.EntityType.Class, "x")
                .Split('.')[0];

            // Pre-seed checkpoint: already-extracted entity with CANONICAL Fireball id.
            using var emptyDoc = System.Text.Json.JsonDocument.Parse("{}");
            var checkpointEntity = new DndMcpAICsharpFun.Domain.Entities.EntityEnvelope(
                Id:              canonicalFireballId,
                Type:            DndMcpAICsharpFun.Domain.Entities.EntityType.Spell,
                Name:            "Fireball",
                SourceBook:      displayName,
                Edition:         "5e",
                Page:            1,
                FirstAppearedIn: new DndMcpAICsharpFun.Domain.Entities.FirstAppearance(displayName, "5e", 1),
                RevisedIn:       Array.Empty<DndMcpAICsharpFun.Domain.Entities.Revision>(),
                SettingTags:     Array.Empty<string>(),
                CanonicalText:   string.Empty,
                Fields:          emptyDoc.RootElement.Clone());

            var checkpointOpts = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
            {
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            };
            var checkpointPath       = Path.Combine(canonicalDir, bookSlug + ".progress.json");
            var checkpointErrorsPath = Path.Combine(canonicalDir, bookSlug + ".progress.errors.json");

            await File.WriteAllTextAsync(checkpointPath,
                System.Text.Json.JsonSerializer.Serialize(
                    new List<DndMcpAICsharpFun.Domain.Entities.EntityEnvelope> { checkpointEntity },
                    checkpointOpts));
            await File.WriteAllTextAsync(checkpointErrorsPath,
                System.Text.Json.JsonSerializer.Serialize(
                    new List<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionErrorEntry>(),
                    checkpointOpts));

            var orchestrator = BuildOrchestrator(
                canonicalDir, schemasDir, tracker, converter, bookmarkReader, llm,
                matcher: realMatcher);

            // Act — full extraction; force=true avoids the "already exists" guard.
            await orchestrator.ExtractAsync(bookId, force: true, errorsOnly: false, ct: CancellationToken.None);

            // Assert: LLM was NOT called — FIREBALL already in checkpoint → doneIds hit → skipped.
            await llm.DidNotReceive().ExtractAsync(
                Arg.Any<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionRequest>(),
                Arg.Any<CancellationToken>());

            // Assert: canonical has exactly one Fireball entity — no duplicate.
            var canonicalPath = Path.Combine(canonicalDir, bookSlug + ".json");
            var canonicalJson = await File.ReadAllTextAsync(canonicalPath);
            var canonical = System.Text.Json.JsonSerializer.Deserialize<
                DndMcpAICsharpFun.Domain.Entities.CanonicalJsonFile>(canonicalJson, checkpointOpts);
            canonical.Should().NotBeNull();
            canonical!.Entities.Should().HaveCount(1,
                "FIREBALL was already in checkpoint; must not be re-extracted and duplicated");
            canonical.Entities[0].Id.Should().Be(canonicalFireballId);
        }
        finally
        {
            try { Directory.Delete(canonicalDir, true); } catch { }
            try { Directory.Delete(schemasDir,   true); } catch { }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Task 4 integration — official-gated allowlist gate fires in orchestrator
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Official_gated_noise_is_declined_not_extracted_and_recorded()
    {
        // Arrange — official record (FivetoolsSourceKey "PHB").
        //
        // The gate is exercised via the REAL section/heading path (EntityCandidateScanner),
        // representative of actual chapter-body noise in the Classes chapter:
        //
        // Page 1: "Rage" heading under a "Barbarian" bookmark (no stat-block cues in body text).
        //   BookmarkTocMapper.Map calls HeadingCategoryClassifier.Guess("Barbarian") → Class.
        //   TocCategoryMap maps page 1 to ContentCategory.Class.
        //   EntityCandidateScanner.Scan assigns ContentCategory.Class for page 1, then
        //   ExpandPrior(Class) → TypePrior = [Class, Monster, Spell, Item] (Class is PRIMARY).
        //   StatBlockScanner finds no size-type + AC line → yields nothing for page 1.
        //   DeterministicTypeResolver.Resolve (isOfficial=true):
        //     1. 5etools has no "Rage" top-level entity ("Rage" lives in classFeature[],
        //        NOT in the indexed class[] array) → no 5etools match.
        //     2. IsEntityLikeName("Rage") = true.
        //     3. IsCompleteStatBlock = false (no AC/HP/Challenge in body text).
        //     4. IsMagicItem = false.
        //     5. isOfficial=true, TypePrior[0]=Class ∈ GatedTypes → Decline("no_5etools_match").
        //
        // Page 2: "FIREBALL" under a "Spells" bookmark → real 5etools match →
        //   ForceType(Spell, "Fireball") → extracted via LLM.
        var canonicalDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var schemasDir   = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(canonicalDir);
        Directory.CreateDirectory(schemasDir);

        // Spell schema must be present so FIREBALL can be extracted via the ForceType(Spell) path.
        File.WriteAllText(
            Path.Combine(schemasDir, "SpellFields.schema.json"),
            "{ \"type\": \"object\" }");

        try
        {
            const int bookId = 600;
            const string displayName = "Player's Handbook";

            var record = new DndMcpAICsharpFun.Domain.IngestionRecord
            {
                Id                = bookId,
                FilePath          = "/dev/null",
                FileName          = "phb.pdf",
                FileHash          = "phb-hash-1",
                Version           = "5e",
                DisplayName       = displayName,
                FivetoolsSourceKey = "PHB",
            };

            var tracker = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Tracking.IIngestionTracker>();
            tracker.GetByIdAsync(bookId, Arg.Any<CancellationToken>()).Returns(record);

            // Page 1 — "Rage" heading with plain class-feature body text.
            //   No size-type line, no "Armor Class" → StatBlockScanner yields nothing for page 1.
            //   EntityCandidateScanner maps page 1 to Class via the "Barbarian" bookmark below,
            //   producing TypePrior=[Class, Monster, Spell, Item] (Class is the PRIMARY gated type).
            // Page 2 — "FIREBALL" heading with brief text.
            //   Spells bookmark → EntityCandidateScanner produces a Spell candidate.
            var converter = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Pdf.IPdfStructureConverter>();
            var converterDoc = new DndMcpAICsharpFun.Features.Ingestion.Pdf.PdfStructureDocument(
                "doc",
                new List<DndMcpAICsharpFun.Features.Ingestion.Pdf.PdfStructureItem>
                {
                    // Page 1 — class feature "Rage" (chapter-body noise; no stat-block cues)
                    new("section_header", "Rage",                                                          1, null),
                    new("text",           "When you enter a rage, you gain advantage on Strength checks.", 1, null),
                    // Page 2 — real spell
                    new("section_header", "FIREBALL",                2, null),
                    new("text",           "A bright streak of fire.", 2, null),
                });
            converter.ConvertAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(converterDoc);

            // "Barbarian" bookmark on page 1:
            //   HeadingCategoryClassifier.Guess("Barbarian") = ContentCategory.Class
            //   → TocCategoryMap maps page 1 to ContentCategory.Class → EntityType.Class (gated).
            // "Spells" bookmark on page 2 → ContentCategory.Spell.
            var bookmarkReader = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Pdf.IPdfBookmarkReader>();
            bookmarkReader.ReadBookmarks(Arg.Any<string>()).Returns(
                new List<DndMcpAICsharpFun.Features.Ingestion.Pdf.PdfBookmark>
                {
                    new("Barbarian", 1),
                    new("Spells",    2),
                });

            // LLM returns success with empty fields — we only care about call count.
            var llm = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.IEntityExtractionLlmClient>();
            using var emptyFields = System.Text.Json.JsonDocument.Parse("{}");
            llm.ExtractAsync(
                    Arg.Any<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionRequest>(),
                    Arg.Any<CancellationToken>())
               .Returns(new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionResponse(
                   Success: true,
                   ToolInput: emptyFields.RootElement.Clone(),
                   StopReason: "tool_use",
                   InputTokens: 0,
                   OutputTokens: 0,
                   ErrorMessage: null,
                   RawJson: null));

            // Real 5etools index — "Fireball" is a Spell; "Rage" is a classFeature (NOT indexed
            // in the "class" array), so it genuinely does not match.
            var realMatcher = new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityNameMatcher(
                new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityNameIndex(
                    TestPaths.RepoFile("5etools")));

            var orchestrator = BuildOrchestrator(
                canonicalDir, schemasDir, tracker, converter, bookmarkReader, llm,
                matcher: realMatcher);

            // Act
            await orchestrator.ExtractAsync(bookId, force: true, errorsOnly: false, ct: CancellationToken.None);

            // ── Assert 1: LLM was called exactly once — for FIREBALL only; NOT for "Rage".
            await llm.Received(1).ExtractAsync(
                Arg.Any<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionRequest>(),
                Arg.Any<CancellationToken>());

            // ── Assert 2: "Rage" is absent from the canonical entities.
            var bookSlug     = DndMcpAICsharpFun.Domain.Entities.EntityIdSlug
                .For(record.FivetoolsSourceKey!, DndMcpAICsharpFun.Domain.Entities.EntityType.Class, "x")
                .Split('.')[0];
            var canonicalPath = Path.Combine(canonicalDir, bookSlug + ".json");

            File.Exists(canonicalPath).Should().BeTrue("canonical JSON must be written");

            var canonicalJson = await File.ReadAllTextAsync(canonicalPath);
            var canonical = System.Text.Json.JsonSerializer.Deserialize<
                DndMcpAICsharpFun.Domain.Entities.CanonicalJsonFile>(
                canonicalJson,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
                {
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                });

            canonical.Should().NotBeNull();
            canonical!.Entities.Should().NotContain(
                e => e.Name == "Rage",
                "declined noise candidates must not appear in the canonical output");

            // ── Assert 3: <slug>.declined.json exists and records "Rage" with no_5etools_match
            //   and primary type Class — confirming the gate fired via the section/heading path.
            //   A stat-block path would produce primary type Monster; Class here proves it is the
            //   real chapter-body noise path from EntityCandidateScanner.
            var declinedPath = Path.Combine(canonicalDir, bookSlug + ".declined.json");
            File.Exists(declinedPath).Should().BeTrue(
                "declined.json must be written when at least one official candidate is declined");

            var declinedJson = await File.ReadAllTextAsync(declinedPath);
            var declined = System.Text.Json.JsonSerializer.Deserialize<
                List<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.DeclinedEntry>>(
                declinedJson,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
                {
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                });

            declined.Should().NotBeNullOrEmpty("at least 'Rage' must be in the declined list");
            declined!.Should().Contain(
                d => d.Name == "Rage"
                  && d.Reason == "no_5etools_match"
                  && d.Type == DndMcpAICsharpFun.Domain.Entities.EntityType.Class,
                "gated noise candidate 'Rage' must be declined with reason no_5etools_match " +
                "and primary type Class (section/heading path), not Monster (stat-block path)");
        }
        finally
        {
            try { Directory.Delete(canonicalDir, true); } catch { }
            try { Directory.Delete(schemasDir,   true); } catch { }
        }
    }

    // ── BuildScannerInputs traceability guard (Fix #2a) ─────────────────────────

    [Fact]
    public async Task BuildScannerInputs_logs_warning_when_heading_overwrites_heading_with_no_body()
    {
        // Traceability guard: when a section_header immediately follows another section_header
        // with no body text between them, the prior section will never become a candidate.
        // BuildScannerInputs must emit a LogWarning naming both the dropped and the next title.
        var canonicalDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var schemasDir   = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(canonicalDir);
        Directory.CreateDirectory(schemasDir);
        try
        {
            const int bookId = 77;
            var record = new DndMcpAICsharpFun.Domain.IngestionRecord
            {
                Id = bookId, FilePath = "/dev/null", FileName = "test.pdf",
                FileHash = "abc", Version = "5e", DisplayName = "Warn Book",
            };
            var tracker = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Tracking.IIngestionTracker>();
            tracker.GetByIdAsync(bookId, Arg.Any<CancellationToken>()).Returns(record);

            // Two consecutive section_headers with NO body between them → the first is dropped.
            var converter = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Pdf.IPdfStructureConverter>();
            converter.ConvertAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
                new DndMcpAICsharpFun.Features.Ingestion.Pdf.PdfStructureDocument("doc",
                    new List<DndMcpAICsharpFun.Features.Ingestion.Pdf.PdfStructureItem>
                    {
                        new("section_header", "MORDENKAINEN'S SWORD",    1, null),
                        new("section_header", "Casting Time: 1 action",  1, null), // no body between
                        new("text",           "You create a blade.",      1, null),
                    }));

            var bookmarkReader = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Pdf.IPdfBookmarkReader>();
            bookmarkReader.ReadBookmarks(Arg.Any<string>()).Returns(
                new List<DndMcpAICsharpFun.Features.Ingestion.Pdf.PdfBookmark> { new("Spells", 1) });

            var llm = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.IEntityExtractionLlmClient>();
            // BuildScannerInputs (and its traceability warning) now live on EntityCandidateBuilder,
            // so capture the builder's logger. The assertion below is unchanged.
            var capturingLogger = new CapturingLogger<EntityCandidateBuilder>();

            var orchestrator = BuildOrchestrator(
                canonicalDir, schemasDir, tracker, converter, bookmarkReader, llm,
                builderLogger: capturingLogger);

            await orchestrator.ExtractAsync(bookId, force: true, errorsOnly: false, ct: CancellationToken.None);

            capturingLogger.Logs.Should().Contain(
                l => l.Level == LogLevel.Warning
                  && l.Message.Contains("MORDENKAINEN'S SWORD")
                  && l.Message.Contains("Casting Time: 1 action"),
                "a warning must name the dropped section and the overwriting heading");
        }
        finally
        {
            try { Directory.Delete(canonicalDir, true); } catch { }
            try { Directory.Delete(schemasDir,   true); } catch { }
        }
    }

    [Fact]
    public async Task BuildScannerInputs_does_not_log_warning_when_heading_follows_body()
    {
        // Sanity guard: a normal heading→body→heading sequence must NOT trigger the warning.
        var canonicalDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var schemasDir   = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(canonicalDir);
        Directory.CreateDirectory(schemasDir);
        try
        {
            const int bookId = 78;
            var record = new DndMcpAICsharpFun.Domain.IngestionRecord
            {
                Id = bookId, FilePath = "/dev/null", FileName = "test.pdf",
                FileHash = "abc", Version = "5e", DisplayName = "Clean Book",
            };
            var tracker = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Tracking.IIngestionTracker>();
            tracker.GetByIdAsync(bookId, Arg.Any<CancellationToken>()).Returns(record);

            // Normal pattern: heading → body → heading → body.
            var converter = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Pdf.IPdfStructureConverter>();
            converter.ConvertAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
                new DndMcpAICsharpFun.Features.Ingestion.Pdf.PdfStructureDocument("doc",
                    new List<DndMcpAICsharpFun.Features.Ingestion.Pdf.PdfStructureItem>
                    {
                        new("section_header", "FIREBALL",           1, null),
                        new("text",           "A bright streak...", 1, null),
                        new("section_header", "SLEEP",              1, null),
                        new("text",           "This spell sends...", 1, null),
                    }));

            var bookmarkReader = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Pdf.IPdfBookmarkReader>();
            bookmarkReader.ReadBookmarks(Arg.Any<string>()).Returns(
                new List<DndMcpAICsharpFun.Features.Ingestion.Pdf.PdfBookmark> { new("Spells", 1) });

            var llm = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.IEntityExtractionLlmClient>();
            // BuildScannerInputs (and its traceability warning) now live on EntityCandidateBuilder,
            // so capture the builder's logger. The assertion below is unchanged.
            var capturingLogger = new CapturingLogger<EntityCandidateBuilder>();

            var orchestrator = BuildOrchestrator(
                canonicalDir, schemasDir, tracker, converter, bookmarkReader, llm,
                builderLogger: capturingLogger);

            await orchestrator.ExtractAsync(bookId, force: true, errorsOnly: false, ct: CancellationToken.None);

            capturingLogger.Logs.Should().NotContain(
                l => l.Level == LogLevel.Warning
                  && (l.Message.Contains("FIREBALL") || l.Message.Contains("SLEEP"))
                  && l.Message.Contains("received no body"),
                "no silent-drop warning should fire when each heading is followed by body text");
        }
        finally
        {
            try { Directory.Delete(canonicalDir, true); } catch { }
            try { Directory.Delete(schemasDir,   true); } catch { }
        }
    }
}
