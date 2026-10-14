using System.Security.Cryptography;
using System.Text;

namespace PaymentOps.Platform.Ai;

/// <summary>
/// SHA-256 over the UTF-8 bytes of a prompt body, after front matter is removed and CRLF and bare
/// CR are converted to LF. Nothing else is changed: no trimming, no Unicode normalization, and the
/// final newline counts. Written as lowercase hex prefixed with <c>sha256:</c>.
/// </summary>
public static class PromptHasher
{
    public const string Prefix = "sha256:";

    public static string Hash(string body) =>
        Prefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(AiReleaseText.NormalizeLineEndings(body))));
}
