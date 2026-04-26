namespace Moder.Update.Compression;

/// <summary>
/// Interface for Zstd compression and decompression.
/// </summary>
public interface IZstdCompressor
{
    byte[] Compress(byte[] data);
    byte[] Decompress(byte[] compressedData);
    Stream CompressStream(Stream input);
    Stream DecompressStream(Stream input);
}
