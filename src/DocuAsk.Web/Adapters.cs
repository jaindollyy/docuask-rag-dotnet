using DocuAsk.Core;
using Microsoft.Extensions.AI;

namespace DocuAsk.Web;

/// <summary>Connects the core library to any embedding provider exposed through Microsoft.Extensions.AI.</summary>
internal sealed class ExtensionsAiEmbedder(IEmbeddingGenerator<string, Embedding<float>> generator) : IEmbedder
{
    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        var embeddings = await generator.GenerateAsync(texts, cancellationToken: cancellationToken);
        return embeddings.Select(e => e.Vector.ToArray()).ToList();
    }
}

/// <summary>Connects the core library to any chat model exposed through IChatClient.</summary>
internal sealed class ExtensionsAiAnswerGenerator(IChatClient chatClient) : IAnswerGenerator
{
    public async Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, userPrompt),
        };

        // A low temperature keeps answers close to the source passages.
        var response = await chatClient.GetResponseAsync(messages, new ChatOptions { Temperature = 0.1f }, cancellationToken);
        return response.Text;
    }
}

internal static class DocsFolder
{
    /// <summary>Looks for a "docs" folder with Markdown files, starting here and walking up.</summary>
    public static string? Find()
    {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "docs");
            if (Directory.Exists(candidate) && Directory.EnumerateFiles(candidate, "*.md").Any())
                return candidate;
        }

        return null;
    }
}

/// <summary>
/// The documents that can be searched: the samples plus anything uploaded.
/// One request at a time touches the index, so uploads and questions can't clash.
/// </summary>
public sealed class DocumentLibrary(RagService rag)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<string> _names = [];

    public IReadOnlyList<string> Names
    {
        get { lock (_names) return _names.ToList(); }
    }

    public bool Contains(string name)
    {
        lock (_names) return _names.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    public void Register(IEnumerable<string> names)
    {
        lock (_names) _names.AddRange(names);
    }

    public async Task<int> AddAsync(IReadOnlyList<SourceDocument> documents, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var chunks = await rag.IndexAsync(documents, cancellationToken);
            Register(documents.Select(d => d.Name));
            return chunks;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RagAnswer> AskAsync(string question, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await rag.AskAsync(question, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}

public sealed record AskRequest(string? Question, string? Collection);

public sealed record Libraries(DocumentLibrary Samples, DocumentLibrary Uploads);

public sealed record SourceResult(string Document, float Similarity, string Excerpt);

public sealed record AskResponse(string Answer, IReadOnlyList<SourceResult> Sources);
