using System.Text;

using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PaymentOps.Platform.AiRelease;

/// <summary>Decoding and strict YAML reading shared by the manifest and prompt front matter.</summary>
internal static class AiReleaseText
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    // Unknown keys fail by default; required members and duplicate keys are enforced explicitly.
    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithEnforceRequiredMembers()
        .WithDuplicateKeyChecking()
        .Build();

    /// <summary>
    /// UTF-8 without a byte-order mark, with CRLF and bare CR converted to LF. Returns null and an
    /// error when the bytes aren't acceptable.
    /// </summary>
    public static string? Decode(byte[] bytes, string path, List<string> errors)
    {
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

    public static T? Deserialize<T>(string yaml, string path, List<string> errors)
        where T : class
    {
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

    private static T? Fail<T>(string error, List<string> errors)
        where T : class
    {
        errors.Add(error);
        return null;
    }
}
