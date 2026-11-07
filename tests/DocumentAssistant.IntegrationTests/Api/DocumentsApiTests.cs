using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DocumentAssistant.Application.Common;
using DocumentAssistant.Application.Documents;
using DocumentAssistant.Application.Rag;
using DocumentAssistant.Application.Retrieval;
using DocumentAssistant.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DocumentAssistant.IntegrationTests.Api;

/// <summary>
/// Runs the real API in memory against the Testcontainers database and a temp storage folder.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DocumentsApiTests : IDisposable
{
    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), "docassistant-api-tests", Guid.NewGuid().ToString());
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    // Configured exactly like production (OpenAI provider, but no API key in the Testing environment).
    private readonly WebApplicationFactory<Program> _unconfiguredAiFactory;

    // Same, but with the offline embedding generator swapped in.
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public DocumentsApiTests(PostgresFixture fixture)
    {
        _unconfiguredAiFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Testing") // skip Development settings and user-secrets
            .UseSetting("ConnectionStrings:DocumentAssistant", fixture.ConnectionString)
            .UseSetting("Storage:RootPath", _storageRoot));
        _factory = _unconfiguredAiFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services
            .AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new HashingEmbeddingGenerator())
            .AddSingleton<IChatClient>(new QuotingChatClient())));
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        _unconfiguredAiFactory.Dispose();
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

        // The background worker may already be processing it, so compare everything but the status.
        var fetched = await _client.GetFromJsonAsync<DocumentDto>(response.Headers.Location);
        Assert.Equal(created, fetched! with { Status = created.Status, FailureReason = null, ProcessedAt = null });

        var list = await _client.GetFromJsonAsync<PagedResult<DocumentDto>>("/api/documents?pageSize=100");
        Assert.Contains(list!.Items, d => d.Id == created.Id);
    }

    [Fact]
    public async Task UploadedPdf_IsProcessedInBackground_IntoChunks()
    {
        var pdf = TestPdfs.Create(
            ["Installation", "Mount the bracket on a flat wall."],
            ["Maintenance", "Clean the filter every three months."]);
        var created = await UploadAsync("manual.pdf", pdf);

        var processed = await WaitForFinalStatusAsync(created.Id);

        Assert.Equal("Ready", processed.Status);
        Assert.NotNull(processed.ProcessedAt);

        var chunks = await _client.GetFromJsonAsync<List<ChunkDto>>($"/api/documents/{created.Id}/chunks");
        Assert.Equal(2, chunks!.Count);
        Assert.Equal(new ChunkDto(chunks[0].Id, 0, 1, "Installation\nMount the bracket on a flat wall.", true), chunks[0]);
        Assert.Equal(new ChunkDto(chunks[1].Id, 1, 2, "Maintenance\nClean the filter every three months.", true), chunks[1]);
    }

    [Fact]
    public async Task Search_FindsChunkInUploadedPdf_InEveryMode()
    {
        var pdf = TestPdfs.Create(
            ["Gizmotron setup", "Charge the gizmotron battery overnight before first use."],
            ["Gizmotron cleaning", "Wipe the gizmotron lens with a dry cloth."]);
        var created = await UploadAsync("gizmotron.pdf", pdf);
        Assert.Equal("Ready", (await WaitForFinalStatusAsync(created.Id)).Status);

        foreach (var mode in new[] { "vector", "keyword", "hybrid" })
        {
            var response = await _client.GetFromJsonAsync<SearchResponse>(
                $"/api/search?q=how do I clean the gizmotron lens&mode={mode}&documentId={created.Id}", JsonOptions);

            Assert.Equal(mode, response!.Mode.ToString(), ignoreCase: true);
            Assert.Equal(2, response.Results[0].PageNumber);
        }
    }

    [Fact]
    public async Task Ask_AnswersFromUploadedPdf_WithCitedSourcePage()
    {
        var pdf = TestPdfs.Create(
            ["Flumbox introduction", "The flumbox is a pretend appliance."],
            ["Flumbox descaling", "Descale the flumbox monthly using white vinegar."]);
        var created = await UploadAsync("flumbox.pdf", pdf);
        Assert.Equal("Ready", (await WaitForFinalStatusAsync(created.Id)).Status);

        var response = await _client.PostAsJsonAsync("/api/ask",
            new { question = "How do I descale the flumbox?", documentId = created.Id });

        response.EnsureSuccessStatusCode();
        var answer = (await response.Content.ReadFromJsonAsync<AskResponse>(JsonOptions))!;
        Assert.True(answer.Answered);
        Assert.Equal("quoting-test-model", answer.Model);
        Assert.EndsWith("[1]", answer.Answer);
        var cited = Assert.Single(answer.Sources, s => s.Cited);
        Assert.Equal(1, cited.Number);
        Assert.Equal(2, cited.PageNumber); // the descaling page was retrieved first
    }

    [Fact]
    public async Task Ask_WithoutQuestion_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/ask", new { question = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_WithoutQuery_Returns400()
    {
        var response = await _client.GetAsync("/api/search?q=");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task WithoutApiKey_DocumentFails_WithInstructionsToConfigureIt()
    {
        using var client = _unconfiguredAiFactory.CreateClient();
        var created = await UploadAsync("manual.pdf", TestPdfs.Create(["Some text"]), client);

        var processed = await WaitForFinalStatusAsync(created.Id, client: client);

        Assert.Equal("Failed", processed.Status);
        Assert.Contains("No OpenAI API key is configured", processed.FailureReason);
    }

    [Fact]
    public async Task UploadedPdfWithoutText_EndsFailed_WithReason()
    {
        var created = await UploadAsync("scanned.pdf", TestPdfs.Create([], []));

        var processed = await WaitForFinalStatusAsync(created.Id);

        Assert.Equal("Failed", processed.Status);
        Assert.Contains("no extractable text", processed.FailureReason);
        Assert.Empty((await _client.GetFromJsonAsync<List<ChunkDto>>($"/api/documents/{created.Id}/chunks"))!);
    }

    [Fact]
    public async Task Process_FailedDocument_Returns202_ReadyDocument_Returns409()
    {
        var failed = await UploadAsync("scanned.pdf", TestPdfs.Create([], []));
        var ready = await UploadAsync("manual.pdf", TestPdfs.Create(["Some text"]));
        var firstAttempt = await WaitForFinalStatusAsync(failed.Id);
        await WaitForFinalStatusAsync(ready.Id);

        var retry = await _client.PostAsync($"/api/documents/{failed.Id}/process", null);
        var conflict = await _client.PostAsync($"/api/documents/{ready.Id}/process", null);
        var missing = await _client.PostAsync($"/api/documents/{Guid.NewGuid()}/process", null);

        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        // The retry runs the pipeline again and fails for the same reason.
        var secondAttempt = await WaitForFinalStatusAsync(failed.Id, finishedAfter: firstAttempt.ProcessedAt);
        Assert.Equal("Failed", secondAttempt.Status);
    }

    /// <summary>
    /// Polls until the document is Ready or Failed. With <paramref name="finishedAfter"/>, waits for
    /// a newer result, so a retry isn't confused with the previous attempt.
    /// </summary>
    private async Task<DocumentDto> WaitForFinalStatusAsync(Guid id, DateTimeOffset? finishedAfter = null, HttpClient? client = null)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            var document = (await (client ?? _client).GetFromJsonAsync<DocumentDto>($"/api/documents/{id}", timeout.Token))!;
            if (document.Status is "Ready" or "Failed" && document.ProcessedAt > (finishedAfter ?? DateTimeOffset.MinValue))
                return document;
            await Task.Delay(50, timeout.Token);
        }
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
    public async Task GetText_ReturnsExtractedTextPerPage()
    {
        var pdf = TestPdfs.Create(["Warranty terms"], ["Contact support at extension 42."]);
        var created = await UploadAsync("warranty.pdf", pdf);

        var text = await _client.GetFromJsonAsync<DocumentTextDto>($"/api/documents/{created.Id}/text");

        Assert.NotNull(text);
        Assert.Equal(2, text.PageCount);
        Assert.Equal("Warranty terms", text.Pages[0].Text);
        Assert.Equal("Contact support at extension 42.", text.Pages[1].Text);
        Assert.Equal(text.Pages.Sum(p => p.Text.Length), text.TotalCharacters);
    }

    [Fact]
    public async Task GetText_PdfWithoutText_Returns422()
    {
        var created = await UploadAsync("scanned.pdf", TestPdfs.Create([], [])); // two blank pages

        var response = await _client.GetAsync($"/api/documents/{created.Id}/text");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Contains("no extractable text", problem?.Detail);
    }

    [Fact]
    public async Task GetText_UnknownDocument_Returns404()
    {
        var response = await _client.GetAsync($"/api/documents/{Guid.NewGuid()}/text");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<DocumentDto> UploadAsync(string fileName, byte[] pdf, HttpClient? client = null)
    {
        var file = new ByteArrayContent(pdf);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var response = await (client ?? _client).PostAsync("/api/documents", new MultipartFormDataContent { { file, "file", fileName } });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DocumentDto>())!;
    }

    [Fact]
    public async Task GetById_Unknown_Returns404()
    {
        var response = await _client.GetAsync($"/api/documents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
