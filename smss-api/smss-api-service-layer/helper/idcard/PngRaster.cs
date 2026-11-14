using System.IO.Compression;

namespace smss_api_service_layer.helper.idcard
{
    /// <summary>
    /// A tiny PNG reader for the 48 x 48 pixel probe pages the logo check draws (8-bit RGB or RGBA, not interlaced), so the check needs no image library. It reads only what QuestPDF
    /// itself wrote a moment ago, never an uploaded file.
    /// </summary>
    internal static class PngRaster
    {
        public static (int W, int H, byte[] Rgb)? TryDecode(byte[] png)
        {
            try
            {
                if (png.Length < 40 || png[0] != 0x89 || png[1] != 0x50) return null;
                var pos = 8;
                int w = 0, h = 0, depth = 0, type = 0, interlace = 0;
                using var idat = new MemoryStream();
                while (pos + 8 <= png.Length)
                {
                    var len = (png[pos] << 24) | (png[pos + 1] << 16) | (png[pos + 2] << 8) | png[pos + 3];
                    var tag = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
                    var d = pos + 8;
                    if (len < 0 || d + len > png.Length) return null;
                    if (tag == "IHDR") { w = (png[d] << 24) | (png[d + 1] << 16) | (png[d + 2] << 8) | png[d + 3]; h = (png[d + 4] << 24) | (png[d + 5] << 16) | (png[d + 6] << 8) | png[d + 7]; depth = png[d + 8]; type = png[d + 9]; interlace = png[d + 12]; }
                    else if (tag == "IDAT") idat.Write(png, d, len);
                    else if (tag == "IEND") break;
                    pos = d + len + 4;
                }
                if (w <= 0 || h <= 0 || w > 4096 || h > 4096 || depth != 8 || interlace != 0 || (type != 2 && type != 6)) return null;
                var bpp = type == 6 ? 4 : 3;
                idat.Position = 2;                                   // the zlib header
                using var z = new DeflateStream(idat, CompressionMode.Decompress);
                var stride = w * bpp;
                var raw = new byte[(stride + 1) * h];
                var read = 0;
                while (read < raw.Length) { var n = z.Read(raw, read, raw.Length - read); if (n == 0) break; read += n; }
                if (read < raw.Length) return null;
                var cur = new byte[stride];
                var prev = new byte[stride];
                var rgb = new byte[w * h * 3];
                for (var y = 0; y < h; y++)
                {
                    var filter = raw[y * (stride + 1)];
                    Buffer.BlockCopy(raw, y * (stride + 1) + 1, cur, 0, stride);
                    for (var i = 0; i < stride; i++)
                    {
                        int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                        cur[i] = (byte)(cur[i] + filter switch { 1 => a, 2 => b, 3 => (a + b) / 2, 4 => Paeth(a, b, c), _ => 0 });
                    }
                    for (var x = 0; x < w; x++)
                    {
                        rgb[(y * w + x) * 3] = cur[x * bpp]; rgb[(y * w + x) * 3 + 1] = cur[x * bpp + 1]; rgb[(y * w + x) * 3 + 2] = cur[x * bpp + 2];
                    }
                    (prev, cur) = (cur, prev);
                }
                return (w, h, rgb);
            }
            catch { return null; }
        }

        private static int Paeth(int a, int b, int c)
        {
            var p = a + b - c; var pa = Math.Abs(p - a); var pb = Math.Abs(p - b); var pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }
    }
}
