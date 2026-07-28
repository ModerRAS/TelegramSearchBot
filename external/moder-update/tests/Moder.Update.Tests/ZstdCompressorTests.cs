using Moder.Update.Compression;

namespace Moder.Update.Tests;

public class ZstdCompressorTests
{
    private readonly ZstdCompressor _compressor = new();

    [Fact]
    public void Compress_Decompress_Roundtrip_ByteArray()
    {
        var original = "Hello, Moder.Update! This is a roundtrip test."u8.ToArray();

        var compressed = _compressor.Compress(original);
        var decompressed = _compressor.Decompress(compressed);

        Assert.Equal(original, decompressed);
    }

    [Fact]
    public void Compress_Decompress_Roundtrip_LargeData()
    {
        var original = new byte[100_000];
        Random.Shared.NextBytes(original);

        var compressed = _compressor.Compress(original);
        var decompressed = _compressor.Decompress(compressed);

        Assert.Equal(original, decompressed);
    }

    [Fact]
    public void Compress_Decompress_Roundtrip_EmptyData()
    {
        var original = Array.Empty<byte>();

        var compressed = _compressor.Compress(original);
        var decompressed = _compressor.Decompress(compressed);

        Assert.Equal(original, decompressed);
    }

    [Fact]
    public void CompressStream_DecompressStream_Roundtrip()
    {
        var original = "Stream compression roundtrip test data."u8.ToArray();
        using var inputStream = new MemoryStream(original);

        using var compressedStream = _compressor.CompressStream(inputStream);
        using var decompressedStream = _compressor.DecompressStream(compressedStream);

        using var result = new MemoryStream();
        decompressedStream.CopyTo(result);

        Assert.Equal(original, result.ToArray());
    }

    [Fact]
    public void Compressed_Data_Is_Smaller_For_Repetitive_Content()
    {
        var original = new byte[10_000];
        Array.Fill(original, (byte)'A');

        var compressed = _compressor.Compress(original);

        Assert.True(compressed.Length < original.Length,
            $"Compressed size ({compressed.Length}) should be smaller than original ({original.Length}).");
    }
}
