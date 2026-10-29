using System.Text;
using System.Text.RegularExpressions;

namespace DocuAsk.Core;

/// <summary>
/// Splits text into chunks on paragraph boundaries, keeping each chunk under a size limit.
/// Each new chunk starts with the end of the previous one, so an idea that crosses a
/// boundary can still be found by a search.
/// </summary>
public sealed partial class TextChunker
{
    public TextChunker(int maxChars = 800, int overlapChars = 100)
    {
        if (maxChars < 100)
            throw new ArgumentOutOfRangeException(nameof(maxChars), "Chunks must allow at least 100 characters.");
        if (overlapChars < 0 || overlapChars > maxChars / 2)
            throw new ArgumentOutOfRangeException(nameof(overlapChars), "Overlap must be between 0 and half the chunk size.");

        MaxChars = maxChars;
        OverlapChars = overlapChars;
    }

    public int MaxChars { get; }

    public int OverlapChars { get; }

    public IReadOnlyList<DocumentChunk> Chunk(string source, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var chunks = new List<DocumentChunk>();
        if (string.IsNullOrWhiteSpace(text))
            return chunks;

        // Room left for new text once the overlap and a paragraph break are in the chunk.
        var pieceLimit = MaxChars - OverlapChars - 2;
        var pieces = SplitIntoParagraphs(text).SelectMany(p => SplitLongText(p, pieceLimit));

        var current = new StringBuilder();
        foreach (var piece in pieces)
        {
            if (current.Length > 0 && current.Length + 2 + piece.Length > MaxChars)
            {
                var finished = current.ToString();
                chunks.Add(Create(source, chunks.Count, finished));

                current.Clear();
                current.Append(TakeTail(finished, OverlapChars));
            }

            if (current.Length > 0)
                current.Append("\n\n");
            current.Append(piece);
        }

        if (current.Length > 0)
            chunks.Add(Create(source, chunks.Count, current.ToString()));

        return chunks;
    }

    private static DocumentChunk Create(string source, int index, string text) =>
        new($"{source}#{index}", source, index, text.Trim());

    private static IEnumerable<string> SplitIntoParagraphs(string text) =>
        ParagraphBreak().Split(text.Replace("\r\n", "\n"))
            .Select(p => p.Trim())
            .Where(p => p.Length > 0);

    /// <summary>Breaks a paragraph that is too long into pieces at word boundaries.</summary>
    internal static IEnumerable<string> SplitLongText(string text, int limit)
    {
        if (text.Length <= limit)
        {
            yield return text;
            yield break;
        }

        var current = new StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            // A single word longer than the limit (a long URL, say) is cut into fixed-size pieces.
            if (word.Length > limit)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }

                for (var i = 0; i < word.Length; i += limit)
                    yield return word.Substring(i, Math.Min(limit, word.Length - i));
                continue;
            }

            if (current.Length > 0 && current.Length + 1 + word.Length > limit)
            {
                yield return current.ToString();
                current.Clear();
            }

            if (current.Length > 0)
                current.Append(' ');
            current.Append(word);
        }

        if (current.Length > 0)
            yield return current.ToString();
    }

    /// <summary>Returns up to <paramref name="maxChars"/> characters from the end, starting at a word boundary.</summary>
    internal static string TakeTail(string text, int maxChars)
    {
        if (maxChars == 0 || text.Length == 0)
            return string.Empty;
        if (text.Length <= maxChars)
            return text;

        var tail = text[^maxChars..];
        var firstBreak = tail.IndexOfAny(new[] { ' ', '\n' });
        return firstBreak >= 0 && firstBreak < tail.Length - 1
            ? tail[(firstBreak + 1)..].TrimStart()
            : tail;
    }

    [GeneratedRegex(@"\n\s*\n")]
    private static partial Regex ParagraphBreak();
}
