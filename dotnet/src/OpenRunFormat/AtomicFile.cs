namespace OpenRunFormat;

/// <summary>Writes a file atomically: temporary file in the same directory, flushed to disk, then moved over the target.</summary>
internal static class AtomicFile
{
    public static void Write(string path, Action<Stream> write)
    {
        var full = Path.GetFullPath(path);
        var tmp = $"{full}.tmp-{Guid.NewGuid():N}";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(fs);
                fs.Flush(flushToDisk: true);
            }
            File.Move(tmp, full, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }
}
