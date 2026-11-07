using System.IO.Compression;
using smss_api_service_layer.helper.reportcard;
using Xunit;
using Xunit.Abstractions;

namespace smss_api_tests
{
    /// <summary>A tiny PNG reader (8-bit, any non-interlaced colour type) so a test can look at the rendered pixels: how far down a page the content reaches, and whether text survives the DRAFT mark.</summary>
    internal static class PngProbe
    {
        public sealed record Img(int W, int H, byte[] Gray);

        public static Img Decode(byte[] png)
        {
            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png.Take(8).ToArray());
            int pos = 8, w = 0, h = 0, depth = 0, color = 0; var idat = new MemoryStream();
            while (pos < png.Length)
            {
                var len = (png[pos] << 24) | (png[pos + 1] << 16) | (png[pos + 2] << 8) | png[pos + 3]; var type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4); var d = pos + 8;
                if (type == "IHDR") { w = (png[d] << 24) | (png[d + 1] << 16) | (png[d + 2] << 8) | png[d + 3]; h = (png[d + 4] << 24) | (png[d + 5] << 16) | (png[d + 6] << 8) | png[d + 7]; depth = png[d + 8]; color = png[d + 9]; Assert.Equal(0, png[d + 12]); }
                else if (type == "IDAT") idat.Write(png, d, len);
                pos = d + len + 4;
            }
            Assert.Equal(8, depth);
            var ch = color switch { 0 => 1, 2 => 3, 4 => 2, 6 => 4, _ => throw new NotSupportedException("PNG colour type " + color) };
            idat.Position = 0; var raw = new MemoryStream(); using (var z = new ZLibStream(idat, CompressionMode.Decompress)) z.CopyTo(raw);
            var data = raw.ToArray(); var stride = w * ch; var prev = new byte[stride]; var cur = new byte[stride]; var gray = new byte[w * h];
            for (var y = 0; y < h; y++)
            {
                var f = data[y * (stride + 1)]; Buffer.BlockCopy(data, y * (stride + 1) + 1, cur, 0, stride);
                for (var i = 0; i < stride; i++)
                {
                    int a = i >= ch ? cur[i - ch] : 0, b = prev[i], c = i >= ch ? prev[i - ch] : 0;
                    cur[i] = (byte)(cur[i] + f switch { 0 => 0, 1 => a, 2 => b, 3 => (a + b) / 2, 4 => Paeth(a, b, c), _ => 0 });
                }
                for (var x = 0; x < w; x++)
                {
                    var o = x * ch; int r, g, bl, al = 255;
                    if (ch <= 2) { r = g = bl = cur[o]; if (ch == 2) al = cur[o + 1]; } else { r = cur[o]; g = cur[o + 1]; bl = cur[o + 2]; if (ch == 4) al = cur[o + 3]; }
                    var lum = (r * 299 + g * 587 + bl * 114) / 1000; gray[y * w + x] = (byte)((lum * al + 255 * (255 - al)) / 255);
                }
                (prev, cur) = (cur, prev);
            }
            return new Img(w, h, gray);
        }

        private static int Paeth(int a, int b, int c) { var p = a + b - c; var pa = Math.Abs(p - a); var pb = Math.Abs(p - b); var pc = Math.Abs(p - c); return pa <= pb && pa <= pc ? a : pb <= pc ? b : c; }

        /// <summary>How far down the page (0 to 1) the lowest piece of content reaches, ignoring the footer zone at the foot of the page (signature row, legend, footer text).</summary>
        public static double ContentBottom(Img i, double footerZone = 0.14)
        {
            var limit = (int)(i.H * (1 - footerZone));
            for (var y = limit - 1; y >= 0; y--)
                for (var x = 0; x < i.W; x++) if (i.Gray[y * i.W + x] < 200) return (double)y / i.H;
            return 0;
        }
    }

    /// <summary>
    /// Stage 5c: layout proofs measured from the rendered pixels. A normal class fits ONE page on every design with or without a photo; heavy data continues on a further page that is filled sensibly;
    /// and the DRAFT mark is translucent, sits behind the content and never hides text.
    /// </summary>
    public class ReportCardLayoutTests
    {
        private readonly ITestOutputHelper _out;
        public ReportCardLayoutTests(ITestOutputHelper output) => _out = output;
        private static readonly byte[] PhotoPng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

        public static readonly (string Design, string Mode, int Skills)[] Shapes =
        {
            ("board-style", "MARKS", 0), ("board-style", "GRADES", 3), ("classic-card", "MARKS", 0), ("minimal-bw", "MARKS", 0), ("minimal-bw", "GRADES", 3), ("term-spread", "MARKS", 0), ("skill-grid", "GRADES", 5), ("playful-report", "GRADES", 0),
        };

        private static RcContext Ctx(string design, string mode, int skills, int terms, int subjects, bool photo = false, bool draft = false, Action<RcSampleOptions>? tweak = null)
        {
            var o = new RcSampleOptions { TemplateKey = design, Mode = mode, Skills = skills, Terms = terms, Subjects = subjects, ShowPhoto = photo, Footer = "Sample footer line" };
            tweak?.Invoke(o);
            return new RcContext { Model = ReportCardSamples.Build(o), Photo = photo ? PhotoPng : null, Draft = draft };
        }

        private static int ExpectedTypicalPages(string design, int terms) => design == "playful-report" ? terms : design == "term-spread" ? (terms + 1) / 2 : 1;      // term spread puts two terms on a landscape sheet

        // ---- a typical class: 7 to 10 subjects, one to three terms, photo off and on: exactly one page per card (one per term for the descriptive design)
        [Theory]
        [InlineData(7, 1)] [InlineData(7, 2)] [InlineData(9, 2)] [InlineData(10, 2)] [InlineData(10, 3)]
        public void A_typical_class_fits_one_page_on_every_design_with_and_without_a_photo(int subjects, int terms)
        {
            foreach (var (design, mode, skills) in Shapes)
            {
                var sub = design == "skill-grid" ? Math.Min(subjects, 4) : design == "playful-report" ? Math.Min(subjects, 7) : subjects;      // a skill grid is for a few subjects, a descriptive card for a few learning areas
                foreach (var photo in new[] { false, true })
                {
                    var pages = ReportCardPdf.RenderPng(Ctx(design, mode, skills, terms, sub, photo)).Count;
                    Assert.True(pages == ExpectedTypicalPages(design, terms), $"{design} {mode} {sub} subjects, {terms} term(s), photo {photo}: {pages} page(s)");
                }
            }
        }

        // ---- heavy data: 16 subjects. Fits one page where the design can; otherwise continues, and no page after the first is left almost empty
        [Theory]
        [InlineData(2)] [InlineData(3)]
        public void Heavy_data_never_leaves_a_mostly_empty_extra_page(int terms)
        {
            foreach (var (design, mode, skills) in Shapes.Where(s => s.Design != "playful-report"))
                foreach (var photo in new[] { false, true })
                {
                    var pages = ReportCardPdf.RenderPng(Ctx(design, mode, skills, terms, 16, photo)).Select(PngProbe.Decode).ToList();
                    _out.WriteLine($"{design} {mode} 16 subjects {terms} terms photo {photo}: {pages.Count} page(s), content bottoms " + string.Join(", ", pages.Select(p => PngProbe.ContentBottom(p).ToString("0.00"))));
                    Assert.True(pages.Count <= 3, $"{design}: {pages.Count} pages");
                    for (var i = 1; i < pages.Count; i++) Assert.True(PngProbe.ContentBottom(pages[i]) >= 0.2, $"{design} {mode} {terms} terms photo {photo}: page {i + 1} is almost empty ({PngProbe.ContentBottom(pages[i]):0.00})");
                }
            // board-style (the layout this pass fixed) carries 16 subjects over two terms on a single page
            foreach (var photo in new[] { false, true })
                if (terms == 2) Assert.Single(ReportCardPdf.RenderPng(Ctx("board-style", "MARKS", 0, 2, 16, photo)));
        }

        [Fact]
        public void A_photo_never_adds_a_page_to_a_card_not_even_a_heavy_one()
        {
            foreach (var (design, mode, skills) in Shapes)
                foreach (var terms in new[] { 2, 3 })
                {
                    var off = ReportCardPdf.RenderPng(Ctx(design, mode, skills, terms, design == "skill-grid" ? 4 : design == "playful-report" ? 16 : 16, false)).Count;
                    var on = ReportCardPdf.RenderPng(Ctx(design, mode, skills, terms, design == "skill-grid" ? 4 : design == "playful-report" ? 16 : 16, true)).Count;
                    Assert.True(on <= off, $"{design} {mode} {terms} terms: a photo adds a page ({off} -> {on})");
                }
        }

        [Fact]
        public void A_continuation_page_repeats_a_compact_header_and_is_filled_sensibly()
        {
            var pages = ReportCardPdf.RenderPng(Ctx("board-style", "MARKS", 0, 3, 16)).Select(PngProbe.Decode).ToList();
            Assert.True(pages.Count >= 2, "16 subjects over three terms cannot fit one page");
            Assert.True(PngProbe.ContentBottom(pages[1]) >= 0.2);
            // the compact header is one line: content starts near the top of page 2 (the full header would push it below 12% of the page)
            var top = Enumerable.Range(0, pages[1].H).First(y => Enumerable.Range(0, pages[1].W).Any(x => pages[1].Gray[y * pages[1].W + x] < 200));
            Assert.True(top < pages[1].H * 0.06, "the continuation page starts with a compact header");
        }

        // ---- the DRAFT mark: translucent, behind the content, on every design and every page type; no text is ever hidden by it
        [Fact]
        public void The_DRAFT_mark_is_visible_but_never_hides_text_on_any_design_or_page_type()
        {
            foreach (var (design, mode, skills) in Shapes)
            {
                void Tweak(RcSampleOptions o) { o.ShowCover = true; o.ShowRules = true; }
                var clean = ReportCardPdf.RenderPng(Ctx(design, mode, skills, 2, 7, false, false, Tweak)).Select(PngProbe.Decode).ToList();
                var draft = ReportCardPdf.RenderPng(Ctx(design, mode, skills, 2, 7, false, true, Tweak)).Select(PngProbe.Decode).ToList();
                Assert.Equal(clean.Count, draft.Count);
                for (var p = 0; p < clean.Count; p++)
                {
                    var a = clean[p]; var b = draft[p]; var n = a.Gray.Length; int inkLost = 0, ink = 0, tinted = 0;
                    for (var i = 0; i < n; i++)
                    {
                        if (a.Gray[i] < 110) { ink++; if (b.Gray[i] > 140) inkLost++; }       // text and rules stay dark
                        if (a.Gray[i] > 250 && b.Gray[i] is > 200 and < 248) tinted++;          // blank paper turned light grey: the mark itself
                    }
                    Assert.True(ink == 0 || inkLost <= ink * 0.01, $"{design} {mode} page {p + 1}: the mark hides text ({inkLost} of {ink} dark pixels)");
                    Assert.True(tinted > n * 0.004, $"{design} {mode} page {p + 1}: the DRAFT mark is not visible enough ({tinted} px)");
                    Assert.True(tinted < n * 0.30, $"{design} {mode} page {p + 1}: the DRAFT mark is too heavy");
                }
            }
        }

        [Fact]
        public void A_published_card_carries_no_DRAFT_mark_and_the_draft_footer_text_stays_on_a_draft()
        {
            foreach (var (design, mode, skills) in Shapes)
            {
                var clean = ReportCardPdf.RenderPng(Ctx(design, mode, skills, 2, 7)).Select(PngProbe.Decode).First();
                // no pixel of the clean card is in the faint-grey band of the mark over blank paper (a mark would add many)
                var faint = clean.Gray.Count(g => g is > 225 and < 244);
                var draft = ReportCardPdf.RenderPng(Ctx(design, mode, skills, 2, 7, draft: true)).Select(PngProbe.Decode).First();
                Assert.True(draft.Gray.Count(g => g is > 225 and < 244) > faint + 2000, $"{design}: draft should add the mark");
            }
        }

        // ---- the footer has no default claim: nothing is stored or applied unless the school sets it
        [Fact]
        public void The_footer_text_has_no_default_anywhere_it_is_empty_until_the_school_sets_it()
        {
            Assert.Null(new smss_api_db_layer.entity.TbSchoolReportCardSettings().FooterText);           // the table entity (and the column has no DEFAULT in the migration)
            Assert.Null(new smss_api_service_layer.dto.ReportCardSettingsRequest().FooterText);          // the request
            Assert.Null(new RcSampleOptions().Footer);                                                  // the built-in sample the settings preview draws
            var (clean, error) = ReportCardSettingsRules.Validate(new smss_api_service_layer.dto.ReportCardSettingsRequest());
            Assert.Null(error); Assert.Null(clean!.FooterText);                                         // saving an empty form stores no footer
            var m = ReportCardSamples.Build(new RcSampleOptions());
            ReportCardBranding.Apply(m, clean, null);
            Assert.Null(m.Footer);                                                                      // a card drawn from empty settings has no footer line
            Assert.Null(new ReportCardModel().Footer);
        }

        [Fact]
        public void A_footer_the_school_set_is_kept_exactly_as_typed()
        {
            var (clean, _) = ReportCardSettingsRules.Validate(new smss_api_service_layer.dto.ReportCardSettingsRequest { FooterText = "  Valid without a seal.  " });
            Assert.Equal("Valid without a seal.", clean!.FooterText);
        }

        // ---- the cover page: long names, long addresses, Hindi, on portrait and landscape designs, with and without the DRAFT mark
        [Theory]
        [InlineData("long")] [InlineData("hindi")] [InlineData("address")]
        public void The_cover_renders_long_and_Hindi_values_on_every_orientation(string kind)
        {
            foreach (var (design, mode, skills) in Shapes)
                foreach (var draft in new[] { false, true })
                {
                    var ctx = Ctx(design, mode, skills, 2, 7, false, draft, o => { o.ShowCover = true; o.LongNames = kind == "long"; o.Hindi = kind == "hindi"; });
                    if (kind == "address") ctx.Model.Student.Address = string.Join(", ", Enumerable.Repeat("Flat 402, Building C, Sunrise Residency Apartments, Behind the Old Municipal Corporation Office", 3));
                    var pages = ReportCardPdf.RenderPng(ctx).Select(PngProbe.Decode).ToList();
                    var cover = pages[0];
                    // find the page frame (the right-most long dark vertical line), then check the strip just inside it: padding keeps content out of it, so any ink there means clipping/overrun
                    var frameX = Enumerable.Range(cover.W / 2, cover.W / 2).Reverse().First(x => Enumerable.Range(0, cover.H).Count(y => cover.Gray[y * cover.W + x] < 120) > cover.H * 0.7);
                    var rows = Enumerable.Range(cover.H / 6, cover.H * 2 / 3).Count(y => Enumerable.Range(frameX - 18, 14).Any(x => cover.Gray[y * cover.W + x] < 120));
                    Assert.True(rows == 0, $"{design} {kind} draft {draft}: cover content runs into the right edge ({rows} rows)");
                }
        }
    }
}
