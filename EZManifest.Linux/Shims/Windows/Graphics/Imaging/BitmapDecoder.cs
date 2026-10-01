using System.Buffers.Binary;
using System.IO;
using Windows.Storage;

namespace Windows.Graphics.Imaging;

/// <summary>
/// Portable stand-in for the WinRT BitmapDecoder: decodes PNG and JPEG headers
/// to expose PixelWidth / PixelHeight, which is all the shared code needs.
/// </summary>
public sealed class BitmapDecoder
{
    public uint PixelWidth { get; }
    public uint PixelHeight { get; }

    private BitmapDecoder(uint width, uint height)
    {
        PixelWidth = width;
        PixelHeight = height;
    }

    public static Task<BitmapDecoder> CreateAsync(IRandomAccessStream stream)
    {
        using Stream source = stream.AsStreamForRead();
        (uint width, uint height) = ReadPngSize(source) ?? ReadJpegSize(source) ?? (0, 0);
        return Task.FromResult(new BitmapDecoder(width, height));
    }

    private static (uint Width, uint Height)? ReadPngSize(Stream stream)
    {
        stream.Position = 0;
        Span<byte> header = stackalloc byte[24];
        int read = stream.Read(header);
        if (read < 24)
            return null;
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        for (int i = 0; i < 8; i++)
        {
            if (header[i] != signature[i])
                return null;
        }
        uint width = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header.Slice(16, 4));
        uint height = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header.Slice(20, 4));
        return (width, height);
    }

    private static (uint Width, uint Height)? ReadJpegSize(Stream stream)
    {
        stream.Position = 0;
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        if (reader.ReadByte() != 0xFF || reader.ReadByte() != 0xD8)
            return null;
        while (true)
        {
            int b = reader.ReadByte();
            if (b != 0xFF)
                continue;
            int marker = reader.ReadByte();
            if (marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF)
            {
                reader.ReadUInt16(); // segment length
                reader.ReadByte();   // precision
                uint height = reader.ReadUInt16();
                uint width = reader.ReadUInt16();
                return (width, height);
            }
            if (marker is 0xD8 or 0x01 or ( >= 0xD0 and <= 0xD7))
                continue;
            int length = reader.ReadUInt16();
            stream.Seek(length - 2, SeekOrigin.Current);
        }
    }
}

