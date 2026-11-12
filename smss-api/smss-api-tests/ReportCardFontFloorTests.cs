using QuestPDF.Fluent;
using smss_api_service_layer.helper.reportcard;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// No report card prints text smaller than 7 pt. The check reads the font size of every piece of text in the RENDERED PDF (see <see cref="PdfTextSizes"/>), so a computed size
    /// ("size - 1.5f"), a FitSize shrink or a new design is caught as well as a typo. A dense card must be fitted by spacing and shorter content, never by a smaller font.
    /// </summary>
    public class ReportCardFontFloorTests
    {
        private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
        private static readonly (string Design, string Mode, int Skills)[] Shapes =
        {
            ("term-spread", "MARKS", 0), ("classic-card", "MARKS", 0), ("minimal-bw", "MARKS", 0), ("board-style", "MARKS", 0),
            ("skill-grid", "GRADES", 1), ("skill-grid", "GRADES", 5), ("playful-report", "GRADES", 0),
            ("board-style", "GRADES", 0), ("board-style", "GRADES", 3), ("minimal-bw", "GRADES", 0), ("minimal-bw", "GRADES", 3),
        };

        public static IEnumerable<object[]> Matrix()
        {
            foreach (var (design, mode, skills) in Shapes)
                foreach (var terms in new[] { 1, 2, 3 })
                    foreach (var subjects in new[] { 6, 16 })
                        foreach (var text in new[] { "normal", "long", "hindi" })
                            yield return new object[] { design, mode, skills, terms, subjects, text };
        }

        private static RcContext Everything(string design, string mode, int skills, int terms, int subjects, string text)
        {
            var o = new RcSampleOptions
            {
                TemplateKey = design, Mode = mode, Skills = skills, Terms = terms, Subjects = subjects, LongNames = text == "long", Hindi = text == "hindi",
                Footer = "Sample footer line that is fairly long so that it has to shrink to fit in one line of the page footer, and then some more words",
                ShowCover = true, ShowRules = true, ShowAttendance = true, ShowGradeLegend = true, ShowRank = true, Fail = text == "long",
            };
            return new RcContext { Model = ReportCardSamples.Build(o), Logo = Png, Signature = Png, Draft = false };
        }

        [Theory]
        [MemberData(nameof(Matrix))]
        public void No_text_on_any_report_card_is_smaller_than_7_pt(string design, string mode, int skills, int terms, int subjects, string text)
        {
            var sizes = PdfTextSizes.Sizes(ReportCardPdf.Render(Everything(design, mode, skills, terms, subjects, text)));
            Assert.NotEmpty(sizes);   // the reader found the text at all (a PDF writer change must not turn this test into a silent pass)
            var smallest = sizes.Keys.Min();
            Assert.True(smallest >= RcKit.MinFontPt, $"{design}/{mode}/{skills} ({terms} terms, {subjects} subjects, {text}) prints text at {smallest} pt ({sizes[smallest]} runs): the floor is {RcKit.MinFontPt} pt.");
        }

        // ---- the two densest designs did not grow extra pages when their text came up to the floor (spacing and margins give way, not the font)
        [Theory]
        [InlineData("board-style", 1, 8, 1)] [InlineData("board-style", 2, 8, 1)] [InlineData("board-style", 3, 8, 1)]
        [InlineData("board-style", 1, 16, 1)] [InlineData("board-style", 2, 16, 1)] [InlineData("board-style", 3, 10, 1)]
        [InlineData("term-spread", 1, 8, 1)] [InlineData("term-spread", 2, 8, 1)] [InlineData("term-spread", 3, 8, 2)]
        [InlineData("term-spread", 1, 16, 1)] [InlineData("term-spread", 2, 16, 1)] [InlineData("term-spread", 3, 16, 2)]
        public void The_dense_designs_keep_their_page_count_at_the_floor(string design, int terms, int subjects, int pages)
        {
            var ctx = new RcContext { Model = ReportCardSamples.Build(new RcSampleOptions { TemplateKey = design, Terms = terms, Subjects = subjects, ShowPhoto = true }) };
            Assert.Equal(pages, ReportCardPdf.RenderPng(ctx).Count);
            Assert.True(PdfTextSizes.Sizes(ReportCardPdf.Render(ctx)).Keys.Min() >= RcKit.MinFontPt);
        }

        [Fact]
        public void The_floor_is_7_pt_and_Pt_and_FitSize_never_go_below_it()
        {
            Assert.Equal(7f, RcKit.MinFontPt);
            Assert.Equal(7f, RcKit.Pt(4.4f));
            Assert.Equal(9f, RcKit.Pt(9f));
            Assert.Equal(7f, RcKit.FitSize(new string('x', 400), 8f, 10, 3f));
            Assert.Equal(7f, RcKit.FitSize("short", 5.5f, 10, 4f));
        }

        [Fact]
        public void The_reader_does_see_a_size_below_the_floor_when_there_is_one()
        {
            // proves the check can fail: a tiny PDF drawn with 5 pt text
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
            var pdf = QuestPDF.Fluent.Document.Create(d => d.Page(p => { p.Size(QuestPDF.Helpers.PageSizes.A6); p.Content().Text("tiny").FontSize(5); })).GeneratePdf();
            Assert.Contains(5.0, PdfTextSizes.Sizes(pdf).Keys);
        }
    }
}
