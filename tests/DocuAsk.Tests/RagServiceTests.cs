using DocuAsk.Core;

namespace DocuAsk.Tests;

public class RagServiceTests
{
    private static readonly SourceDocument[] Documents =
    [
        new("baggage.md", "Baggage allowance: one checked baggage item up to 23 kg is included in every fare."),
        new("changes.md", "Flight change rules: a change fee applies to economy fares. Refund requests take 10 business days."),
        new("insurance.md", "Travel insurance covers medical costs and cancelled trips. Insurance is optional."),
    ];

    private static (RagService Rag, KeywordEmbedder Embedder, RecordingGenerator Generator) Create(RagOptions? options = null)
    {
        var embedder = new KeywordEmbedder();
        var generator = new RecordingGenerator();
        var rag = new RagService(embedder, generator, new TextChunker(), new InMemoryVectorIndex(), options ?? new RagOptions { TopK = 2 });
        return (rag, embedder, generator);
    }

    [Fact]
    public async Task Indexing_returns_the_number_of_chunks_added()
    {
        var (rag, _, _) = Create();

        var count = await rag.IndexAsync(Documents);

        Assert.Equal(3, count);
    }

    [Fact]
    public async Task Indexing_sends_chunks_to_the_embedder_in_batches()
    {
        var (rag, embedder, _) = Create(new RagOptions { EmbeddingBatchSize = 2 });

        await rag.IndexAsync(Documents);

        Assert.Equal(2, embedder.Calls);
    }

    [Fact]
    public async Task The_most_relevant_document_is_used_as_context_and_cited()
    {
        var (rag, _, generator) = Create();
        await rag.IndexAsync(Documents);

        var answer = await rag.AskAsync("How much baggage can I check?");

        Assert.Equal("baggage.md", answer.Sources[0].Chunk.Source);
        Assert.Contains("[1] (source: baggage.md)", generator.LastUserPrompt);
        Assert.Contains("Question: How much baggage can I check?", generator.LastUserPrompt);
        Assert.Equal(PromptBuilder.SystemPrompt, generator.LastSystemPrompt);
    }

    [Fact]
    public async Task An_unrelated_question_is_answered_without_calling_the_model()
    {
        var (rag, _, generator) = Create();
        await rag.IndexAsync(Documents);

        var answer = await rag.AskAsync("What is the weather like in Paris?");

        Assert.Equal(RagService.NoAnswerMessage, answer.Answer);
        Assert.Empty(answer.Sources);
        Assert.Equal(0, generator.Calls);
    }

    [Fact]
    public async Task Asking_before_indexing_explains_what_is_missing()
    {
        var (rag, _, _) = Create();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => rag.AskAsync("Any question?"));

        Assert.Contains("No documents", error.Message);
    }

    [Fact]
    public async Task Only_the_top_k_chunks_reach_the_prompt()
    {
        var (rag, _, _) = Create(new RagOptions { TopK = 1 });
        await rag.IndexAsync(Documents);

        var answer = await rag.AskAsync("Is there a fee for a flight change or a refund?");

        Assert.Equal("changes.md", Assert.Single(answer.Sources).Chunk.Source);
    }
}
