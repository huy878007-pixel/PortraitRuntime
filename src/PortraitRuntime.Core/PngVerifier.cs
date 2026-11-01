using System.IO.Compression;
using System.Text;

namespace PortraitRuntime.Core;

public static class PngVerifier
{
    static readonly uint[] crcTable = Enumerable.Range(0, 256).Select(n =>
    { uint c = (uint)n; for (int i = 0; i < 8; i++) c = (c & 1) != 0 ? 0xedb88320U ^ (c >> 1) : c >> 1; return c; }).ToArray();
    static uint Crc(byte[] type, byte[] data)
    {
        uint c = 0xffffffff;
        foreach (var b in type) c = crcTable[(c ^ b) & 255] ^ (c >> 8);
        foreach (var b in data) c = crcTable[(c ^ b) & 255] ^ (c >> 8);
        return c ^ 0xffffffff;
    }
    static uint Big(byte[] bytes, int i = 0) => ((uint)bytes[i] << 24) | ((uint)bytes[i + 1] << 16) | ((uint)bytes[i + 2] << 8) | bytes[i + 3];
    public static (int Width, int Height) Read(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length > 64 * 1024 * 1024) throw new PackException("PNG exceeds 64 MiB: " + path);
            using var reader = new BinaryReader(stream);
            if (!reader.ReadBytes(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) throw new PackException("Invalid PNG signature: " + path);
            int width = 0, height = 0, bits = 0, colors = 0, interlace = 0;
            bool ended = false, sawData = false;
            using var compressed = new MemoryStream();
            while (stream.Position < stream.Length)
            {
                var lengthBytes = reader.ReadBytes(4);
                if (lengthBytes.Length != 4) throw new InvalidDataException("Truncated PNG chunk");
                uint size = Big(lengthBytes);
                if (size > stream.Length - stream.Position - 8) throw new InvalidDataException("Truncated PNG payload");
                var type = reader.ReadBytes(4);
                var data = reader.ReadBytes((int)size);
                var crc = reader.ReadBytes(4);
                if (crc.Length != 4 || Big(crc) != Crc(type, data)) throw new InvalidDataException("PNG checksum mismatch");
                var kind = Encoding.ASCII.GetString(type);
                if (width == 0 && kind != "IHDR") throw new InvalidDataException("IHDR must be first");
                if (kind == "IHDR")
                {
                    if (width != 0 || data.Length != 13) throw new InvalidDataException("Invalid IHDR");
                    width = checked((int)Big(data)); height = checked((int)Big(data, 4)); bits = data[8];
                    int colorType = data[9];
                    colors = colorType == 0 || colorType == 3 ? 1 : colorType == 2 ? 3 : colorType == 4 ? 2 : colorType == 6 ? 4 : 0;
                    bool depth = colorType == 3 ? new[] { 1, 2, 4, 8 }.Contains(bits) : colorType == 0 ? new[] { 1, 2, 4, 8, 16 }.Contains(bits) : bits == 8 || bits == 16;
                    if (width <= 0 || height <= 0 || width > 8192 || height > 8192 || colors == 0 || !depth || data[10] != 0 || data[11] != 0 || data[12] > 1) throw new InvalidDataException("Unsupported PNG layout");
                    interlace = data[12];
                }
                if (kind == "IDAT") { compressed.Write(data, 0, data.Length); sawData = true; }
                if (kind == "IEND") { if (size != 0) throw new InvalidDataException("Invalid IEND"); ended = true; break; }
            }
            if (!ended || !sawData || stream.Position != stream.Length || compressed.Length < 6) throw new InvalidDataException("Incomplete PNG");
            byte[] payload = compressed.ToArray();
            if ((payload[0] & 15) != 8 || (payload[0] >> 4) > 7 || ((payload[0] << 8) | payload[1]) % 31 != 0 || (payload[1] & 32) != 0) throw new InvalidDataException("Invalid PNG zlib header");
            var rows = new List<int>();
            void Pass(int x, int y, int stepX, int stepY)
            {
                int w = width <= x ? 0 : (width - x + stepX - 1) / stepX;
                int h = height <= y ? 0 : (height - y + stepY - 1) / stepY;
                if (w == 0) return;
                int row = (w * colors * bits + 7) / 8;
                for (int i = 0; i < h; i++) rows.Add(row);
            }
            if (interlace == 0) Pass(0, 0, 1, 1);
            else { Pass(0, 0, 8, 8); Pass(4, 0, 8, 8); Pass(0, 4, 4, 8); Pass(2, 0, 4, 4); Pass(0, 2, 2, 4); Pass(1, 0, 2, 2); Pass(0, 1, 1, 2); }
            long expected = rows.Sum(x => (long)x + 1);
            if (expected > 128 * 1024 * 1024) throw new InvalidDataException("PNG decoded size exceeds limit");
            using var input = new MemoryStream(payload, 2, payload.Length - 6);
            using var inflate = new DeflateStream(input, CompressionMode.Decompress);
            var buffer = new byte[8192]; long total = 0; uint a = 1, bsum = 0;
            int rowIndex = 0, column = 0, read;
            while ((read = inflate.Read(buffer, 0, buffer.Length)) != 0)
            {
                total += read; if (total > expected) throw new InvalidDataException("PNG decoded size mismatch");
                for (int i = 0; i < read; i++)
                {
                    byte value = buffer[i]; a = (a + value) % 65521; bsum = (bsum + a) % 65521;
                    if (column == 0 && value > 4) throw new InvalidDataException("Invalid PNG filter");
                    if (++column == rows[rowIndex] + 1) { rowIndex++; column = 0; }
                }
            }
            if (total != expected || Big(payload, payload.Length - 4) != ((bsum << 16) | a)) throw new InvalidDataException("PNG decompression checksum mismatch");
            return (width, height);
        }
        catch (Exception e) when (e is IOException || e is InvalidDataException || e is OverflowException || e is ArgumentException)
        { throw new PackException("Invalid PNG " + path + ": " + e.Message); }
    }
}
