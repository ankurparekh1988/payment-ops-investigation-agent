using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

using PaymentOps.Knowledge.Corpus;

namespace PaymentOps.Knowledge.Indexing;

/// <summary>
/// A section's search document key: the doc id, then the full SHA-256 of its heading path. Doc ids
/// never contain "--", and each heading is length-prefixed, so two different paths can't share a key.
/// </summary>
internal static class SectionKey
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
}
