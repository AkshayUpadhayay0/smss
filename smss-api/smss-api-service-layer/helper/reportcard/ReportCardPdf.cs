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

        /// <summary>Many cards in one document (a bulk PDF). The contexts are drawn lazily one after the other, so only one student's images are held at a time.</summary>
        public static byte[] RenderMany(IEnumerable<RcContext> cards) => Build(cards).GeneratePdf();

        /// <summary>The pages as PNG images (the settings preview).</summary>
        public static List<byte[]> RenderPng(RcContext ctx, int dpi = 110) =>
            Build(new[] { ctx }).GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = dpi }).ToList();
    }
}
