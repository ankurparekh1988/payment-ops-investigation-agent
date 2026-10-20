using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using PaymentOps.Knowledge.Corpus;

namespace PaymentOps.Knowledge.Indexing;

/// <summary>
/// A section's search document key: the doc id, then the full SHA-256 of its heading path. Doc ids
/// never contain "--", and each heading is length-prefixed, so two different paths can't share a key.
/// </summary>
internal static partial class SectionKey
{
    public static string For(string docId, SectionPath path)
    {
        using var input = new MemoryStream();
        Span<byte> length = stackalloc byte[sizeof(int)];
        foreach (var heading in (string[])[path.H1, path.H2, path.H3])
        {
            var bytes = Encoding.UTF8.GetBytes(heading);
            BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            input.Write(length);
            input.Write(bytes);
        }

        return $"{docId}--{Convert.ToHexStringLower(SHA256.HashData(input.ToArray()))}";
    }

    /// <summary>Whether a key has the shape <see cref="For"/> produces. Checked before a key reaches a query.</summary>
    public static bool IsWellFormed(string key) => WellFormed().IsMatch(key);

    // The doc id rule from the source reader, then the hex hash.
    [GeneratedRegex("^(?=.{1,64}--)[a-z0-9]+(-[a-z0-9]+)*--[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex WellFormed();
}
