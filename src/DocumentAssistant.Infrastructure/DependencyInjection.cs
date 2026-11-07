using DocumentAssistant.Application.Abstractions;
using DocumentAssistant.Infrastructure.Extraction;
using DocumentAssistant.Infrastructure.Persistence;
using DocumentAssistant.Infrastructure.Processing;
using DocumentAssistant.Infrastructure.Retrieval;
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
        services.AddScoped<IChunkSearch, PgChunkSearch>();
        services.AddSingleton<IFileStorage>(_ => new LocalFileStorage(storageRootPath));
        services.AddSingleton<IPdfTextExtractor, PdfPigTextExtractor>();

        services.AddSingleton<ChannelDocumentProcessingQueue>();
        services.AddSingleton<IDocumentProcessingQueue>(sp => sp.GetRequiredService<ChannelDocumentProcessingQueue>());
        services.AddHostedService<DocumentProcessingWorker>();

        return services;
    }
}
