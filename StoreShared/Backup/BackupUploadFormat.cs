using System.Text;

namespace StoreShared.Backup;

/// <summary>Detects whether an uploaded backup is a zip archive or a raw SQLite file.</summary>
public static class BackupUploadFormat
{
    public enum Kind
    {
        Unknown,
        Zip,
        Sqlite,
    }

    public static async Task<Kind> DetectAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(filePath);
        return await DetectAsync(stream, cancellationToken);
    }

    public static async Task<Kind> DetectAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        if (!stream.CanRead)
            return Kind.Unknown;

        if (!stream.CanSeek)
        {
            await using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;
            return DetectFromHeader(buffer);
        }

        var position = stream.Position;
        try
        {
            return DetectFromHeader(stream);
        }
        finally
        {
            stream.Position = position;
        }
    }

    private static Kind DetectFromHeader(Stream stream)
    {
        Span<byte> header = stackalloc byte[16];
        var read = stream.Read(header);
        if (read >= 2 && header[0] == 0x50 && header[1] == 0x4B)
            return Kind.Zip;

        if (read >= 15 && Encoding.ASCII.GetString(header[..15]) == "SQLite format 3")
            return Kind.Sqlite;

        return Kind.Unknown;
    }
}
