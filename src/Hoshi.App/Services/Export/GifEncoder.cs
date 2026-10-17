namespace Hoshi.App.Services.Export;

/// <summary>One picture of an animation: BGRA pixels (4 bytes each, row by row) and how long it stays.</summary>
public sealed record GifFrame(byte[] Bgra, TimeSpan Delay);

/// <summary>
/// A small animated GIF (GIF89a) writer, Hoshi's own: one palette for the whole animation (median cut over a 15-bit
/// histogram of every frame, so the wood, both stones and the red marks all get colours), and each frame after the
/// first stores only the rectangle that changed. Loops forever.
/// </summary>
public static class GifEncoder
{
    private const int Bins = 1 << 15;

    public static byte[] Encode(int width, int height, IReadOnlyList<GifFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        if (frames.Count == 0)
        {
            throw new ArgumentException("An animation needs at least one frame.", nameof(frames));
        }

        foreach (GifFrame f in frames)
        {
            if (f.Bgra.Length != width * height * 4)
            {
                throw new ArgumentException("Every frame must have width × height BGRA pixels.", nameof(frames));
            }
        }

        (byte[] palette, byte[] lookup) = Palette(frames);
        using var output = new MemoryStream();
        var w = new BinaryWriter(output);
        w.Write("GIF89a"u8);
        w.Write((ushort)width);
        w.Write((ushort)height);
        w.Write((byte)0xF7); // global colour table, 8 bits, 256 entries
        w.Write((byte)0);
        w.Write((byte)0);
        w.Write(palette);

        // Netscape loop extension: repeat forever.
        w.Write([0x21, 0xFF, 0x0B]);
        w.Write("NETSCAPE2.0"u8);
        w.Write([0x03, 0x01, 0x00, 0x00, 0x00]);

        byte[]? previous = null;
        foreach (GifFrame frame in frames)
        {
            byte[] indices = Index(frame.Bgra, lookup);
            (int x, int y, int fw, int fh) = previous is null ? (0, 0, width, height) : Changed(previous, indices, width, height);
            previous = indices;

            int centis = Math.Clamp((int)Math.Round(frame.Delay.TotalMilliseconds / 10), 2, 65535);
            w.Write([0x21, 0xF9, 0x04, 0x04]); // graphic control: keep the frame under the next one
            w.Write((ushort)centis);
            w.Write([0x00, 0x00]);

            w.Write((byte)0x2C);
            w.Write((ushort)x);
            w.Write((ushort)y);
            w.Write((ushort)fw);
            w.Write((ushort)fh);
            w.Write((byte)0);

            byte[] sub = new byte[fw * fh];
            for (int row = 0; row < fh; row++)
            {
                Array.Copy(indices, ((y + row) * width) + x, sub, row * fw, fw);
            }

            w.Write((byte)8);
            WriteBlocks(w, Lzw(sub));
        }

        w.Write((byte)0x3B);
        w.Flush();
        return output.ToArray();
    }

