using QuestPDF.Drawing;
using QuestPDF.Infrastructure;

namespace smss_api_service_layer.helper
{
    /// <summary>
    /// The one-time QuestPDF set-up shared by every PDF the application draws (ID cards, the attendance report, report cards): the Community licence, the missing-glyph rule and the
    /// embedded fonts. Call Ensure() before the first document; it does the work once per process and is safe from any thread.
    /// </summary>
    public static class PdfBootstrap
    {
        private static readonly object Gate = new();
        private static bool _done;

        public static void Ensure()
        {
            if (_done) return;
            lock (Gate)
            {
                if (_done) return;
                // Community licence: free for organisations under USD 1M annual gross revenue. Set once for the process.
                QuestPDF.Settings.License = LicenseType.Community;
                // A character no bundled font has (an emoji, a rare script) must not fail the whole PDF: it prints as a blank box instead.
                QuestPDF.Settings.ThrowOnMissingTextGlyphs = false;
                // Hind (Latin + Devanagari) and PT Serif ship inside the assembly, so the output does not depend on the fonts of the server.
                var asm = typeof(PdfBootstrap).Assembly;
                foreach (var name in asm.GetManifestResourceNames().Where(n => n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)))
                    FontManager.RegisterFontFromEmbeddedResource(asm, name);
                _done = true;
            }
        }
    }
}
