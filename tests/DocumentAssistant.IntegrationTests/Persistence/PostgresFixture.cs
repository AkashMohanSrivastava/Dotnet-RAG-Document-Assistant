using DocumentAssistant.Infrastructure;
using DocumentAssistant.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace DocumentAssistant.IntegrationTests.Persistence;

/// <summary>
/// Starts a throwaway PostgreSQL + pgvector container (same image as local dev),
/// applies migrations once, and hands out DbContexts wired exactly like the API.
/// Requires Docker to be running.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg17-bookworm").Build();

    private ServiceProvider _services = null!;

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        _services = new ServiceCollection()
            .AddInfrastructure(ConnectionString, Path.Combine(Path.GetTempPath(), "docassistant-fixture"))
            .BuildServiceProvider();

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    /// <summary>A fresh context per unit of work, so reads really hit the database.</summary>
    public DocumentAssistantDbContext CreateContext() =>
        _services.CreateScope().ServiceProvider.GetRequiredService<DocumentAssistantDbContext>();

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}
