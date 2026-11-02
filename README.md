# DocuAsk: RAG over your documents in .NET

Ask questions about a folder of Markdown or text files and get short answers that cite where they came from. Built in C# with [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai), so the model provider can be swapped without touching the core logic.

```
> How much checked baggage is included in economy?

Every economy fare includes one checked bag up to 23 kg [1].

Sources:
  [1] baggage-policy.md (similarity 0.61)
```

*Example output. Answers depend on the model and your documents.*

## How it works

1. **Load:** reads every `.md` and `.txt` file in a folder.
2. **Chunk:** splits each document on paragraph boundaries into chunks of up to 800 characters. Each chunk starts with the last 100 characters of the previous one, so ideas that cross a boundary aren't lost.
3. **Embed:** turns each chunk into a vector with an embedding model, in batches.
4. **Index:** stores the vectors in memory and caches them to a JSON file. The cache has a fingerprint of the documents, chunk settings and embedding model, so it is only rebuilt when one of them changes.
5. **Retrieve:** embeds the question and finds the most similar chunks using cosine similarity. Weak matches below a minimum score are dropped.
6. **Answer:** sends the question and the top chunks to the chat model, with instructions to answer only from those passages, cite them, and ignore any instructions written inside them.

If nothing relevant is found, DocuAsk says so without calling the chat model, so it doesn't guess and doesn't spend tokens.

## Project structure

| Project | What it contains |
| --- | --- |
| `src/DocuAsk.Core` | Chunking, vector index, retrieval and prompt building. No AI SDK dependencies, so it is fully unit tested. |
| `src/DocuAsk.Cli` | Console app. Connects the core to OpenAI or Ollama through `IChatClient` and `IEmbeddingGenerator`. |
| `src/DocuAsk.Web` | Browser UI: upload documents and ask questions. ASP.NET Core minimal API, a single static page, PDF text extraction with PdfPig |
| `tests/DocuAsk.Tests` | xUnit tests using a fake keyword embedder, so they run offline with no API key. |
| `docs/` | Sample policy documents for a fictional airline. |

## Run it

Requirements: .NET 10 SDK, plus either [Ollama](https://ollama.com) (free, runs locally) or an OpenAI API key.

### With Ollama (free)

```bash
ollama pull llama3.2
ollama pull nomic-embed-text
dotnet run --project src/DocuAsk.Cli -- docs
```

### With OpenAI

```bash
# Windows PowerShell
$env:OPENAI_API_KEY = "your-key"

# macOS / Linux
export OPENAI_API_KEY="your-key"

dotnet run --project src/DocuAsk.Cli -- docs
```

When `OPENAI_API_KEY` is set, OpenAI is used; otherwise Ollama. Set `DOCUASK_PROVIDER` to `ollama` or `openai` to choose explicitly, and `DOCUASK_CHAT_MODEL` or `DOCUASK_EMBEDDING_MODEL` to change models.

### Web page

**Document Ask**, the same RAG engine in the browser. Upload your own PDF, Markdown or text files (or use the samples), ask a question, and see the answer with the source passages and their similarity scores.

```bash
dotnet run --project src/DocuAsk.Web
```

It opens `http://localhost:5090`. Provider settings are the same as the CLI.

## Run the tests

```bash
dotnet test
```

The tests cover chunk sizes and overlap, cosine similarity, search ranking and score filtering, saving and reloading the index, batching, citations in the prompt, and skipping the model when nothing relevant is found.

## Using a different model provider

The core library only knows about two small interfaces, `IEmbedder` and `IAnswerGenerator`. The CLI implements them on top of Microsoft.Extensions.AI, so any provider with an `IChatClient` and `IEmbeddingGenerator` implementation can be plugged in. OpenAI and Ollama are supported out of the box; Azure OpenAI would need only a few lines in `Program.cs`.

## Possible next steps

- Replace the in-memory index with a vector database such as PostgreSQL with pgvector
- Add PDF and Word document loading
- Add an evaluation set of questions with expected sources to measure retrieval quality
