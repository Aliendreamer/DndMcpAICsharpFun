using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Entities;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities;

public class CanonicalJsonLoaderKnowledgeTests
{
    private static string MinimalJson(
        string tablesJson = "[]",
        string choiceSetsJson = "[]",
        string entitiesJson = "[]") =>
        $$"""
        {
          "schemaVersion": "1",
          "book": {
            "sourceBook": "Test Knowledge Book",
            "edition": "5e",
            "fileHash": "abc123",
            "displayName": "Test Knowledge Book"
          },
          "entities": {{entitiesJson}},
          "tables": {{tablesJson}},
          "choiceSets": {{choiceSetsJson}}
        }
        """;

    // ──────────────────────────────────────────────────────────────────
    // Test A: happy path — one table, one choiceSet whose option references it
    // ──────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Load_carries_tables_and_choicesets()
    {
        var tablesJson = """
        [
          {
            "id": "tb.table.hit-dice",
            "name": "Hit Dice Table",
            "columns": ["Level", "Hit Dice"],
            "rows": [
              {
                "cells": [
                  { "value": "1", "provenance": { "blockId": "blk-001", "sourceBook": "Test Knowledge Book", "page": 10 } },
                  { "value": "1d10", "provenance": null }
                ]
              }
            ]
          }
        ]
        """;

        var choiceSetsJson = """
        [
          {
            "id": "tb.choiceset.equipment",
            "name": "Starting Equipment",
            "options": [
              {
                "key": "a",
                "tableId": "tb.table.hit-dice",
                "rowIndex": 0,
                "provenance": null
              }
            ]
          }
        ]
        """;

        var json = MinimalJson(tablesJson, choiceSetsJson);
        var tmp = Path.GetTempFileName();
        await File.WriteAllTextAsync(tmp, json);
        try
        {
            var loader = new CanonicalJsonLoader();
            var file = await loader.LoadAsync(tmp, CancellationToken.None);

            file.Tables.Should().HaveCount(1);
            file.Tables[0].Id.Should().Be("tb.table.hit-dice");
            file.Tables[0].Name.Should().Be("Hit Dice Table");
            file.Tables[0].Columns.Should().HaveCount(2);
            file.Tables[0].Rows.Should().HaveCount(1);
            file.Tables[0].Rows[0].Cells.Should().HaveCount(2);
            file.Tables[0].Rows[0].Cells[0].Value.Should().Be("1");
            file.Tables[0].Rows[0].Cells[0].Provenance.Should().NotBeNull();
            file.Tables[0].Rows[0].Cells[0].Provenance!.BlockId.Should().Be("blk-001");

            file.ChoiceSets.Should().HaveCount(1);
            file.ChoiceSets[0].Id.Should().Be("tb.choiceset.equipment");
            file.ChoiceSets[0].Options.Should().HaveCount(1);
            file.ChoiceSets[0].Options[0].TableId.Should().Be("tb.table.hit-dice");
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    // ──────────────────────────────────────────────────────────────────
    // Test B: duplicate table ids → throws CanonicalJsonSchemaException
    // ──────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Load_rejects_duplicate_table_ids()
    {
        var tablesJson = """
        [
          { "id": "tb.table.dup", "name": "Table A", "columns": ["Col"], "rows": [] },
          { "id": "tb.table.dup", "name": "Table B", "columns": ["Col"], "rows": [] }
        ]
        """;

        var json = MinimalJson(tablesJson);
        var tmp = Path.GetTempFileName();
        await File.WriteAllTextAsync(tmp, json);
        try
        {
            var loader = new CanonicalJsonLoader();
            var act = async () => await loader.LoadAsync(tmp, CancellationToken.None);
            await act.Should().ThrowAsync<CanonicalJsonSchemaException>()
                     .WithMessage("*duplicate*");
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    // ──────────────────────────────────────────────────────────────────
    // Backward compat: entities-only file (no tables key) → Tables defaults empty
    // ──────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Load_tables_default_empty_when_absent()
    {
        var json = """
        {
          "schemaVersion": "1",
          "book": {
            "sourceBook": "Legacy Book",
            "edition": "5e",
            "fileHash": "xyz",
            "displayName": "Legacy Book"
          },
          "entities": []
        }
        """;

        var tmp = Path.GetTempFileName();
        await File.WriteAllTextAsync(tmp, json);
        try
        {
            var loader = new CanonicalJsonLoader();
            var file = await loader.LoadAsync(tmp, CancellationToken.None);
            file.Tables.Should().BeEmpty();
            file.ChoiceSets.Should().BeEmpty();
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    // ──────────────────────────────────────────────────────────────────
    // Extra: ChoiceOption.TableId that does not resolve → throws
    // ──────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Load_rejects_choiceoption_with_unresolvable_tableId()
    {
        var choiceSetsJson = """
        [
          {
            "id": "tb.choiceset.broken",
            "name": "Broken Set",
            "options": [
              { "key": "a", "tableId": "tb.table.nonexistent", "rowIndex": 0, "provenance": null }
            ]
          }
        ]
        """;

        var json = MinimalJson("[]", choiceSetsJson);
        var tmp = Path.GetTempFileName();
        await File.WriteAllTextAsync(tmp, json);
        try
        {
            var loader = new CanonicalJsonLoader();
            var act = async () => await loader.LoadAsync(tmp, CancellationToken.None);
            await act.Should().ThrowAsync<CanonicalJsonSchemaException>();
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}
