namespace DocuAsk.Core;

/// <summary>
/// Turns text into vectors. The CLI implements this with Microsoft.Extensions.AI,
/// so any embedding provider behind IEmbeddingGenerator can be plugged in.
/// </summary>
public interface IEmbedder
{
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);
}

/// <summary>
/// Produces an answer from a system prompt and a user prompt.
/// The CLI implements this with IChatClient.
/// </summary>
public interface IAnswerGenerator
{
    Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);
}
