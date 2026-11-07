using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace smss_api_service_layer.helper.reportcard
{
    /// <summary>The bulk limit. Measured, not guessed: see Docs/report-card-performance.md (2.5x headroom over a usual 40-student section).</summary>
    public static class ReportCardBulk
    {
        public const int DefaultMaxStudentsPerRequest = 100;
        /// <summary>The configured cap (appsettings ReportCards:BulkMaxStudents, default 100). Set once at start-up from configuration; never below 1.</summary>
        public static int MaxStudentsPerRequest { get; set; } = DefaultMaxStudentsPerRequest;
    }

    /// <summary>Draws report cards (model + design) to PDF or PNG. The QuestPDF licence, glyph rule and fonts come from the shared PdfBootstrap.</summary>
    public static class ReportCardPdf
    {
        /// <summary>One student's pages, in order: the optional cover, the card itself, the optional rules page. The same call draws a single card and every card of a bulk file.</summary>
        private static void Compose(IDocumentContainer doc, RcContext ctx)
        {
            var template = ReportCardDesigns.Find(ctx.Model.TemplateKey) ?? throw new InvalidOperationException($"No report card design named '{ctx.Model.TemplateKey}'.");
            if (ctx.Model.ShowCover) ReportCardExtraPages.Cover(doc, template, ctx);
            template.Render(doc, ctx);
            if (ctx.Model.ShowRules && !string.IsNullOrWhiteSpace(ctx.Model.RulesText)) ReportCardExtraPages.Rules(doc, template, ctx);
        }

        private static Document Build(IEnumerable<RcContext> cards)
        {
            PdfBootstrap.Ensure();
            return Document.Create(doc => { foreach (var c in cards) Compose(doc, c); });
        }

        public static byte[] Render(RcContext ctx) => Build(new[] { ctx }).GeneratePdf();

        /// <summary>The number of students drawn per piece of a bulk PDF. A piece is laid out, written to disk and freed before the next one starts, so the memory of a bulk request does not grow
        /// with the number of students (a card with a full-size photo is several MB while it is being laid out).</summary>
        public const int BulkChunkSize = 20;

        /// <summary>
        /// Many cards in one PDF (a bulk file). The contexts are drawn lazily, <see cref="BulkChunkSize"/> students at a time: each piece is generated to a temporary file and released, then the
        /// pieces are merged in order. A bulk of up to one piece is simply one document. The temporary folder is always removed.
        /// </summary>
        public static byte[] RenderMany(IEnumerable<RcContext> cards, int chunkSize = BulkChunkSize)
        {
            var chunk = new List<RcContext>(chunkSize); var parts = new List<string>(); string? dir = null;
            void Spill()
            {
                dir ??= Directory.CreateTempSubdirectory("smss-rc-bulk-").FullName;
                var path = Path.Combine(dir, $"part{parts.Count:D3}.pdf");
                Build(chunk).GeneratePdf(path); parts.Add(path); chunk.Clear();
            }
            try
            {
                foreach (var c in cards) { chunk.Add(c); if (chunk.Count >= chunkSize) Spill(); }
                if (parts.Count == 0) return Build(chunk).GeneratePdf();
                if (chunk.Count > 0) Spill();
                var op = DocumentOperation.LoadFile(parts[0]);
                foreach (var p in parts.Skip(1)) op = op.MergeFile(p);
                var merged = Path.Combine(dir!, "merged.pdf"); op.Save(merged);
                return File.ReadAllBytes(merged);
            }
            finally { if (dir != null) { try { Directory.Delete(dir, true); } catch { /* a leftover temp folder is harmless */ } } }
        }

        /// <summary>The pages as PNG images (the settings preview).</summary>
        public static List<byte[]> RenderPng(RcContext ctx, int dpi = 110) =>
            Build(new[] { ctx }).GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = dpi }).ToList();
    }
}
