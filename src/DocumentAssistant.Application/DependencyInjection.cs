using DocumentAssistant.Application.Documents;
using Microsoft.Extensions.DependencyInjection;

namespace DocumentAssistant.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<DocumentService>();
        return services;
    }
}
