using DocumentAssistant.Application.Documents;
using DocumentAssistant.Application.Embeddings;
using DocumentAssistant.Application.Text;
using Microsoft.Extensions.DependencyInjection;

namespace DocumentAssistant.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<DocumentService>();
        services.AddScoped<DocumentProcessor>();
        services.AddSingleton<TextChunker>();
        services.AddScoped<ChunkEmbedder>();
        return services;
    }
}
