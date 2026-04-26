using ZstdSharp;

namespace Moder.Update.Compression;

/// <summary>
/// Zstd compression/decompression using ZstdSharp.
/// </summary>
public class ZstdCompressor : IZstdCompressor
{
    private readonly int _compressionLevel;

    public ZstdCompressor(int compressionLevel = 3)
    {
        _compressionLevel = compressionLevel;
    }

    public byte[] Compress(byte[] data)
    {
        using var compressor = new Compressor(_compressionLevel);
        return compressor.Wrap(data).ToArray();
    }

    public byte[] Decompress(byte[] compressedData)
    {
        using var decompressor = new Decompressor();
        return decompressor.Unwrap(compressedData).ToArray();
    }

    public Stream CompressStream(Stream input)
    {
        var output = new MemoryStream();
        using (var compressionStream = new CompressionStream(output, _compressionLevel, leaveOpen: true))
        {
            input.CopyTo(compressionStream);
        }
        output.Position = 0;
        return output;
    }

    public Stream DecompressStream(Stream input)
    {
        var output = new MemoryStream();
        using (var decompressionStream = new DecompressionStream(input, leaveOpen: true))
        {
            decompressionStream.CopyTo(output);
        }
        output.Position = 0;
        return output;
    }
}
