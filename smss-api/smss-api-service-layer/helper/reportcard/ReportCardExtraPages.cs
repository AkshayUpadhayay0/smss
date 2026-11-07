using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace smss_api_service_layer.helper.reportcard
{
    /// <summary>The optional cover page and rules page (drawn in Step 6 of Stage 5a). They use the orientation of the chosen design so a booklet prints consistently.</summary>
    public static class ReportCardExtraPages
    {
        public static void Cover(IDocumentContainer doc, IReportCardTemplate template, RcContext ctx) { }
        public static void Rules(IDocumentContainer doc, IReportCardTemplate template, RcContext ctx) { }
    }
}
