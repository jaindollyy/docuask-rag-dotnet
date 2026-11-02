using DocuAsk.Core;
using DocuAsk.Web;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;
using UglyToad.PdfPig;

var builder = WebApplication.CreateBuilder(args);

// Provider: "ollama" (free, runs locally) or "openai". Defaults to OpenAI when a key is set.
var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
var provider = (Environment.GetEnvironmentVariable("DOCUASK_PROVIDER")
    ?? (string.IsNullOrWhiteSpace(apiKey) ? "ollama" : "openai")).ToLowerInvariant();

IChatClient chatClient;
IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator;
string embeddingModel;
float minScore;

switch (provider)
{
    case "openai":
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Set OPENAI_API_KEY, or set DOCUASK_PROVIDER=ollama.");

        embeddingModel = Environment.GetEnvironmentVariable("DOCUASK_EMBEDDING_MODEL") ?? "text-embedding-3-small";
        var openAi = new OpenAIClient(apiKey);
        chatClient = openAi.GetChatClient(Environment.GetEnvironmentVariable("DOCUASK_CHAT_MODEL") ?? "gpt-4o-mini").AsIChatClient();
        embeddingGenerator = openAi.GetEmbeddingClient(embeddingModel).AsIEmbeddingGenerator();
        minScore = 0.25f;
        break;
    }
    case "ollama":
    {
        var endpoint = new Uri(Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT") ?? "http://localhost:11434");
        embeddingModel = Environment.GetEnvironmentVariable("DOCUASK_EMBEDDING_MODEL") ?? "nomic-embed-text";
        chatClient = new OllamaApiClient(endpoint, Environment.GetEnvironmentVariable("DOCUASK_CHAT_MODEL") ?? "llama3.2");
        embeddingGenerator = new OllamaApiClient(endpoint, embeddingModel);
        // Local embedding models score unrelated text higher, so they need a stricter threshold.
        minScore = 0.5f;
        break;
    }
    default:
        throw new InvalidOperationException($"Unknown provider '{provider}'. Use ollama or openai.");
}

// Load and index the documents once at startup (cached, like the CLI).
var docsPath = builder.Configuration["DocsPath"] ?? DocsFolder.Find()
    ?? throw new InvalidOperationException("Couldn't find a docs folder. Set DocsPath in configuration.");

var documents = DocumentLoader.LoadDirectory(docsPath);
var chunker = new TextChunker(maxChars: 800, overlapChars: 100);
var index = new InMemoryVectorIndex();
var rag = new RagService(
    new ExtensionsAiEmbedder(embeddingGenerator),
    new ExtensionsAiAnswerGenerator(chatClient),
    chunker,
    index,
    new RagOptions { TopK = 4, MinScore = minScore });

var cachePath = Path.Combine(docsPath, ".docuask-index.json");
var fingerprint = IndexFingerprint.Compute(documents, chunker, embeddingModel);
if (!await index.TryLoadAsync(cachePath, fingerprint))
{
    await rag.IndexAsync(documents);
    await index.SaveAsync(cachePath, fingerprint);
}

var library = new DocumentLibrary(rag);
library.Register(documents.Select(d => d.Name));
builder.Services.AddSingleton(library);

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

const long MaxFileBytes = 5 * 1024 * 1024;
const int MaxDocuments = 25;
string[] allowedTypes = [".pdf", ".md", ".txt"];

app.MapGet("/api/info", (DocumentLibrary lib) => new
{
    provider,
    documents = lib.Names,
});

app.MapPost("/api/documents", async (HttpRequest request, DocumentLibrary lib, CancellationToken ct) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest(new { error = "Upload files as form data." });

    var form = await request.ReadFormAsync(ct);
    if (form.Files.Count == 0)
        return Results.BadRequest(new { error = "Choose at least one file." });

    var added = new List<SourceDocument>();
    var errors = new List<string>();

    foreach (var file in form.Files)
    {
        var name = Path.GetFileName(file.FileName);

        if (!allowedTypes.Contains(Path.GetExtension(name).ToLowerInvariant()))
            errors.Add($"{name}: only PDF, Markdown and text files are supported.");
        else if (file.Length > MaxFileBytes)
            errors.Add($"{name}: files must be 5 MB or smaller.");
        else if (lib.Contains(name) || added.Any(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            errors.Add($"{name}: a document with this name is already loaded.");
        else if (lib.Names.Count + added.Count >= MaxDocuments)
            errors.Add($"{name}: the demo holds up to {MaxDocuments} documents.");
        else
        {
            try
            {
                var text = await ReadTextAsync(file, ct);
                if (string.IsNullOrWhiteSpace(text))
                    errors.Add($"{name}: no text found. Scanned PDFs aren't supported.");
                else
                    added.Add(new SourceDocument(name, text));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"{name}: this file couldn't be read.");
            }
        }
    }

    var chunks = 0;
    if (added.Count > 0)
    {
        try
        {
            chunks = await lib.AddAsync(added, ct);
        }
        catch (HttpRequestException)
        {
            return Results.Problem("The AI service isn't available. Check that Ollama is running.", statusCode: 503);
        }
    }

    return Results.Ok(new
    {
        added = added.Select(d => d.Name).ToList(),
        chunks,
        errors,
        documents = lib.Names,
    });
});

app.MapPost("/api/ask", async (AskRequest request, DocumentLibrary lib, CancellationToken ct) =>
{
    var question = request.Question?.Trim();
    if (string.IsNullOrEmpty(question))
        return Results.BadRequest(new { error = "Enter a question." });
    if (question.Length > 500)
        return Results.BadRequest(new { error = "Questions must be 500 characters or fewer." });

    try
    {
        var answer = await lib.AskAsync(question, ct);
        var sources = answer.Sources
            .Select(s => new SourceResult(s.Chunk.Source, MathF.Round(s.Score, 2), Excerpt(s.Chunk.Text)))
            .ToList();
        return Results.Ok(new AskResponse(answer.Answer, sources));
    }
    catch (HttpRequestException)
    {
        return Results.Problem("The AI service isn't available. Check that Ollama is running.", statusCode: 503);
    }
});

app.Run();

static async Task<string> ReadTextAsync(IFormFile file, CancellationToken ct)
{
    await using var stream = file.OpenReadStream();

    if (Path.GetExtension(file.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        using var pdf = PdfDocument.Open(buffer.ToArray());
        return string.Join("\n\n", pdf.GetPages().Select(page => page.Text));
    }

    using var reader = new StreamReader(stream);
    return await reader.ReadToEndAsync(ct);
}

static string Excerpt(string text)
{
    var clean = string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Replace("#", "").Trim();
    return clean.Length <= 220 ? clean : clean[..220].TrimEnd() + "…";
}
