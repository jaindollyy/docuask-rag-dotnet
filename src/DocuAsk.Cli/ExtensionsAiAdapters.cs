using DocuAsk.Core;
using Microsoft.Extensions.AI;

namespace DocuAsk.Cli;

/// <summary>Connects the core library to any embedding provider exposed through Microsoft.Extensions.AI.</summary>
internal sealed class ExtensionsAiEmbedder(IEmbeddingGenerator<string, Embedding<float>> generator) : IEmbedder
{
    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        var embeddings = await generator.GenerateAsync(texts, cancellationToken: cancellationToken);
        return embeddings.Select(e => e.Vector.ToArray()).ToList();
    }
}

/// <summary>Connects the core library to any chat model exposed through IChatClient.</summary>
internal sealed class ExtensionsAiAnswerGenerator(IChatClient chatClient) : IAnswerGenerator
{
    public async Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, userPrompt),
        };

        // A low temperature keeps answers close to the source passages.
        var response = await chatClient.GetResponseAsync(messages, new ChatOptions { Temperature = 0.1f }, cancellationToken);
        return response.Text;
    }
}
