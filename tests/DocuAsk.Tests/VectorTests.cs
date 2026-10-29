using DocuAsk.Core;

namespace DocuAsk.Tests;

public class VectorMathTests
{
    [Fact]
    public void Identical_vectors_have_similarity_one() =>
        Assert.Equal(1f, VectorMath.CosineSimilarity([1f, 2f, 3f], [1f, 2f, 3f]), 5);

    [Fact]
    public void Unrelated_vectors_have_similarity_zero() =>
        Assert.Equal(0f, VectorMath.CosineSimilarity([1f, 0f], [0f, 1f]), 5);

    [Fact]
    public void Opposite_vectors_have_similarity_minus_one() =>
        Assert.Equal(-1f, VectorMath.CosineSimilarity([1f, 1f], [-1f, -1f]), 5);

    [Fact]
    public void A_zero_vector_has_similarity_zero() =>
        Assert.Equal(0f, VectorMath.CosineSimilarity([0f, 0f], [1f, 1f]));

    [Fact]
    public void Vectors_of_different_lengths_are_rejected() =>
        Assert.Throws<ArgumentException>(() => VectorMath.CosineSimilarity([1f, 2f], [1f, 2f, 3f]));
}

public class InMemoryVectorIndexTests
{
    private static DocumentChunk Chunk(string id) => new(id, "doc.md", 0, id);

    [Fact]
    public void Search_returns_the_closest_chunks_first()
    {
        var index = new InMemoryVectorIndex();
        index.Add(Chunk("far"), [0f, 1f]);
        index.Add(Chunk("close"), [1f, 0.1f]);
        index.Add(Chunk("middle"), [1f, 1f]);

        var results = index.Search([1f, 0f], top: 2);

        Assert.Equal(new[] { "close", "middle" }, results.Select(r => r.Chunk.Id));
    }

    [Fact]
    public void Search_drops_chunks_below_the_minimum_score()
    {
        var index = new InMemoryVectorIndex();
        index.Add(Chunk("relevant"), [1f, 0f]);
        index.Add(Chunk("unrelated"), [0f, 1f]);

        var results = index.Search([1f, 0f], top: 5, minScore: 0.5f);

        Assert.Equal("relevant", Assert.Single(results).Chunk.Id);
    }

    [Fact]
    public void Vectors_from_different_models_cannot_be_mixed()
    {
        var index = new InMemoryVectorIndex();
        index.Add(Chunk("a"), [1f, 0f]);

        Assert.Throws<ArgumentException>(() => index.Add(Chunk("b"), [1f, 0f, 0f]));
    }

    [Fact]
    public async Task A_saved_index_loads_back_with_the_same_fingerprint()
    {
        var path = Path.Combine(Path.GetTempPath(), $"docuask-{Guid.NewGuid():N}.json");
        try
        {
            var original = new InMemoryVectorIndex();
            original.Add(Chunk("a"), [1f, 0f]);
            original.Add(Chunk("b"), [0f, 1f]);
            await original.SaveAsync(path, "fingerprint-1");

            var loaded = new InMemoryVectorIndex();
            Assert.True(await loaded.TryLoadAsync(path, "fingerprint-1"));
            Assert.Equal(2, loaded.Count);
            Assert.Equal("a", loaded.Search([1f, 0f], top: 1)[0].Chunk.Id);

            var stale = new InMemoryVectorIndex();
            Assert.False(await stale.TryLoadAsync(path, "fingerprint-2"));
            Assert.Equal(0, stale.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void The_fingerprint_changes_when_a_document_changes()
    {
        var chunker = new TextChunker();
        var before = IndexFingerprint.Compute([new SourceDocument("a.md", "Bags up to 23 kg.")], chunker, "model-1");
        var after = IndexFingerprint.Compute([new SourceDocument("a.md", "Bags up to 30 kg.")], chunker, "model-1");
        var otherModel = IndexFingerprint.Compute([new SourceDocument("a.md", "Bags up to 23 kg.")], chunker, "model-2");

        Assert.NotEqual(before, after);
        Assert.NotEqual(before, otherModel);
    }
}
