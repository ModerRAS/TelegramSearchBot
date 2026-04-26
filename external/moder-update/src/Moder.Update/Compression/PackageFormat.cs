namespace Moder.Update.Compression;

/// <summary>
/// Defines the binary package format for Moder.Update packages.
/// Format: [4 bytes magic] + [zstd compressed tar data]
/// </summary>
public static class PackageFormat
{
    /// <summary>Magic bytes identifying a Moder.Update package: "MUP\0".</summary>
    public static readonly byte[] MagicBytes = [0x4D, 0x55, 0x50, 0x00];

    /// <summary>Size of the magic header in bytes.</summary>
    public const int HeaderSize = 4;

    /// <summary>
    /// Validates that the stream starts with the correct magic bytes.
    /// </summary>
    public static bool ValidateMagic(Stream stream)
    {
        var buffer = new byte[HeaderSize];
        var bytesRead = stream.Read(buffer, 0, HeaderSize);
        return bytesRead == HeaderSize && buffer.AsSpan().SequenceEqual(MagicBytes);
    }

    /// <summary>
    /// Writes the magic header to the stream.
    /// </summary>
    public static void WriteMagic(Stream stream)
    {
        stream.Write(MagicBytes, 0, HeaderSize);
    }
}
