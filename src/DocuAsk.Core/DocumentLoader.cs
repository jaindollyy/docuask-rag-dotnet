using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DocuAsk.Core;

public static class DocumentLoader
{
    private static readonly string[] SupportedExtensions = [".md", ".txt"];

    /// <summary>Loads every Markdown and text file in a folder and its subfolders.</summary>
    public static IReadOnlyList<SourceDocument> LoadDirectory(string path)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Folder not found: {path}");

        return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .Select(f => new SourceDocument(
                Path.GetRelativePath(path, f).Replace('\\', '/'),
                File.ReadAllText(f)))
            .Where(d => !string.IsNullOrWhiteSpace(d.Text))
            .ToList();
    }
}

public static class IndexFingerprint
{
    /// <summary>
    /// A hash of everything that affects the index: the documents, the chunk settings and the
    /// embedding model. If any of them change, the saved index is rebuilt.
    /// </summary>
    public static string Compute(IEnumerable<SourceDocument> documents, TextChunker chunker, string embeddingModel)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        void Add(string value)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(value));
            hash.AppendData(new byte[] { 0 });
        }

        Add(embeddingModel);
        Add(chunker.MaxChars.ToString(CultureInfo.InvariantCulture));
        Add(chunker.OverlapChars.ToString(CultureInfo.InvariantCulture));

        foreach (var document in documents.OrderBy(d => d.Name, StringComparer.Ordinal))
        {
            Add(document.Name);
            Add(document.Text);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
