using System.Text.Json;

namespace DocuAsk.Core;

/// <summary>
/// A small vector store held in memory. It can be saved to a JSON file, so documents
/// are only embedded again when they change.
/// </summary>
public sealed class InMemoryVectorIndex
{
    private readonly List<IndexEntry> _entries = [];
    private int _dimensions;

    public int Count => _entries.Count;

    public void Add(DocumentChunk chunk, float[] vector)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ArgumentNullException.ThrowIfNull(vector);

        if (vector.Length == 0)
            throw new ArgumentException("A vector must have at least one dimension.", nameof(vector));

        if (_dimensions == 0)
            _dimensions = vector.Length;
        else if (vector.Length != _dimensions)
            throw new ArgumentException(
                $"Expected a vector with {_dimensions} dimensions but got {vector.Length}. " +
                "All vectors in an index must come from the same embedding model.", nameof(vector));

        _entries.Add(new IndexEntry(chunk, vector));
    }

    public void Clear()
    {
        _entries.Clear();
        _dimensions = 0;
    }

    /// <summary>Returns the chunks most similar to the query, best first.</summary>
    public IReadOnlyList<ScoredChunk> Search(float[] query, int top, float minScore = 0f)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(top, 1);

        if (_entries.Count == 0)
            return [];

        return _entries
            .Select(e => new ScoredChunk(e.Chunk, VectorMath.CosineSimilarity(query, e.Vector)))
            .Where(s => s.Score >= minScore)
            .OrderByDescending(s => s.Score)
            .Take(top)
            .ToList();
    }

    public async Task SaveAsync(string path, string fingerprint, CancellationToken cancellationToken = default)
    {
        var file = new IndexFile(fingerprint, _entries);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, file, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Loads a saved index if it exists and was built from the same documents and settings.
    /// Returns false when it has to be rebuilt.
    /// </summary>
    public async Task<bool> TryLoadAsync(string path, string fingerprint, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            return false;

        IndexFile? file;
        try
        {
            await using var stream = File.OpenRead(path);
            file = await JsonSerializer.DeserializeAsync<IndexFile>(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return false;
        }

        if (file is null || file.Fingerprint != fingerprint || file.Entries is null)
            return false;

        Clear();
        foreach (var entry in file.Entries)
            Add(entry.Chunk, entry.Vector);
        return true;
    }

    internal sealed record IndexEntry(DocumentChunk Chunk, float[] Vector);

    internal sealed record IndexFile(string Fingerprint, List<IndexEntry> Entries);
}
