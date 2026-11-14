using System.IO.Compression;

namespace smss_api_tests
{
    /// <summary>A minimal PNG reader for the card previews (8-bit RGB / RGBA, not interlaced), so tests can look at real pixels and no image library is needed.</summary>
    internal sealed class Pixels
    {
        public int W, H;
        public byte[] Rgb = Array.Empty<byte>();   // 3 bytes per pixel

        public (byte R, byte G, byte B) At(int x, int y) { var i = (y * W + x) * 3; return (Rgb[i], Rgb[i + 1], Rgb[i + 2]); }

        public static Pixels Decode(byte[] png)
        {
            var pos = 8;
            int w = 0, h = 0, depth = 0, type = 0, interlace = 0;
            using var idat = new MemoryStream();
            while (pos < png.Length)
            {
                var len = (png[pos] << 24) | (png[pos + 1] << 16) | (png[pos + 2] << 8) | png[pos + 3];
                var tag = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
                var d = pos + 8;
                if (tag == "IHDR") { w = (png[d] << 24) | (png[d + 1] << 16) | (png[d + 2] << 8) | png[d + 3]; h = (png[d + 4] << 24) | (png[d + 5] << 16) | (png[d + 6] << 8) | png[d + 7]; depth = png[d + 8]; type = png[d + 9]; interlace = png[d + 12]; }
                else if (tag == "IDAT") idat.Write(png, d, len);
                else if (tag == "IEND") break;
                pos = d + len + 4;
            }
            if (depth != 8 || interlace != 0 || (type != 2 && type != 6)) throw new NotSupportedException($"PNG depth {depth} type {type} interlace {interlace}");
            var bpp = type == 6 ? 4 : 3;
            idat.Position = 2;   // skip the zlib header
            using var z = new DeflateStream(idat, CompressionMode.Decompress);
            var raw = new byte[(w * bpp + 1) * h];
            var read = 0;
            while (read < raw.Length) { var n = z.Read(raw, read, raw.Length - read); if (n == 0) break; read += n; }
            var stride = w * bpp;
            var cur = new byte[stride];
            var prev = new byte[stride];
            var rgb = new byte[w * h * 3];
            for (var y = 0; y < h; y++)
            {
                var f = raw[y * (stride + 1)];
                Buffer.BlockCopy(raw, y * (stride + 1) + 1, cur, 0, stride);
                for (var i = 0; i < stride; i++)
                {
                    int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                    cur[i] = (byte)(cur[i] + f switch
                    {
                        1 => a, 2 => b, 3 => (a + b) / 2,
                        4 => Paeth(a, b, c),
                        _ => 0
                    });
                }
                for (var x = 0; x < w; x++) { rgb[(y * w + x) * 3] = cur[x * bpp]; rgb[(y * w + x) * 3 + 1] = cur[x * bpp + 1]; rgb[(y * w + x) * 3 + 2] = cur[x * bpp + 2]; }
                (prev, cur) = (cur, prev);
            }
            return new Pixels { W = w, H = h, Rgb = rgb };
        }

        private static int Paeth(int a, int b, int c)
        {
            var p = a + b - c; var pa = Math.Abs(p - a); var pb = Math.Abs(p - b); var pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }

        /// <summary>Bounding box (x0, y0, x1, y1 inclusive) of the pixels where the two images differ noticeably; null when they are the same.</summary>
        public static (int X0, int Y0, int X1, int Y1)? DiffBox(Pixels a, Pixels b, int threshold = 40)
        {
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            for (var y = 0; y < a.H; y++)
                for (var x = 0; x < a.W; x++)
                {
                    var p = a.At(x, y); var q = b.At(x, y);
                    if (Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B) <= threshold) continue;
                    if (x < x0) x0 = x; if (y < y0) y0 = y; if (x > x1) x1 = x; if (y > y1) y1 = y;
                }
            return x1 < 0 ? null : (x0, y0, x1, y1);
        }

        /// <summary>Bounding box of pixels close to a colour (inside an optional region).</summary>
        public (int X0, int Y0, int X1, int Y1)? ColourBox(byte r, byte g, byte b, (int X0, int Y0, int X1, int Y1)? within = null, int tol = 60)
        {
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            var (ax, ay, bx, by) = within ?? (0, 0, W - 1, H - 1);
            for (var y = ay; y <= by; y++)
                for (var x = ax; x <= bx; x++)
                {
                    var p = At(x, y);
                    if (Math.Abs(p.R - r) + Math.Abs(p.G - g) + Math.Abs(p.B - b) > tol) continue;
                    if (x < x0) x0 = x; if (y < y0) y0 = y; if (x > x1) x1 = x; if (y > y1) y1 = y;
                }
            return x1 < 0 ? null : (x0, y0, x1, y1);
        }

        public static bool Overlap((int X0, int Y0, int X1, int Y1) a, (int X0, int Y0, int X1, int Y1) b) =>
            a.X0 <= b.X1 && b.X0 <= a.X1 && a.Y0 <= b.Y1 && b.Y0 <= a.Y1;
    }
}
