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
}
