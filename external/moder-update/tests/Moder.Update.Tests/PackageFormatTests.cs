using Moder.Update.Compression;

namespace Moder.Update.Tests;

public class PackageFormatTests
{
    [Fact]
    public void ValidateMagic_ValidHeader_ReturnsTrue()
    {
        using var stream = new MemoryStream();
        PackageFormat.WriteMagic(stream);
        stream.Position = 0;

        Assert.True(PackageFormat.ValidateMagic(stream));
    }

    [Fact]
    public void ValidateMagic_InvalidHeader_ReturnsFalse()
    {
        using var stream = new MemoryStream(new byte[] { 0x00, 0x00, 0x00, 0x00 });

        Assert.False(PackageFormat.ValidateMagic(stream));
    }

    [Fact]
    public void ValidateMagic_EmptyStream_ReturnsFalse()
    {
        using var stream = new MemoryStream();

        Assert.False(PackageFormat.ValidateMagic(stream));
    }

    [Fact]
    public void ValidateMagic_TooShortStream_ReturnsFalse()
    {
        using var stream = new MemoryStream(new byte[] { 0x4D, 0x55 });

        Assert.False(PackageFormat.ValidateMagic(stream));
    }

    [Fact]
    public void MagicBytes_AreCorrect()
    {
        Assert.Equal(4, PackageFormat.MagicBytes.Length);
        Assert.Equal(0x4D, PackageFormat.MagicBytes[0]); // 'M'
        Assert.Equal(0x55, PackageFormat.MagicBytes[1]); // 'U'
        Assert.Equal(0x50, PackageFormat.MagicBytes[2]); // 'P'
        Assert.Equal(0x00, PackageFormat.MagicBytes[3]); // '\0'
    }
}
