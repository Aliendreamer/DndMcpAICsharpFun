using System.Net;
using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.Retrieval.Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DndMcpAICsharpFun.Tests.Entities.Retrieval;

public sealed class EntityRetrievalEndpointsTests
{
    private static async Task<(HttpClient Client, IEntityRetrievalService Svc)> BuildClientAsync()
    {
        var svc = Substitute.For<IEntityRetrievalService>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(svc);
        builder.Services.Configure<AdminOptions>(o => o.ApiKey = "test-key");

        var app = builder.Build();
        app.MapEntityRetrievalEndpoints();

        await app.StartAsync();
        return (app.GetTestClient(), svc);
    }

    [Fact]
    public async Task Get_by_id_returns_404_for_unknown_id()
    {
        var (client, svc) = await BuildClientAsync();
        svc.GetByIdAsync("does-not-exist", Arg.Any<CancellationToken>())
            .Returns((EntityFullResult?)null);

        var response = await client.GetAsync("/retrieval/entities/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Search_returns_400_when_q_missing()
    {
        var (client, _) = await BuildClientAsync();

        var response = await client.GetAsync("/retrieval/entities/search");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
