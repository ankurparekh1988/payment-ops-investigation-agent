using System.Text;

using PaymentOps.Knowledge.Corpus;

namespace PaymentOps.Knowledge.Tests;

/// <summary>A source directory on disk holding only the files a test writes.</summary>
internal sealed class SourceTree : IDisposable
{
    public const string Body = """
        # Missing settlement batch

        ## Confirm the batch is missing

        Check the batches received for the settlement date.

        ### Partial files

        A second file can complete the batch.
        """;

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("knowledge-src-");

    public string Root => _root.FullName;

    public static string Source(
        string docId = "missing-settlement-batch",
        string title = "Missing settlement batch",
        string docType = "runbook",
        string? extraFrontMatter = null,
        string body = Body) =>
        $"---\ndocId: {docId}\ntitle: {title}\ndocType: {docType}\n{extraFrontMatter}---\n\n{body}\n";

    public SourceTree Add(string relativePath, string content) => Add(relativePath, Encoding.UTF8.GetBytes(content));

    public SourceTree Add(string relativePath, byte[] content)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return this;
    }

    public IReadOnlyList<KnowledgeSource> Read() => KnowledgeSourceReader.Read(Root);

    public IReadOnlyList<string> Errors() => Assert.Throws<KnowledgeCorpusException>(Read).Errors;

    public void Dispose() => _root.Delete(recursive: true);
}
