using DocumentAssistant.Application.Abstractions;
using DocumentAssistant.Infrastructure.Persistence;
using DocumentAssistant.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DocumentAssistant.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, string connectionString, string storageRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageRootPath);

        services.AddDbContext<DocumentAssistantDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql.UseVector())
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IDocumentRepository, DocumentRepository>();
        services.AddSingleton<IFileStorage>(_ => new LocalFileStorage(storageRootPath));

        return services;
    }
}
