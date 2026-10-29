namespace DocuAsk.Core;

public static class VectorMath
{
    /// <summary>
    /// Cosine similarity between two vectors: 1 means the same direction, 0 unrelated, -1 opposite.
    /// Returns 0 when either vector is all zeros.
    /// </summary>
    public static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException($"Vectors must be the same length ({a.Length} vs {b.Length}).");

        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA == 0 || normB == 0)
            return 0f;

        return (float)(dot / (Math.Sqrt(normA) * Math.Sqrt(normB)));
    }
}
