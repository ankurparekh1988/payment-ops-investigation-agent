// Validates the knowledge sources and writes each document's body as <docId>.md, then corpus.json
// describing exactly what was built. Errors are written as GitHub annotations.
//
// Usage: PaymentOps.CorpusBuild <source-directory> --out <directory>

using PaymentOps.Knowledge.Corpus;
using PaymentOps.Tools.Shared;

if (args is not [var sourceDirectory, "--out", var outputDirectory])
{
    Console.Error.WriteLine("Usage: PaymentOps.CorpusBuild <source-directory> --out <directory>");
    return 2;
}

try
{
    var sources = KnowledgeSourceReader.Read(sourceDirectory);
    CorpusManifest.Write(sources, outputDirectory);
    Console.WriteLine($"Built {sources.Count} documents, {sources.Count(s => s.Classification == KnowledgeClassification.Restricted)} restricted, into {Path.GetFullPath(outputDirectory)}.");
    return 0;
}
catch (KnowledgeCorpusException ex)
{
    foreach (var error in ex.Errors)
    {
        GitHubAnnotation.Error("Knowledge corpus", error);
    }

    return 1;
}
