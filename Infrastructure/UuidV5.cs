using System.Security.Cryptography;

namespace DndMcpAICsharpFun.Infrastructure;

/// <summary>
/// Generates RFC 4122 version-5 (SHA-1 name-based) UUIDs.
/// </summary>
public static class UuidV5
{
    /// <summary>
    /// Computes a deterministic UUID v5 from <paramref name="namespaceId"/> and
    /// a UTF-8 <paramref name="name"/> byte array.
    /// </summary>
    /// <remarks>
    /// The .NET <see cref="Guid"/> type stores bytes in mixed-endian order.  This
    /// method converts to/from big-endian network byte order as required by
    /// RFC 4122 §4.3 before and after the SHA-1 hash so that the result is
    /// identical to any standards-conformant UUID v5 implementation.
    /// </remarks>
    public static Guid Create(Guid namespaceId, byte[] name)
    {
        // Represent the namespace UUID in big-endian (network) byte order for hashing.
        Span<byte> ns = stackalloc byte[16];
        if (!namespaceId.TryWriteBytes(ns)) throw new InvalidOperationException();

        // .NET stores Guid bytes in mixed-endian; swap to big-endian for RFC 4122.
        (ns[0], ns[3]) = (ns[3], ns[0]);
        (ns[1], ns[2]) = (ns[2], ns[1]);
        (ns[4], ns[5]) = (ns[5], ns[4]);
        (ns[6], ns[7]) = (ns[7], ns[6]);

        byte[] input = new byte[16 + name.Length];
        ns.CopyTo(input);
        name.CopyTo(input, 16);

        byte[] hash = SHA1.HashData(input);

        // Set version bits (5) in octet 6 and variant bits (RFC 4122) in octet 8.
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

        // Convert the first 16 bytes back from big-endian to .NET mixed-endian Guid layout.
        Span<byte> result = hash.AsSpan(0, 16);
        (result[0], result[3]) = (result[3], result[0]);
        (result[1], result[2]) = (result[2], result[1]);
        (result[4], result[5]) = (result[5], result[4]);
        (result[6], result[7]) = (result[7], result[6]);

        return new Guid(result);
    }
}