using System.Text.Json.Serialization;
using DocumentAssistant.Api.ErrorHandling;
using DocumentAssistant.Application;
using DocumentAssistant.Application.Documents;
using DocumentAssistant.Application.Embeddings;
using DocumentAssistant.Application.Rag;
using DocumentAssistant.Application.Text;
using DocumentAssistant.Infrastructure;
using DocumentAssistant.Infrastructure.AI;
using DocumentAssistant.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

var connectionString = builder.Configuration.GetConnectionString("DocumentAssistant")
    ?? throw new InvalidOperationException("Connection string 'DocumentAssistant' is not configured.");
var storageRootPath = builder.Configuration["Storage:RootPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "uploads");

builder.Services.AddApplication();
builder.Services.AddInfrastructure(connectionString, storageRootPath);

// Upload size: the service enforces the limit; the server limits allow it plus multipart overhead.
var uploadSection = builder.Configuration.GetSection(DocumentUploadOptions.SectionName);
builder.Services.Configure<DocumentUploadOptions>(uploadSection);
var maxRequestBytes = (uploadSection.Get<DocumentUploadOptions>() ?? new()).MaxFileSizeBytes + 1024 * 1024;
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = maxRequestBytes);
builder.Services.Configure<FormOptions>(form => form.MultipartBodyLengthLimit = maxRequestBytes);

builder.Services.Configure<ChunkingOptions>(builder.Configuration.GetSection(ChunkingOptions.SectionName));

var embeddingsSection = builder.Configuration.GetSection(EmbeddingOptions.SectionName);
builder.Services.Configure<EmbeddingOptions>(embeddingsSection);
builder.Services.AddEmbeddingProvider(embeddingsSection.Get<AiProviderOptions>() ?? new());

var ragSection = builder.Configuration.GetSection(RagOptions.SectionName);
builder.Services.Configure<RagOptions>(ragSection);
builder.Services.AddChatProvider(builder.Configuration.GetSection("Chat").Get<AiProviderOptions>() ?? new());

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApplicationExceptionHandler>();

builder.Services.AddControllers()
    .AddJsonOptions(json => json.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// In Docker the database starts empty, so apply migrations at startup there (off by default).
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<DocumentAssistantDbContext>().Database.MigrateAsync();
}

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// The Docker image puts the built React app in wwwroot; in development Vite serves it instead.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthorization();

app.MapControllers();
// Unknown non-API paths get the React app; unknown /api paths still return 404.
app.MapFallbackToFile("{*path:regex(^(?!api/).*$)}", "index.html");

app.Run();

// Lets integration tests reference the entry point via WebApplicationFactory<Program>.
public partial class Program;
