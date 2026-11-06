using QuestPDF.Fluent;

namespace smss_api_service_layer.helper.reportcard
{
    /// <summary>Draws one report card (model + template) to a PDF. The QuestPDF licence, glyph rule and fonts come from the shared PdfBootstrap.</summary>
    public static class ReportCardPdf
    {
        public static byte[] Render(RcContext ctx)
        {
            PdfBootstrap.Ensure();
            var template = ReportCardDesigns.Find(ctx.Model.TemplateKey) ?? throw new InvalidOperationException($"No report card design named '{ctx.Model.TemplateKey}'.");
            return Document.Create(doc => template.Render(doc, ctx)).GeneratePdf();
        }
    }
}
