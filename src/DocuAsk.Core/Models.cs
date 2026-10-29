namespace DocuAsk.Core;

/// <summary>A document loaded from disk, before it is split into chunks.</summary>
public sealed record SourceDocument(string Name, string Text);

/// <summary>A piece of a document small enough to embed and pass to the model.</summary>
public sealed record DocumentChunk(string Id, string Source, int Index, string Text);

/// <summary>A chunk returned by a search, with its similarity to the question.</summary>
public sealed record ScoredChunk(DocumentChunk Chunk, float Score);

/// <summary>The model's answer and the chunks it was given as context.</summary>
public sealed record RagAnswer(string Answer, IReadOnlyList<ScoredChunk> Sources);

/// <summary>Settings that control retrieval.</summary>
public sealed class RagOptions
{
    /// <summary>How many chunks to pass to the model as context.</summary>
    public int TopK { get; init; } = 4;

    /// <summary>Chunks less similar than this are ignored, so weak matches don't reach the prompt.</summary>
    public float MinScore { get; init; } = 0.25f;

    /// <summary>How many chunks to send to the embedding service in one request.</summary>
    public int EmbeddingBatchSize { get; init; } = 32;
}
