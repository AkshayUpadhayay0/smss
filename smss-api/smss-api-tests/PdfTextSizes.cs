using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace smss_api_tests
{
    /// <summary>
    /// Reads the font size of every piece of TEXT that a PDF actually draws: it inflates the page content streams and follows the "size Tf" operators to the next show-text (Tj / TJ). It reports what
    /// was rendered (computed sizes, FitSize shrinking and all), not what the source says, so a size built as "size - 1.5f" cannot slip under a limit unnoticed. Text drawn through a scaling matrix
    /// is multiplied by the matrix's scale.
    /// </summary>
    public static class PdfTextSizes
    {
        private static readonly Regex Op = new(@"(?<n>-?\d*\.?\d+(?:[eE][-+]?\d+)?)\s+Tf\b|(?<tm>(?:-?\d*\.?\d+(?:[eE][-+]?\d+)?\s+){5}-?\d*\.?\d+(?:[eE][-+]?\d+)?\s+(?:Tm|cm))\b|\b(?<show>Tj|TJ)\b|\b(?<q>q|Q)\b|(?<bt>BT)", RegexOptions.Compiled);

        /// <summary>Every size (in points) at which text was shown, with how many shows used it.</summary>
        public static SortedDictionary<double, int> Sizes(byte[] pdf)
        {
            var result = new SortedDictionary<double, int>();
            var text = Encoding.Latin1.GetString(pdf);
            foreach (Match s in Regex.Matches(text, @"stream\r?\n"))
            {
                var start = s.Index + s.Length;
                var end = text.IndexOf("endstream", start, StringComparison.Ordinal);
                if (end < 0) break;
                string content;
                try
                {
                    using var ms = new MemoryStream(pdf, start, end - start);
                    using var z = new ZLibStream(ms, CompressionMode.Decompress);
                    using var rd = new StreamReader(z, Encoding.Latin1);
                    content = rd.ReadToEnd();
                }
                catch (Exception) { continue; }   // images, fonts and anything else that is not a deflated content stream
                if (!content.Contains(" Tf")) continue;
                double size = 0, scale = 1; var stack = new Stack<double>();
                foreach (Match m in Op.Matches(content))
                {
                    if (m.Groups["n"].Success) size = double.Parse(m.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture);
                    else if (m.Groups["tm"].Success)
                    {
                        var p = m.Groups["tm"].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                        var a = double.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture); var b = double.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture);
                        scale = Math.Sqrt(a * a + b * b);
                        if (scale == 0) scale = 1;
                    }
                    else if (m.Groups["q"].Success) { if (m.Value == "q") stack.Push(scale); else if (stack.Count > 0) scale = stack.Pop(); }
                    else if (m.Groups["show"].Success && size > 0)
                    {
                        var eff = Math.Round(size * scale, 2);
                        result[eff] = result.GetValueOrDefault(eff) + 1;
                    }
                }
            }
            return result;
        }
    }
}
