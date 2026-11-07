using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using DocumentAssistant.Application.Common;
using DocumentAssistant.Application.Documents;
using DocumentAssistant.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DocumentAssistant.IntegrationTests.Api;

/// <summary>
/// Runs the real API in memory against the Testcontainers database and a temp storage folder.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DocumentsApiTests : IDisposable
{
    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), "docassistant-api-tests", Guid.NewGuid().ToString());
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public DocumentsApiTests(PostgresFixture fixture)
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Testing") // skip Development settings and user-secrets
            .UseSetting("ConnectionStrings:DocumentAssistant", fixture.ConnectionString)
            .UseSetting("Storage:RootPath", _storageRoot));
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        if (Directory.Exists(_storageRoot))
            Directory.Delete(_storageRoot, recursive: true);
    }

    private static MultipartFormDataContent FileForm(string fileName, string text)
    {
        var file = new ByteArrayContent(Encoding.ASCII.GetBytes(text));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    [Fact]
    public async Task Upload_ValidPdf_Returns201_AndDocumentCanBeFetched()
    {
        var response = await _client.PostAsync("/api/documents", FileForm("guide.pdf", "%PDF-1.4\nbody"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<DocumentDto>();
        Assert.NotNull(created);
        Assert.Equal("guide.pdf", created.FileName);
        Assert.Equal("Uploaded", created.Status);
        Assert.Equal($"/api/documents/{created.Id}", response.Headers.Location?.AbsolutePath);
        Assert.True(File.Exists(Path.Combine(_storageRoot, DocumentService.StorageKeyFor(created.Id))));

        var fetched = await _client.GetFromJsonAsync<DocumentDto>(response.Headers.Location);
        Assert.Equal(created, fetched);

        var list = await _client.GetFromJsonAsync<PagedResult<DocumentDto>>("/api/documents?pageSize=100");
        Assert.Contains(list!.Items, d => d.Id == created.Id);
    }

    [Fact]
    public async Task Upload_NotAPdf_Returns400ProblemDetails()
    {
        var response = await _client.PostAsync("/api/documents", FileForm("fake.pdf", "just text"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("The file is not a valid PDF.", problem?.Detail);
    }

    [Fact]
    public async Task Upload_WithoutFile_Returns400()
    {
        var response = await _client.PostAsync("/api/documents", new MultipartFormDataContent());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetById_Unknown_Returns404()
    {
        var response = await _client.GetAsync($"/api/documents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
