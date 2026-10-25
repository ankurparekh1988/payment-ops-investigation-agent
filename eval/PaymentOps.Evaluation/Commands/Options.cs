using System.Globalization;
using System.Text.Json;

using PaymentOps.Evaluation.Running;
using PaymentOps.Knowledge.Corpus;
using PaymentOps.Platform.Ai;
using PaymentOps.Tools.Shared;

namespace PaymentOps.Evaluation.Commands;

/// <summary>The command line was wrong: an unknown or missing option, or a value that can't be used.</summary>
internal sealed class UsageException(string message) : Exception(message);

/// <summary>Named options in any order: <c>--name value</c>, repeatable where the command allows it, and bare flags.</summary>
internal sealed class Options
{
    private readonly Dictionary<string, List<string>> _values;
    private readonly HashSet<string> _flags;

    private Options(Dictionary<string, List<string>> values, HashSet<string> flags)
    {
        _values = values;
        _flags = flags;
    }

    public static Options Parse(IReadOnlyList<string> args, IReadOnlyCollection<string> valued, IReadOnlyCollection<string> flags)
    {
        var values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var set = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Count; i++)
        {
            var name = args[i].StartsWith("--", StringComparison.Ordinal) ? args[i][2..] : throw new UsageException($"'{args[i]}' isn't an option.");
            if (flags.Contains(name))
            {
                set.Add(name);
            }
            else if (!valued.Contains(name))
            {
                throw new UsageException($"--{name} isn't an option of this command.");
            }
            else if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new UsageException($"--{name} needs a value.");
            }
            else
            {
                (values.TryGetValue(name, out var list) ? list : values[name] = []).Add(args[++i]);
            }
        }

        return new Options(values, set);
    }

    public IReadOnlyList<string> All(string name) =>
        _values.TryGetValue(name, out var list) ? list : throw new UsageException($"--{name} is required.");

    public string One(string name) => All(name) is [var only] ? only : throw new UsageException($"--{name} can be given once.");

    public string? Optional(string name) => _values.ContainsKey(name) ? One(name) : null;

    public bool Flag(string name) => _flags.Contains(name);

    public Uri Https(string name) =>
        Uri.TryCreate(One(name), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri : throw new UsageException($"--{name} must be an https URL.");

    /// <summary>A file the command will write, checked now so a bad path doesn't waste a whole live evaluation.</summary>
    public string OutputFile(string name)
    {
        var path = Path.GetFullPath(One(name));
        return Directory.Exists(Path.GetDirectoryName(path)) && !Directory.Exists(path)
            ? path
            : throw new UsageException($"--{name} must be a file in a folder that exists.");
    }

    public int Positive(string name) =>
        int.TryParse(One(name), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : throw new UsageException($"--{name} must be a positive whole number.");

    /// <summary>
    /// The release from --ai, reading the index --index names: live by default, or the release's evaluation index,
    /// so every reader measures the change's own corpus.
    /// </summary>
    public AiRelease Release()
    {
        var release = AiReleaseLoader.Load(new DirectoryAiReleaseSource(One("ai")));
        return Optional("index") switch
        {
            null or "live" => release,
            "evaluation" => release with { KnowledgeIndex = release.EvaluationIndex },
            _ => throw new UsageException("--index must be live or evaluation."),
        };
    }
}

/// <summary>
/// The exit codes every evaluation command shares, with no overlap: 0 passed or report-only; 1 the evaluation
/// failed; 2 the command or its input files were wrong; 3 the live environment couldn't give a fair run.
/// </summary>
internal static class ExitCodes
{
    public const int Passed = 0;
    public const int Failed = 1;
    public const int InvalidInput = 2;
    public const int CouldNotEvaluate = 3;

    public static async Task<int> GuardAsync(string title, string usage, Func<Task<int>> run)
    {
        try
        {
            return await run();
        }
        catch (UsageException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine($"Usage: {usage}");
            return InvalidInput;
        }
        catch (AiReleaseException ex)
        {
            GitHubAnnotation.Errors("AI release", ex.Errors);
            return InvalidInput;
        }
        catch (KnowledgeCorpusException ex)
        {
            GitHubAnnotation.Errors("Knowledge corpus", ex.Errors);
            return InvalidInput;
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException)
        {
            GitHubAnnotation.Error(title, ex.Message);
            return InvalidInput;
        }
        catch (CouldNotEvaluateException ex)
        {
            GitHubAnnotation.Error(title, ex.Message);
            return CouldNotEvaluate;
        }
    }
}
