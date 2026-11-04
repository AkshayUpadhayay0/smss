using System.Globalization;
using System.Text.RegularExpressions;

namespace smss_api_service_layer.helper.idcard
{
    /// <summary>Validation of the brand colours a school picks (same pattern as the database check: #rrggbb).</summary>
    public static class IdCardColors
    {
        public static readonly Regex HexPattern = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);
        public static bool IsHex(string? value) => value != null && HexPattern.IsMatch(value);
    }

    /// <summary>
    /// Every colour a template needs, derived from the school's two brand colours. Text colours are chosen by luminance, so any brand colour
    /// (a near-black or a pastel) stays readable: <see cref="OnPrimary"/> is what to print ON the primary colour, <see cref="PrimaryText"/> is
    /// the primary darkened just enough to be read ON white.
    /// </summary>
    public sealed class IdCardPalette
    {
        public string Primary { get; }
        public string Secondary { get; }
        public string OnPrimary { get; }
        public string OnSecondary { get; }
        public string PrimaryDark { get; }
        public string PrimaryLight { get; }
        public string PrimaryTint { get; }
        public string SecondaryTint { get; }
        public string PrimaryText { get; }
        public string SecondaryText { get; }
        public string Ink => "#1F2430";
        public string Muted => "#5B6472";
        /// <summary>True when the primary is so pale that shapes in it need an outline to show on a white card.</summary>
        public bool PrimaryIsPale { get; }
        public bool SecondaryIsPale { get; }

        private IdCardPalette(string p, string s)
        {
            Primary = p; Secondary = s;
            OnPrimary = On(p); OnSecondary = On(s);
            PrimaryDark = Mix(p, "#000000", 0.28);
            PrimaryLight = Mix(p, "#FFFFFF", 0.35);
            PrimaryTint = Mix(p, "#FFFFFF", 0.90);
            SecondaryTint = Mix(s, "#FFFFFF", 0.88);
            PrimaryText = ReadableOnWhite(p);
            SecondaryText = ReadableOnWhite(s);
            PrimaryIsPale = Luminance(p) > 0.80;
            SecondaryIsPale = Luminance(s) > 0.80;
        }

        public static IdCardPalette From(string primary, string secondary) => new(Norm(primary), Norm(secondary));

        private static string Norm(string hex) => hex.ToUpperInvariant();

        private static (double r, double g, double b) Rgb(string hex) => (
            int.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber) / 255.0,
            int.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber) / 255.0,
            int.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber) / 255.0);

        private static string Hex(double r, double g, double b) =>
            $"#{(int)Math.Round(Math.Clamp(r, 0, 1) * 255):X2}{(int)Math.Round(Math.Clamp(g, 0, 1) * 255):X2}{(int)Math.Round(Math.Clamp(b, 0, 1) * 255):X2}";

        /// <summary>WCAG relative luminance, 0 (black) to 1 (white).</summary>
        public static double Luminance(string hex)
        {
            static double Lin(double c) => c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            var (r, g, b) = Rgb(hex);
            return 0.2126 * Lin(r) + 0.7152 * Lin(g) + 0.0722 * Lin(b);
        }

        public static double Contrast(string a, string b)
        {
            var la = Luminance(a); var lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        /// <summary>Mixes <paramref name="a"/> towards <paramref name="b"/> by t (0 = a, 1 = b).</summary>
        public static string Mix(string a, string b, double t)
        {
            var (ar, ag, ab) = Rgb(a); var (br, bg, bb) = Rgb(b);
            return Hex(ar + (br - ar) * t, ag + (bg - ag) * t, ab + (bb - ab) * t);
        }

        /// <summary>White or near-black, whichever reads better on <paramref name="background"/>.</summary>
        public static string On(string background) =>
            Contrast(background, "#FFFFFF") >= Contrast(background, "#111827") ? "#FFFFFF" : "#111827";

        /// <summary>The colour itself when it is readable on white, else darkened step by step until it is (contrast 4.5).</summary>
        public static string ReadableOnWhite(string hex)
        {
            var c = hex;
            for (var i = 0; i < 12 && Contrast(c, "#FFFFFF") < 4.5; i++) c = Mix(c, "#000000", 0.12);
            return c;
        }
    }
}
