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
                registry: new DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.BookSourceRegistry(
                    Path.Combine(Path.GetTempPath(), "__nonexistent_books__.json")),
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
                chunker: new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.SemanticChunker(),
                merger: new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityFieldMerger(),
                options: opts,
                ollamaOpts: ollamaOpts,
                logger: NullLogger<EntityExtractionOrchestrator>.Instance);

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
        DndMcpAICsharpFun.Infrastructure.Sqlite.IngestionRecord record,
        DndMcpAICsharpFun.Features.Ingestion.Tracking.IIngestionTracker tracker,
        DndMcpAICsharpFun.Features.Ingestion.Pdf.IDoclingPdfConverter docling,
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

        var record = new DndMcpAICsharpFun.Infrastructure.Sqlite.IngestionRecord
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
        var docling = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Pdf.IDoclingPdfConverter>();
        var doclingDoc = new DndMcpAICsharpFun.Features.Ingestion.Pdf.DoclingDocument(
            "doc",
            new List<DndMcpAICsharpFun.Features.Ingestion.Pdf.DoclingItem>
            {
                new("heading", "Aboleth", 1, null),
                new("text",    "Aboleth — a slimy aberration.", 1, null),
                new("heading", "Beholder", 2, null),
                new("text",    "Beholder — a tyrant.", 2, null),
            });
        docling.ConvertAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(doclingDoc);

        var bookmarkReader = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Pdf.IPdfBookmarkReader>();
        bookmarkReader.ReadBookmarks(Arg.Any<string>()).Returns(
            new List<DndMcpAICsharpFun.Features.Ingestion.Pdf.PdfBookmark>
            {
                new("Monsters", 1),
                new("Monsters", 2),
            });

        var llm = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.IEntityExtractionLlmClient>();

        return (canonicalDir, schemasDir, record, tracker, docling, bookmarkReader, llm);
    }

    private static EntityExtractionOrchestrator BuildOrchestrator(
        string canonicalDir,
        string schemasDir,
        DndMcpAICsharpFun.Features.Ingestion.Tracking.IIngestionTracker tracker,
        DndMcpAICsharpFun.Features.Ingestion.Pdf.IDoclingPdfConverter docling,
        DndMcpAICsharpFun.Features.Ingestion.Pdf.IPdfBookmarkReader bookmarkReader,
        DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.IEntityExtractionLlmClient llm,
        DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.BookSourceRegistry? registry = null)
    {
        var opts = Options.Create(new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityExtractionOptions
        {
            CanonicalDirectory = canonicalDir,
            SchemasDirectory   = schemasDir,
        });
        var ollamaOpts = Options.Create(new DndMcpAICsharpFun.Infrastructure.Ollama.OllamaOptions());
        // Use an empty registry (no books.json) when caller does not supply one.
        var effectiveRegistry = registry
            ?? new DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.BookSourceRegistry(
                   Path.Combine(Path.GetTempPath(), "__nonexistent_books__.json"));

        return new EntityExtractionOrchestrator(
            tracker:       tracker,
            registry:      effectiveRegistry,
            docling:       docling,
            bookmarks:     bookmarkReader,
            llm:           llm,
            promptBuilder: new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionPromptBuilder(),
            scanner:       new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityCandidateScanner(),
            writer:        new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.CanonicalJsonWriter(),
            errorsFile:    new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionErrorsFile(),
            warningsFile:  new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionWarningsFile(),
            refResolver:   new DndMcpAICsharpFun.Features.Entities.EntityReferenceResolver(),
            retry:         new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionRetryPolicy { MaxAttempts = 1 },
            chunker:       new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.SemanticChunker(),
            merger:        new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityFieldMerger(),
            options:       opts,
            ollamaOpts:    ollamaOpts,
            logger:        NullLogger<EntityExtractionOrchestrator>.Instance);
    }

    [Fact]
    public async Task ErrorsOnly_skips_candidates_not_in_retry_set()
    {
        const int bookId = 100;
        const string displayName = "Test Book";

        var (canonicalDir, schemasDir, record, tracker, docling, bookmarkReader, llm)
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

            var orchestrator = BuildOrchestrator(canonicalDir, schemasDir, tracker, docling, bookmarkReader, llm);

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

        var (canonicalDir, schemasDir, record, tracker, docling, bookmarkReader, llm)
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

            var orchestrator = BuildOrchestrator(canonicalDir, schemasDir, tracker, docling, bookmarkReader, llm);

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

        var (canonicalDir, schemasDir, record, tracker, docling, bookmarkReader, llm)
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

            var orchestrator = BuildOrchestrator(canonicalDir, schemasDir, tracker, docling, bookmarkReader, llm);

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

        var (canonicalDir, schemasDir, record, tracker, docling, bookmarkReader, llm)
            = BuildTwoMonsterHarness(bookId, displayName);

        try
        {
            var bookSlug      = "test-book";
            var canonicalPath = Path.Combine(canonicalDir, bookSlug + ".json");
            File.Exists(canonicalPath).Should().BeFalse("test pre-condition: no canonical file");

            var orchestrator = BuildOrchestrator(canonicalDir, schemasDir, tracker, docling, bookmarkReader, llm);

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

        var (canonicalDir, schemasDir, record, tracker, docling, bookmarkReader, llm)
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

            var orchestrator = BuildOrchestrator(canonicalDir, schemasDir, tracker, docling, bookmarkReader, llm);

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
            var record = new DndMcpAICsharpFun.Infrastructure.Sqlite.IngestionRecord
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

            var docling = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Pdf.IDoclingPdfConverter>();
            var doclingDoc = new DndMcpAICsharpFun.Features.Ingestion.Pdf.DoclingDocument(
                "doc",
                new List<DndMcpAICsharpFun.Features.Ingestion.Pdf.DoclingItem>
                {
                    new("heading", "Pit Trap", 1, null),
                    new("text",    part1,       1, null),
                    new("text",    part2,       1, null),
                });
            docling.ConvertAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(doclingDoc);

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
            var ollamaOpts = Options.Create(new DndMcpAICsharpFun.Infrastructure.Ollama.OllamaOptions());
            var registry   = new DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.BookSourceRegistry(
                Path.Combine(Path.GetTempPath(), "__nonexistent_books__.json"));

            var orchestrator = new EntityExtractionOrchestrator(
                tracker:       tracker,
                registry:      registry,
                docling:       docling,
                bookmarks:     bookmarkReader,
                llm:           llm,
                promptBuilder: new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionPromptBuilder(),
                scanner:       new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityCandidateScanner(),
                writer:        new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.CanonicalJsonWriter(),
                errorsFile:    new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionErrorsFile(),
                warningsFile:  new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionWarningsFile(),
                refResolver:   new DndMcpAICsharpFun.Features.Entities.EntityReferenceResolver(),
                retry:         new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.ExtractionRetryPolicy { MaxAttempts = 1 },
                chunker:       new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.SemanticChunker(),
                merger:        new DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.EntityFieldMerger(),
                options:       opts,
                ollamaOpts:    ollamaOpts,
                logger:        NullLogger<EntityExtractionOrchestrator>.Instance);

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
            var record = new DndMcpAICsharpFun.Infrastructure.Sqlite.IngestionRecord
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
            var docling = Substitute.For<DndMcpAICsharpFun.Features.Ingestion.Pdf.IDoclingPdfConverter>();
            var doclingDoc = new DndMcpAICsharpFun.Features.Ingestion.Pdf.DoclingDocument(
                "doc",
                new List<DndMcpAICsharpFun.Features.Ingestion.Pdf.DoclingItem>
                {
                    new("heading", "Aboleth", 1, null),
                    new("text",    "Aboleth — a slimy aberration.", 1, null),
                });
            docling.ConvertAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(doclingDoc);

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

            var orchestrator = BuildOrchestrator(
                canonicalDir, schemasDir, tracker, docling, bookmarkReader, llm, registry);

            // Act
            await orchestrator.ExtractAsync(bookId, force: true, errorsOnly: false, ct: CancellationToken.None);

            // Assert: canonical JSON must exist and carry source="PHB", edition="Edition2014".
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
}
