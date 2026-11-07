using DocumentAssistant.Api.ErrorHandling;
using DocumentAssistant.Application;
using DocumentAssistant.Application.Documents;
using DocumentAssistant.Infrastructure;
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

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ValidationExceptionHandler>();

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

// Lets integration tests reference the entry point via WebApplicationFactory<Program>.
public partial class Program;
