using DocuAsk.Cli;
using DocuAsk.Core;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;

// Provider: "ollama" (free, runs locally) or "openai". Defaults to OpenAI when a key is set.
var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
var provider = (Environment.GetEnvironmentVariable("DOCUASK_PROVIDER")
    ?? (string.IsNullOrWhiteSpace(apiKey) ? "ollama" : "openai")).ToLowerInvariant();

IChatClient chatClient;
IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator;
string embeddingModel;
float minScore;

if (provider == "openai")
{
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        Console.Error.WriteLine("Set the OPENAI_API_KEY environment variable, or set DOCUASK_PROVIDER=ollama.");
        return 1;
    }

    var chatModel = Environment.GetEnvironmentVariable("DOCUASK_CHAT_MODEL") ?? "gpt-4o-mini";
    embeddingModel = Environment.GetEnvironmentVariable("DOCUASK_EMBEDDING_MODEL") ?? "text-embedding-3-small";
    minScore = 0.25f;

    var openAi = new OpenAIClient(apiKey);
    chatClient = openAi.GetChatClient(chatModel).AsIChatClient();
    embeddingGenerator = openAi.GetEmbeddingClient(embeddingModel).AsIEmbeddingGenerator();
}
else if (provider == "ollama")
{
    var endpoint = new Uri(Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT") ?? "http://localhost:11434");
    var chatModel = Environment.GetEnvironmentVariable("DOCUASK_CHAT_MODEL") ?? "llama3.2";
    embeddingModel = Environment.GetEnvironmentVariable("DOCUASK_EMBEDDING_MODEL") ?? "nomic-embed-text";
    // Local embedding models score unrelated text higher, so they need a stricter threshold.
    minScore = 0.5f;

    chatClient = new OllamaApiClient(endpoint, chatModel);
    embeddingGenerator = new OllamaApiClient(endpoint, embeddingModel);
}
else
{
    Console.Error.WriteLine($"Unknown provider '{provider}'. Use ollama or openai.");
    return 1;
}

Console.WriteLine($"Using {provider}.");

var docsPath = args.Length > 0 ? args[0] : "docs";

if (!Directory.Exists(docsPath))
{
    Console.Error.WriteLine($"Folder not found: {docsPath}");
    Console.Error.WriteLine("Usage: docuask [folder-with-md-or-txt-files]");
    return 1;
}

var chunker = new TextChunker(maxChars: 800, overlapChars: 100);
var index = new InMemoryVectorIndex();
var rag = new RagService(
    new ExtensionsAiEmbedder(embeddingGenerator),
    new ExtensionsAiAnswerGenerator(chatClient),
    chunker,
    index,
    new RagOptions { TopK = 4, MinScore = minScore });

var documents = DocumentLoader.LoadDirectory(docsPath);
if (documents.Count == 0)
{
    Console.Error.WriteLine($"No .md or .txt files found in {docsPath}.");
    return 1;
}

// Embeddings take time (and cost money on paid providers), so the index is cached and only rebuilt when the documents change.
var cachePath = Path.Combine(docsPath, ".docuask-index.json");
var fingerprint = IndexFingerprint.Compute(documents, chunker, embeddingModel);

if (await index.TryLoadAsync(cachePath, fingerprint))
{
    Console.WriteLine($"Loaded {index.Count} chunks from {documents.Count} documents (cached).");
}
else
{
    Console.WriteLine($"Indexing {documents.Count} documents...");
    var chunkCount = await rag.IndexAsync(documents);
    await index.SaveAsync(cachePath, fingerprint);
    Console.WriteLine($"Indexed {chunkCount} chunks.");
}

Console.WriteLine("Ask a question about your documents. Press Enter on an empty line to quit.");

while (true)
{
    Console.Write("\n> ");
    var question = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(question))
        break;

    try
    {
        var answer = await rag.AskAsync(question);
        Console.WriteLine();
        Console.WriteLine(answer.Answer);

        if (answer.Sources.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Sources:");
            for (var i = 0; i < answer.Sources.Count; i++)
            {
                var source = answer.Sources[i];
                Console.WriteLine($"  [{i + 1}] {source.Chunk.Source} (similarity {source.Score:F2})");
            }
        }
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        Console.Error.WriteLine($"Couldn't answer that: {ex.Message}");
    }
}

return 0;
