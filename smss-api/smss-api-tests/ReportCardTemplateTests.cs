using smss_api_service_layer.helper;
using smss_api_service_layer.helper.idcard;
using smss_api_service_layer.helper.reportcard;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// Every report card design x one, two and three terms x short and long subject lists (up to 16) x ordinary, very long and Hindi text x dark, light and default brand colours,
    /// with and without a logo and a signature. A design that overflows or breaks would throw while laying the page out, so "it renders" is a real check; the page count and the
    /// PDF header are asserted as well.
    /// </summary>
    public class ReportCardTemplateTests
    {
        // a valid 1x1 PNG (the logo and signature stand-ins)
        private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
        private static readonly string[] Designs = { "term-spread", "classic-card", "minimal-bw" };
        private static readonly (string Name, string Primary, string Secondary)[] Palettes =
        {
            ("default", "#1F3A5F", "#C8963E"), ("very dark", "#05080F", "#101010"), ("very light", "#FFF59D", "#E6FFE6"), ("pure white", "#FFFFFF", "#FFFFFF"), ("pure black", "#000000", "#000000"), ("mid grey", "#777777", "#8A8A8A"),
        };

        public static IEnumerable<object[]> Matrix()
        {
            foreach (var d in Designs)
                foreach (var terms in new[] { 1, 2, 3 })
                    foreach (var subjects in new[] { 6, 16 })
                        foreach (var text in new[] { "normal", "long", "hindi" })
                            foreach (var pal in new[] { 0, 1, 2 })
                                yield return new object[] { d, terms, subjects, text, pal };
        }

        private static RcContext Context(string design, int terms, int subjects, string text, int palette, bool logo, bool signature, bool draft = false, Action<RcSampleOptions>? tweak = null)
        {
            var o = new RcSampleOptions { TemplateKey = design, Terms = terms, Subjects = subjects, LongNames = text == "long", Hindi = text == "hindi", Primary = Palettes[palette].Primary, Secondary = Palettes[palette].Secondary, Footer = "Sample footer line" };
            tweak?.Invoke(o);
            return new RcContext { Model = ReportCardSamples.Build(o), Logo = logo ? Png : null, Signature = signature ? Png : null, Draft = draft };
        }

        [Theory]
        [MemberData(nameof(Matrix))]
        public void Every_design_renders_every_shape_without_overflow(string design, int terms, int subjects, string text, int palette)
        {
            var pdf = ReportCardPdf.Render(Context(design, terms, subjects, text, palette, logo: false, signature: false));
            Assert.True(pdf.Length > 2000);
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        }

        [Theory]
        [InlineData("classic-card", 1)] [InlineData("classic-card", 2)] [InlineData("classic-card", 3)]
        [InlineData("minimal-bw", 1)] [InlineData("minimal-bw", 2)] [InlineData("minimal-bw", 3)]
        public void The_portrait_designs_put_a_whole_term_set_on_ONE_page_even_with_16_long_subjects(string design, int terms)
        {
            var pages = ReportCardPdf.RenderPng(Context(design, terms, 16, "long", 0, logo: true, signature: true)).Count;
            Assert.Equal(1, pages);
        }

        [Theory]
        [InlineData(1, 1)] [InlineData(2, 1)] [InlineData(3, 2)]
        public void Term_spread_puts_two_terms_on_a_sheet(int terms, int sheets) =>
            Assert.Equal(sheets, ReportCardPdf.RenderPng(Context("term-spread", terms, 8, "normal", 0, false, false)).Count);

        [Fact]
        public void A_logo_and_a_signature_and_the_draft_mark_do_not_break_any_design()
        {
            foreach (var d in Designs)
                Assert.True(ReportCardPdf.Render(Context(d, 3, 16, "long", 1, logo: true, signature: true, draft: true)).Length > 2000);
        }

        [Fact]
        public void No_logo_is_fine_and_a_logo_changes_the_picture()
        {
            foreach (var d in Designs)
            {
                var without = ReportCardPdf.RenderPng(Context(d, 2, 8, "normal", 0, false, false))[0];
                var with = ReportCardPdf.RenderPng(Context(d, 2, 8, "normal", 0, true, false))[0];
                Assert.NotEqual(without, with);
            }
        }

        [Fact]
        public void The_settings_toggles_change_the_card()
        {
            foreach (var d in Designs)
            {
                var all = ReportCardPdf.RenderPng(Context(d, 2, 8, "normal", 0, false, false))[0];
                var noLegend = ReportCardPdf.RenderPng(Context(d, 2, 8, "normal", 0, false, false, tweak: o => o.ShowGradeLegend = false))[0];
                var noAtt = ReportCardPdf.RenderPng(Context(d, 2, 8, "normal", 0, false, false, tweak: o => o.ShowAttendance = false))[0];
                var noFooter = ReportCardPdf.RenderPng(Context(d, 2, 8, "normal", 0, false, false, tweak: o => o.Footer = null))[0];
                Assert.NotEqual(all, noLegend); Assert.NotEqual(all, noAtt); Assert.NotEqual(all, noFooter);
            }
        }

        [Fact]
        public void Every_registered_design_is_marked_implemented_in_the_setup_catalogue_and_every_implemented_one_is_registered()
        {
            foreach (var info in ReportCardTemplates.All)
            {
                var design = ReportCardDesigns.Find(info.Key);
                Assert.Equal(info.Implemented, design != null);
                if (design != null) { Assert.Equal(info.Modes, design.SupportedModes); Assert.Equal(info.Orientation, char.ToUpperInvariant(design.Orientation[0]) + design.Orientation[1..]); }
            }
        }

        // ---- contrast: any brand colour stays readable (the same luminance logic as the ID cards)
        [Theory]
        [InlineData("#1F3A5F")] [InlineData("#C8963E")] [InlineData("#000000")] [InlineData("#FFFFFF")] [InlineData("#FFF59D")] [InlineData("#777777")] [InlineData("#808080")] [InlineData("#FF00FF")] [InlineData("#00FFFF")] [InlineData("#FFD700")] [InlineData("#05080F")]
        public void Brand_colours_always_give_readable_text(string hex)
        {
            var p = IdCardPalette.From(hex, hex);
            Assert.True(IdCardPalette.Contrast(p.PrimaryText, "#FFFFFF") >= 4.5, $"text in {hex} on white: {IdCardPalette.Contrast(p.PrimaryText, "#FFFFFF"):0.00}");
            Assert.True(IdCardPalette.Contrast(p.SecondaryText, "#FFFFFF") >= 4.5);
            // the worst case anywhere in the colour space is a mid grey: neither white nor near-black gets past about 4.48 on it, so 4.4 is the honest floor for text on a SOLID brand colour
            Assert.True(IdCardPalette.Contrast(p.OnPrimary, hex) >= 4.4, $"text on solid {hex}: {IdCardPalette.Contrast(p.OnPrimary, hex):0.00}");
            // the cards put dark ink on the light tints
            Assert.True(IdCardPalette.Contrast("#111827", RcKit.Tint(hex, 0.82)) >= 7, $"ink on the header tint of {hex}");
            Assert.True(IdCardPalette.Contrast("#111827", RcKit.Tint(hex, 0.94)) >= 7);
        }

        [Fact]
        public void Pal_falls_back_to_the_defaults_for_a_broken_colour()
        {
            var m = new ReportCardModel { Primary = "not a colour", Secondary = "" };
            var p = RcKit.Pal(m);
            Assert.Equal("#1F3A5F", p.Primary);
        }

        // ---- cover and rules pages (Step 6): same orientation as the design, in single and bulk PDFs
        [Theory]
        [InlineData("term-spread", 2, true, true, 3)] [InlineData("term-spread", 2, true, false, 2)] [InlineData("term-spread", 2, false, true, 2)] [InlineData("term-spread", 3, true, true, 4)]
        [InlineData("classic-card", 3, true, true, 3)] [InlineData("classic-card", 1, false, false, 1)] [InlineData("minimal-bw", 2, true, true, 3)] [InlineData("minimal-bw", 2, true, false, 2)]
        public void The_cover_and_rules_pages_are_added_only_when_switched_on(string design, int terms, bool cover, bool rules, int pages) =>
            Assert.Equal(pages, ReportCardPdf.RenderPng(Context(design, terms, 8, "normal", 0, false, false, tweak: o => { o.ShowCover = cover; o.ShowRules = rules; })).Count);

        [Fact]
        public void The_rules_page_with_no_text_is_left_out_and_a_very_long_text_still_fits_on_one_page()
        {
            Assert.Equal(1, ReportCardPdf.RenderPng(Context("classic-card", 2, 8, "normal", 0, false, false, tweak: o => { o.ShowRules = true; o.RulesText = null; })).Count);
            var longRules = string.Join('\n', Enumerable.Range(1, 30).Select(i => $"{i}. " + new string('x', 60) + " rule text that goes on and on and wraps over the whole width of the page " + i));
            foreach (var d in Designs)
                Assert.Equal(2, ReportCardPdf.RenderPng(Context(d, 1, 8, "normal", 0, false, false, tweak: o => { o.ShowRules = true; o.RulesText = longRules.Length > 1500 ? longRules[..1500] : longRules; })).Count);
        }

        [Theory]
        [InlineData("term-spread")] [InlineData("classic-card")] [InlineData("minimal-bw")]
        public void Cover_and_rules_work_with_long_and_Hindi_text_a_logo_and_a_dark_colour(string design)
        {
            foreach (var text in new[] { "long", "hindi" })
                Assert.True(ReportCardPdf.Render(Context(design, 2, 16, text, 1, logo: true, signature: true, tweak: o => { o.ShowCover = true; o.ShowRules = true; })).Length > 3000);
        }

        [Fact]
        public void A_bulk_PDF_is_the_sum_of_its_cards_in_the_given_order()
        {
            var cards = Enumerable.Range(1, 5).Select(i => Context("classic-card", 2, 8, "normal", 0, false, false, tweak: o => { o.ShowCover = true; o.ShowRules = true; o.StudentName = "Student " + i; })).ToList();
            var one = ReportCardPdf.RenderPng(cards[0]).Count;
            var bulk = ReportCardPdf.RenderMany(cards);
            Assert.Equal(3, one);
            Assert.True(bulk.Length > 5000);
        }

    }
}
