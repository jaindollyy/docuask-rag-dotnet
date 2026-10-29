using DocuAsk.Core;

namespace DocuAsk.Tests;

public class TextChunkerTests
{
    [Fact]
    public void Short_text_becomes_one_chunk()
    {
        var chunker = new TextChunker(maxChars: 200, overlapChars: 20);

        var chunks = chunker.Chunk("policy.md", "Checked bags up to 23 kg are included.");

        var chunk = Assert.Single(chunks);
        Assert.Equal("policy.md#0", chunk.Id);
        Assert.Equal("policy.md", chunk.Source);
        Assert.Equal("Checked bags up to 23 kg are included.", chunk.Text);
    }

    [Fact]
    public void Empty_text_produces_no_chunks()
    {
        var chunker = new TextChunker();

        Assert.Empty(chunker.Chunk("empty.md", "   \n\n  "));
    }

    [Fact]
    public void Long_text_is_split_and_every_chunk_stays_within_the_limit()
    {
        var chunker = new TextChunker(maxChars: 200, overlapChars: 40);
        var paragraphs = Enumerable.Range(1, 12)
            .Select(i => $"Paragraph {i} explains one rule of the travel policy in a sentence or two of plain words.");
        var text = string.Join("\n\n", paragraphs);

        var chunks = chunker.Chunk("policy.md", text);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.Text.Length <= 200, $"Chunk was {c.Text.Length} characters."));
        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(c => c.Index));
    }

    [Fact]
    public void Each_chunk_starts_with_the_end_of_the_previous_one()
    {
        var chunker = new TextChunker(maxChars: 200, overlapChars: 50);
        var text = string.Join("\n\n", Enumerable.Range(1, 8)
            .Select(i => $"Rule {i}: passengers must check in at least sixty minutes before departure time."));

        var chunks = chunker.Chunk("rules.md", text);

        for (var i = 1; i < chunks.Count; i++)
        {
            var firstWords = string.Join(' ', chunks[i].Text.Split(' ').Take(3));
            Assert.Contains(firstWords, chunks[i - 1].Text);
        }
    }

    [Fact]
    public void A_paragraph_longer_than_the_limit_is_split_at_word_boundaries()
    {
        var chunker = new TextChunker(maxChars: 120, overlapChars: 0);
        var text = string.Join(' ', Enumerable.Repeat("baggage", 60));

        var chunks = chunker.Chunk("long.md", text);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.Text.Length <= 120));
        Assert.All(chunks, c => Assert.DoesNotContain("baggag ", c.Text + " "));
    }

    [Theory]
    [InlineData(50, 10)]
    [InlineData(200, -1)]
    [InlineData(200, 150)]
    public void Invalid_settings_are_rejected(int maxChars, int overlap)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextChunker(maxChars, overlap));
    }

    [Fact]
    public void TakeTail_starts_at_a_word_boundary()
    {
        var tail = TextChunker.TakeTail("the quick brown fox jumps", 12);

        Assert.Equal("fox jumps", tail);
    }
}
