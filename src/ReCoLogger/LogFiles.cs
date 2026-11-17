using System.IO.Compression;
using System.Text;

namespace ReCoLogger;

/// <summary>
/// Reads saved logs back from the session files (the last N sessions).
/// Every method takes an optional <c>directory</c>; by default the folder of the running logger is used.
/// </summary>
public static class LogFiles
{
    /// <summary>Session files ordered from the oldest to the newest.</summary>
    public static IReadOnlyList<string> GetSessionFiles(string? directory = null)
    {
        if (directory == null)
        {
            Log.Flush(); // make sure the current session's buffered records are on disk
            directory = Log.LogDirectory;
        }
        if (directory == null || !Directory.Exists(directory)) return [];
        return Directory.GetFiles(directory, "log_*.txt").OrderBy(f => f, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Returns the last <paramref name="n"/> records across all sessions in chronological order,
    /// optionally filtered. The filter is applied first, so you get n records that pass it.
    /// A multi-line record (exception, stack trace) counts as one.
    /// </summary>
    public static IReadOnlyList<string> ReadLast(int n, LogFilter? filter = null, string? directory = null)
    {
        var files = GetSessionFiles(directory);
        var result = new List<string>();
        for (int fi = files.Count - 1; fi >= 0 && result.Count < n; fi--)
        {
            var recs = ReadRecords(files[fi]);
            for (int i = recs.Count - 1; i >= 0 && result.Count < n; i--)
                if (filter == null || filter.MatchesRecord(recs[i])) result.Add(recs[i]);
        }
        result.Reverse();
        return result;
    }

    /// <summary>Returns every record from all saved sessions, optionally filtered.</summary>
    public static IReadOnlyList<string> ReadAll(LogFilter? filter = null, string? directory = null)
    {
        var result = new List<string>();
        foreach (var f in GetSessionFiles(directory))
            foreach (var r in ReadRecords(f))
                if (filter == null || filter.MatchesRecord(r)) result.Add(r);
        return result;
    }

    /// <summary>Packs all log files into a zip, e.g. to send to a developer.</summary>
    public static void ExportZip(string zipPath, string? directory = null)
    {
        if (File.Exists(zipPath)) File.Delete(zipPath);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var f in GetSessionFiles(directory))
        {
            var entry = zip.CreateEntry(Path.GetFileName(f), CompressionLevel.Optimal);
            using var src = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var dst = entry.Open();
            src.CopyTo(dst);
        }
    }

    /// <summary>Splits a file into records. Lines starting with a space continue the previous record.</summary>
    static List<string> ReadRecords(string path)
    {
        var recs = new List<string>();
        StringBuilder? cur = null;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs, Encoding.UTF8);
        string? line;
        while ((line = sr.ReadLine()) != null)
        {
            if (cur != null && line.Length > 0 && line[0] == ' ') cur.Append('\n').Append(line);
            else
            {
                if (cur != null) recs.Add(cur.ToString());
                cur = new StringBuilder(line);
            }
        }
        if (cur != null) recs.Add(cur.ToString());
        return recs;
    }
}
