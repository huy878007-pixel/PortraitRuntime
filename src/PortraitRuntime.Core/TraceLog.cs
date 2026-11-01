using System.Text;
using Newtonsoft.Json;

namespace PortraitRuntime.Core;

/// <summary>Local JSONL trace. Files rotate within a session; existing sessions are never deleted.</summary>
public sealed class TraceLog : IDisposable
{
    readonly string directory;
    readonly long maxFileBytes;
    readonly int maxFiles;
    readonly object sync = new();
    StreamWriter writer;
    int fileIndex;
    long bytes, sequence;
    public string Session { get; } = DateTime.UtcNow.ToString("yyyyMMddTHHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
    public bool Detailed { get; set; } = true;
    public long Dropped { get; private set; }
    public string Failure { get; private set; }
    public string CurrentFile { get; private set; }
    public string DirectoryPath => directory;
    public List<string> Files { get; } = new();
    bool disposed;
    public TraceLog(string directory, long maxFileBytes = 4 * 1024 * 1024, int maxFiles = 32)
    {
        this.directory = Path.GetFullPath(directory); this.maxFileBytes = Math.Max(256, maxFileBytes); this.maxFiles = Math.Max(1, maxFiles);
        try { System.IO.Directory.CreateDirectory(this.directory); }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        { Failure = e.ToString(); }
    }
    public bool Write(string level, string name, object data = null)
    {
        lock (sync)
        {
            if (disposed || Failure != null || (!Detailed && level == "debug")) return false;
            try
            {
                string line = JsonConvert.SerializeObject(new { utc = DateTime.UtcNow.ToString("O"), session = Session, seq = ++sequence, level, @event = name, data },
                    new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore, MaxDepth = 32 });
                long size = Encoding.UTF8.GetByteCount(line) + Encoding.UTF8.GetByteCount(Environment.NewLine);
                if (size > maxFileBytes) { Dropped++; return false; }
                if (writer == null || bytes + size > maxFileBytes)
                {
                    if (fileIndex >= maxFiles) { Dropped++; return false; }
                    writer?.Dispose();
                    CurrentFile = Path.Combine(directory, "runtime-" + Session + "-" + (++fileIndex).ToString("D3") + ".jsonl");
                    writer = new StreamWriter(new FileStream(CurrentFile, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
                    Files.Add(CurrentFile); bytes = 0;
                }
                writer.WriteLine(line); bytes += size; return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException)
            { Failure = e.ToString(); return false; }
        }
    }
    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            try { writer?.Dispose(); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Failure = e.ToString(); }
            finally { disposed = true; }
        }
    }
}
