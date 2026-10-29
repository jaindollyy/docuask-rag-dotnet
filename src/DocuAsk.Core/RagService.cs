namespace DocuAsk.Core;

/// <summary>
/// Retrieval-augmented generation: find the chunks most relevant to a question,
/// then ask the model to answer from those chunks only.
/// </summary>
public sealed class RagService
{
    public const string NoAnswerMessage = "I couldn't find anything in the documents about that.";

    private readonly IEmbedder _embedder;
    private readonly IAnswerGenerator _generator;
    private readonly TextChunker _chunker;
    private readonly InMemoryVectorIndex _index;
    private readonly RagOptions _options;

    public RagService(
        IEmbedder embedder,
        IAnswerGenerator generator,
        TextChunker chunker,
        InMemoryVectorIndex index,
        RagOptions? options = null)
    {
        _embedder = embedder ?? throw new ArgumentNullException(nameof(embedder));
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _chunker = chunker ?? throw new ArgumentNullException(nameof(chunker));
        _index = index ?? throw new ArgumentNullException(nameof(index));
        _options = options ?? new RagOptions();

        ArgumentOutOfRangeException.ThrowIfLessThan(_options.TopK, 1, nameof(options));
        ArgumentOutOfRangeException.ThrowIfLessThan(_options.EmbeddingBatchSize, 1, nameof(options));
    }

    /// <summary>Chunks and embeds the documents and adds them to the index. Returns the number of chunks added.</summary>
    public async Task<int> IndexAsync(IEnumerable<SourceDocument> documents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var chunks = documents.SelectMany(d => _chunker.Chunk(d.Name, d.Text)).ToList();

        for (var start = 0; start < chunks.Count; start += _options.EmbeddingBatchSize)
        {
            var batch = chunks.Skip(start).Take(_options.EmbeddingBatchSize).ToList();
            var vectors = await _embedder.EmbedAsync(batch.Select(c => c.Text).ToList(), cancellationToken);

            if (vectors.Count != batch.Count)
                throw new InvalidOperationException(
                    $"The embedding service returned {vectors.Count} vectors for {batch.Count} chunks.");

            for (var i = 0; i < batch.Count; i++)
                _index.Add(batch[i], vectors[i]);
        }

        return chunks.Count;
    }

    public async Task<RagAnswer> AskAsync(string question, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        if (_index.Count == 0)
            throw new InvalidOperationException("No documents have been indexed yet.");

        var vectors = await _embedder.EmbedAsync([question], cancellationToken);
        if (vectors.Count != 1)
            throw new InvalidOperationException("The embedding service didn't return a vector for the question.");

        var matches = _index.Search(vectors[0], _options.TopK, _options.MinScore);

        // Nothing relevant: answer directly instead of letting the model guess.
        if (matches.Count == 0)
            return new RagAnswer(NoAnswerMessage, []);

        var answer = await _generator.GenerateAsync(
            PromptBuilder.SystemPrompt,
            PromptBuilder.BuildUserPrompt(question, matches),
            cancellationToken);

        return new RagAnswer(answer.Trim(), matches);
    }
}
