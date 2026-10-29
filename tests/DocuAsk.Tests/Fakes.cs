using DocuAsk.Core;

namespace DocuAsk.Tests;

/// <summary>
/// A deterministic embedder for tests: each dimension counts one keyword,
/// so texts that share keywords get similar vectors. No network calls.
/// </summary>
internal sealed class KeywordEmbedder : IEmbedder
{
    private static readonly string[] Vocabulary = ["baggage", "refund", "change", "insurance", "flight", "fee"];

    public int Calls { get; private set; }

    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        Calls++;
        IReadOnlyList<float[]> vectors = texts.Select(Embed).ToList();
        return Task.FromResult(vectors);
    }

    private static float[] Embed(string text)
    {
        var words = text.ToLowerInvariant()
            .Split(new[] { ' ', '\n', '.', ',', '?', '!', ':' }, StringSplitOptions.RemoveEmptyEntries);

        return Vocabulary
            .Select(term => (float)words.Count(w => w.StartsWith(term, StringComparison.Ordinal)))
            .ToArray();
    }
}

/// <summary>Records what it was asked and returns a fixed answer.</summary>
internal sealed class RecordingGenerator(string answer = "Checked bags up to 23 kg are included [1].") : IAnswerGenerator
{
    public string? LastSystemPrompt { get; private set; }

    public string? LastUserPrompt { get; private set; }

    public int Calls { get; private set; }

    public Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        Calls++;
        LastSystemPrompt = systemPrompt;
        LastUserPrompt = userPrompt;
        return Task.FromResult(answer);
    }
}
