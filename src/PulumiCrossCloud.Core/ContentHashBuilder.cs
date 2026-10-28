using System.Buffers.Binary;
using System.Security.Cryptography;

namespace PulumiCrossCloud.Core;

internal static class ContentHashBuilder
{
    internal static void AppendBlock(HashAlgorithm hash, byte[] bytes)
    {
        // Frame paths and contents so file "a" containing "bc" cannot hash
        // identically to file "ab" containing "c".
        var length = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(length, bytes.LongLength);
        hash.TransformBlock(length, 0, length.Length, null, 0);
        hash.TransformBlock(bytes, 0, bytes.Length, null, 0);
    }
}