    // The smallest rectangle holding every pixel that differs (at least one pixel, so the frame still counts).
    private static (int X, int Y, int W, int H) Changed(byte[] before, byte[] after, int width, int height)
    {
        int minX = width, minY = height, maxX = -1, maxY = -1;
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                if (before[row + x] != after[row + x])
                {
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                }
            }
        }

        return maxX < 0 ? (0, 0, 1, 1) : (minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static int Bin(byte[] bgra, int i) => ((bgra[i + 2] >> 3) << 10) | ((bgra[i + 1] >> 3) << 5) | (bgra[i] >> 3);

    private static byte[] Index(byte[] bgra, byte[] lookup)
    {
        byte[] indices = new byte[bgra.Length / 4];
        for (int p = 0; p < indices.Length; p++)
        {
            indices[p] = lookup[Bin(bgra, p * 4)];
        }

        return indices;
    }

    /// <summary>The 256-colour palette (RGB triples) and, for each 15-bit colour, its nearest palette entry.</summary>
    internal static (byte[] Palette, byte[] Lookup) Palette(IReadOnlyList<GifFrame> frames)
    {
        int[] counts = new int[Bins];
        foreach (GifFrame f in frames)
        {
            for (int i = 0; i < f.Bgra.Length; i += 4)
            {
                counts[Bin(f.Bgra, i)]++;
            }
        }

        List<int> used = [];
        for (int b = 0; b < Bins; b++)
        {
            if (counts[b] > 0)
            {
                used.Add(b);
            }
        }

        // Median cut: split the box with the widest channel range at its weighted median until there are 256.
        var boxes = new List<List<int>> { used };
        while (boxes.Count < 256)
        {
            int best = -1, bestRange = 0, bestChannel = 0;
            for (int i = 0; i < boxes.Count; i++)
            {
                if (boxes[i].Count < 2)
                {
                    continue;
                }

                for (int c = 0; c < 3; c++)
                {
                    int lo = int.MaxValue, hi = int.MinValue;
                    foreach (int b in boxes[i])
                    {
                        int v = Channel(b, c);
                        lo = Math.Min(lo, v);
                        hi = Math.Max(hi, v);
                    }

                    if (hi - lo > bestRange)
                    {
                        (best, bestRange, bestChannel) = (i, hi - lo, c);
                    }
                }
            }

            if (best < 0)
            {
                break;
            }

            List<int> box = boxes[best];
            int channel = bestChannel;
            box.Sort((a, b) => Channel(a, channel).CompareTo(Channel(b, channel)));
            long total = box.Sum(b => (long)counts[b]);
            long run = 0;
            int cut = 1;
            for (int i = 0; i < box.Count - 1; i++)
            {
                run += counts[box[i]];
                cut = i + 1;
                if (run * 2 >= total)
                {
                    break;
                }
            }

            boxes[best] = box[..cut];
            boxes.Add(box[cut..]);
        }

        byte[] palette = new byte[256 * 3];
        for (int i = 0; i < boxes.Count; i++)
        {
            long r = 0, g = 0, bl = 0, n = 0;
            foreach (int b in boxes[i])
            {
                long k = counts[b];
                r += Channel(b, 0) * k;
                g += Channel(b, 1) * k;
                bl += Channel(b, 2) * k;
                n += k;
            }

            n = Math.Max(n, 1);
            palette[i * 3] = Expand((int)(r / n));
            palette[(i * 3) + 1] = Expand((int)(g / n));
            palette[(i * 3) + 2] = Expand((int)(bl / n));
        }

        int colours = Math.Max(boxes.Count, 1);
        byte[] lookup = new byte[Bins];
        foreach (int b in used)
        {
            int r = Expand(Channel(b, 0)), g = Expand(Channel(b, 1)), bl = Expand(Channel(b, 2));
            int nearest = 0, distance = int.MaxValue;
            for (int i = 0; i < colours; i++)
            {
                int dr = r - palette[i * 3], dg = g - palette[(i * 3) + 1], db = bl - palette[(i * 3) + 2];
                int d = (dr * dr * 3) + (dg * dg * 4) + (db * db * 2);
                if (d < distance)
                {
                    (nearest, distance) = (i, d);
                }
            }

            lookup[b] = (byte)nearest;
        }

        return (palette, lookup);
    }

    private static int Channel(int bin, int c) => c switch
    {
        0 => (bin >> 10) & 31,
        1 => (bin >> 5) & 31,
        _ => bin & 31,
    };

    private static byte Expand(int five) => (byte)((five << 3) | (five >> 2));

    /// <summary>GIF's LZW with 8-bit pixels: variable code size, clear code when the table is full.</summary>
    internal static byte[] Lzw(byte[] pixels)
    {
        const int clear = 256, end = 257;
        var bits = new BitPacker();
        var table = new Dictionary<int, int>();
        int size = 9, next = 258;
        bits.Write(clear, size);
        if (pixels.Length == 0)
        {
            bits.Write(end, size);
            return bits.ToArray();
        }

        int prefix = pixels[0];
        for (int i = 1; i < pixels.Length; i++)
        {
            int key = (prefix << 8) | pixels[i];
            if (table.TryGetValue(key, out int code))
            {
                prefix = code;
                continue;
            }

            bits.Write(prefix, size);
            if (next < 4096)
            {
                table[key] = next++;
                if (next > (1 << size) && size < 12)
                {
                    size++;
                }
            }
            else
            {
                bits.Write(clear, size);
                table.Clear();
                size = 9;
                next = 258;
            }

            prefix = pixels[i];
        }

        bits.Write(prefix, size);
        bits.Write(end, size);
        return bits.ToArray();
    }

    private static void WriteBlocks(BinaryWriter w, byte[] data)
    {
        for (int i = 0; i < data.Length; i += 255)
        {
            int n = Math.Min(255, data.Length - i);
            w.Write((byte)n);
            w.Write(data, i, n);
        }

        w.Write((byte)0);
    }

    private sealed class BitPacker
    {
        private readonly List<byte> _bytes = [];
        private int _buffer;
        private int _count;

        public void Write(int code, int size)
        {
            _buffer |= code << _count;
            _count += size;
            while (_count >= 8)
            {
                _bytes.Add((byte)_buffer);
                _buffer >>= 8;
                _count -= 8;
            }
        }

        public byte[] ToArray()
        {
            if (_count > 0)
            {
                _bytes.Add((byte)_buffer);
                _buffer = 0;
                _count = 0;
            }

            return [.. _bytes];
        }
    }
}
