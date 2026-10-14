using System.Security.Cryptography;
using System.Text;

using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PaymentOps.Platform.Text;

/// <summary>
/// Reading of reviewed source files: strict UTF-8, LF line endings, strict YAML and front matter.
/// Every method reports problems into an error list, so a caller can report all of them at once.
/// </summary>
public static class SourceText
{
    public const string Sha256Prefix = "sha256:";

    private const string Delimiter = "---";

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    // Unknown keys fail by default; required members, non-null values and duplicate keys are
    // enforced explicitly.
    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithEnforceRequiredMembers()
        .WithEnforceNullability()
        .WithDuplicateKeyChecking()
        .Build();

    /// <summary>
    /// UTF-8 without a byte-order mark, with CRLF and bare CR converted to LF. Returns null and an
    /// error when the bytes aren't acceptable.
    /// </summary>
    public static string? Decode(byte[] bytes, string path, List<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        if (bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble))
        {
            errors.Add($"{path} starts with a UTF-8 byte-order mark; save it as UTF-8 without one.");
            return null;
        }

        try
        {
            return NormalizeLineEndings(StrictUtf8.GetString(bytes));
        }
        catch (DecoderFallbackException)
        {
            errors.Add($"{path} isn't valid UTF-8.");
            return null;
        }
    }

    public static string NormalizeLineEndings(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    /// <summary>
    /// SHA-256 over the UTF-8 bytes of the text with LF line endings, as lowercase hex prefixed with
    /// <c>sha256:</c>. Nothing else is changed, so a checkout on Windows or Linux hashes the same.
    /// </summary>
    public static string Sha256(string text) =>
        Sha256Prefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeLineEndings(text))));

    public static T? Deserialize<T>(string yaml, string path, List<string> errors)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(errors);

        try
        {
            return Yaml.Deserialize<T>(yaml) ?? Fail<T>($"{path} is empty.", errors);
        }
        catch (YamlException ex)
        {
            var reason = ex.InnerException?.Message ?? ex.Message;
            return Fail<T>($"{path} (line {ex.Start.Line}): {reason}", errors);
        }
    }

    /// <summary>
    /// Splits LF text into front matter and body. The first line must be exactly <c>---</c>; the next
    /// line that is exactly <c>---</c> ends the front matter, and everything after it is the body.
    /// <c>BodyLine</c> is the 1-based line of the file the body starts on, for error messages.
    /// </summary>
    public static (string FrontMatter, string Body, int BodyLine)? SplitFrontMatter(string text, string path, List<string> errors)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(errors);

        const string Opening = Delimiter + "\n";
        const string Closing = "\n" + Delimiter;

        if (!text.StartsWith(Opening, StringComparison.Ordinal))
        {
            errors.Add($"{path} must start with a '{Delimiter}' line opening its front matter.");
            return null;
        }

        // Searching from the opening line's own newline also finds an empty front matter.
        var close = text.IndexOf(Closing + "\n", Opening.Length - 1, StringComparison.Ordinal);
        if (close >= 0)
        {
            var bodyStart = close + Closing.Length + 1;
            return (text[Opening.Length..(close + 1)], text[bodyStart..], text.AsSpan(0, bodyStart).Count('\n') + 1);
        }

        if (text.Length > Opening.Length && text.EndsWith(Closing, StringComparison.Ordinal))
        {
            return (text[Opening.Length..(text.Length - Delimiter.Length)], string.Empty, text.AsSpan().Count('\n') + 2);
        }

        errors.Add($"{path} has no closing '{Delimiter}' line after its front matter.");
        return null;
    }

    private static T? Fail<T>(string error, List<string> errors)
        where T : class
    {
        errors.Add(error);
        return null;
    }
}
