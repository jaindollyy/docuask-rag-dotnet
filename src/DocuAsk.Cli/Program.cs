using DocuAsk.Cli;
using DocuAsk.Core;
using Microsoft.Extensions.AI;
using OpenAI;

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("Set the OPENAI_API_KEY environment variable, then run again.");
    return 1;
}

var chatModel = Environment.GetEnvironmentVariable("DOCUASK_CHAT_MODEL") ?? "gpt-4o-mini";
var embeddingModel = Environment.GetEnvironmentVariable("DOCUASK_EMBEDDING_MODEL") ?? "text-embedding-3-small";
var docsPath = args.Length > 0 ? args[0] : "docs";

if (!Directory.Exists(docsPath))
{
    Console.Error.WriteLine($"Folder not found: {docsPath}");
    Console.Error.WriteLine("Usage: docuask [folder-with-md-or-txt-files]");
    return 1;
}

var openAi = new OpenAIClient(apiKey);
IChatClient chatClient = openAi.GetChatClient(chatModel).AsIChatClient();
IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator =
    openAi.GetEmbeddingClient(embeddingModel).AsIEmbeddingGenerator();

var chunker = new TextChunker(maxChars: 800, overlapChars: 100);
var index = new InMemoryVectorIndex();
var rag = new RagService(
    new ExtensionsAiEmbedder(embeddingGenerator),
    new ExtensionsAiAnswerGenerator(chatClient),
    chunker,
    index,
    new RagOptions { TopK = 4, MinScore = 0.25f });

var documents = DocumentLoader.LoadDirectory(docsPath);
if (documents.Count == 0)
{
    Console.Error.WriteLine($"No .md or .txt files found in {docsPath}.");
    return 1;
}

// Embeddings cost money, so the index is cached and only rebuilt when the documents change.
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
