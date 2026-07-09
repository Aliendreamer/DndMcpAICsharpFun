using DndMcpAICsharpFun.Extensions;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Features.Retrieval.Entities;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Retrieval;

// Regression guard: FusedRetrievalService consumes the scoped IEmbeddingService, so it MUST be
// registered scoped. Registering it as a singleton made the app fail DI scope-validation at startup
// ("Cannot consume scoped service IEmbeddingService from singleton IFusedRetrievalService").
public class RetrievalRegistrationTests
{
    [Fact]
    public void FusedRetrievalService_is_registered_scoped()
    {
        var services = new ServiceCollection().AddRetrieval();

        var descriptor = services.Single(s => s.ServiceType == typeof(IFusedRetrievalService));

        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void Retrieval_services_that_use_embeddings_share_the_scoped_lifetime()
    {
        var services = new ServiceCollection().AddRetrieval();

        ServiceLifetime LifetimeOf(Type t) => services.Single(s => s.ServiceType == t).Lifetime;

        // All three retrieval services depend (directly or transitively) on the scoped IEmbeddingService,
        // so none may be a singleton.
        Assert.Equal(ServiceLifetime.Scoped, LifetimeOf(typeof(IFusedRetrievalService)));
        Assert.Equal(ServiceLifetime.Scoped, LifetimeOf(typeof(IRagRetrievalService)));
        Assert.Equal(ServiceLifetime.Scoped, LifetimeOf(typeof(IEntityRetrievalService)));
    }
}