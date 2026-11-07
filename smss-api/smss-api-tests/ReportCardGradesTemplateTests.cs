using smss_api_service_layer.helper;
using smss_api_service_layer.helper.reportcard;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// Stage 5b: the GRADES designs (skill-grid, playful-report, the grades variants of board-style and minimal-bw) and the new board-style MARKS layout. Every shape x one, two and three terms x short
    /// and 16-subject lists x ordinary, very long and Hindi text x dark, light and default colours, with and without a logo, signature, cover and rules page. A layout that overflowed or broke would
    /// throw while the page is laid out, so "it renders" is a real check; page counts, the compatibility rule and the monochrome promise are asserted as well.
    /// </summary>
    public class ReportCardGradesTemplateTests
    {
        private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
        private static readonly (string Primary, string Secondary)[] Palettes = { ("#1F3A5F", "#C8963E"), ("#05080F", "#101010"), ("#FFF59D", "#E6FFE6") };
        public static readonly (string Design, string Mode, int Skills)[] Shapes =
        {
            ("skill-grid", "GRADES", 1), ("skill-grid", "GRADES", 5), ("playful-report", "GRADES", 0), ("board-style", "GRADES", 0), ("board-style", "GRADES", 3),
            ("minimal-bw", "GRADES", 0), ("minimal-bw", "GRADES", 3), ("board-style", "MARKS", 0),
        };

        public static IEnumerable<object[]> Matrix()
        {
            foreach (var (design, mode, skills) in Shapes)
                foreach (var terms in new[] { 1, 2, 3 })
                    foreach (var subjects in new[] { 6, 16 })
                        foreach (var text in new[] { "normal", "long", "hindi" })
                            foreach (var pal in new[] { 0, 1, 2 })
                                yield return new object[] { design, mode, skills, terms, subjects, text, pal };
        }

        private static RcContext Context(string design, string mode, int skills, int terms, int subjects, string text, int pal, bool logo = false, bool signature = false, Action<RcSampleOptions>? tweak = null)
        {
            var o = new RcSampleOptions
            {
                TemplateKey = design, Mode = mode, Skills = skills, Terms = terms, Subjects = subjects, LongNames = text == "long", Hindi = text == "hindi",
                Primary = Palettes[pal].Primary, Secondary = Palettes[pal].Secondary
            };
            tweak?.Invoke(o);
            return new RcContext { Model = ReportCardSamples.Build(o), Logo = logo ? Png : null, Signature = signature ? Png : null };
        }

        [Theory]
        [MemberData(nameof(Matrix))]
        public void Every_design_renders_every_shape_without_overflow(string design, string mode, int skills, int terms, int subjects, string text, int pal)
        {
            var pdf = ReportCardPdf.Render(Context(design, mode, skills, terms, subjects, text, pal));
            Assert.True(pdf.Length > 2000);
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        }

        [Fact]
        public void Logo_signature_cover_and_rules_do_not_break_any_shape_and_each_adds_its_page()
        {
            foreach (var (design, mode, skills) in Shapes)
            {
                var plain = ReportCardPdf.RenderPng(Context(design, mode, skills, 2, 8, "long", 1)).Count;
                var full = ReportCardPdf.RenderPng(Context(design, mode, skills, 2, 8, "long", 1, logo: true, signature: true, tweak: o => { o.ShowCover = true; o.ShowRules = true; })).Count;
                Assert.Equal(plain + 2, full);
                Assert.True(ReportCardPdf.Render(Context(design, mode, skills, 3, 16, "hindi", 2, logo: true, signature: true, tweak: o => { o.ShowCover = true; o.ShowRules = true; })).Length > 3000);
            }
        }

        [Theory]
        [InlineData(1)] [InlineData(2)] [InlineData(3)]
        public void Playful_report_is_one_page_per_term_for_a_normal_list_and_flows_on_for_a_long_one(int terms)
        {
            Assert.Equal(terms, ReportCardPdf.RenderPng(Context("playful-report", "GRADES", 0, terms, 7, "normal", 0)).Count);
            Assert.True(ReportCardPdf.RenderPng(Context("playful-report", "GRADES", 0, terms, 16, "long", 0)).Count > terms);
        }

        [Fact]
        public void Skill_grid_fits_four_subjects_of_five_skills_and_two_terms_on_one_landscape_sheet_and_splits_a_wide_matrix_into_more_tables()
        {
            Assert.Equal(1, ReportCardPdf.RenderPng(Context("skill-grid", "GRADES", 5, 2, 4, "normal", 0)).Count);
            Assert.True(ReportCardPdf.RenderPng(Context("skill-grid", "GRADES", 5, 3, 16, "long", 0)).Count >= 2);
        }

        [Fact]
        public void The_grades_layouts_fit_one_page_for_a_usual_card()
        {
            Assert.Equal(1, ReportCardPdf.RenderPng(Context("board-style", "GRADES", 3, 2, 6, "normal", 0)).Count);
            Assert.Equal(1, ReportCardPdf.RenderPng(Context("minimal-bw", "GRADES", 3, 2, 6, "normal", 0)).Count);
        }

        [Fact]
        public void Minimal_is_monochrome_in_both_modes_the_brand_colours_never_change_a_pixel()
        {
            foreach (var (mode, skills) in new[] { ("MARKS", 0), ("GRADES", 3), ("GRADES", 0) })
            {
                var a = ReportCardPdf.RenderPng(Context("minimal-bw", mode, skills, 2, 8, "normal", 0));
                var b = ReportCardPdf.RenderPng(Context("minimal-bw", mode, skills, 2, 8, "normal", 2));
                Assert.Equal(a.Count, b.Count);
                for (var i = 0; i < a.Count; i++) Assert.Equal(a[i], b[i]);
            }
        }

        [Fact]
        public void The_coloured_designs_do_follow_the_brand_colours()
        {
            foreach (var (design, mode, skills) in new[] { ("skill-grid", "GRADES", 5), ("playful-report", "GRADES", 0), ("board-style", "GRADES", 3), ("board-style", "MARKS", 0) })
                Assert.NotEqual(ReportCardPdf.RenderPng(Context(design, mode, skills, 2, 6, "normal", 0))[0], ReportCardPdf.RenderPng(Context(design, mode, skills, 2, 6, "normal", 2))[0]);
        }

        private static bool Differs(RcContext a, RcContext b)
        {
            var x = ReportCardPdf.RenderPng(a); var y = ReportCardPdf.RenderPng(b);
            return x.Count != y.Count || x.Zip(y).Any(pair => !pair.First.SequenceEqual(pair.Second));
        }

        [Fact]
        public void The_settings_toggles_change_the_new_designs()
        {
            // every sheet of the card is compared: the attendance of a long board card sits on its second sheet
            foreach (var (design, mode, skills) in Shapes)
            {
                var all = Context(design, mode, skills, 2, 7, "normal", 0);
                Assert.True(Differs(all, Context(design, mode, skills, 2, 7, "normal", 0, tweak: o => o.ShowGradeLegend = false)), $"{design} {mode}: legend toggle");
                Assert.True(Differs(all, Context(design, mode, skills, 2, 7, "normal", 0, tweak: o => o.Footer = null)), $"{design} {mode}: footer");
                Assert.True(Differs(all, Context(design, mode, skills, 2, 7, "normal", 0, tweak: o => o.ShowAttendance = false)), $"{design} {mode}: attendance toggle");
            }
        }

        [Fact]
        public void A_subject_that_is_not_counted_in_the_total_is_still_printed_by_every_design()
        {
            // found on real data: the demo GRADES schemes have "include in total" off, and the first versions of the new designs silently dropped such subjects
            foreach (var (design, mode, skills) in Shapes)
            {
                var kept = Context(design, mode, skills, 2, 6, "normal", 0); kept.Model.Subjects[0].Included = false;
                var removed = Context(design, mode, skills, 2, 6, "normal", 0); removed.Model.Subjects.RemoveAt(0);
                Assert.True(Differs(kept, removed), $"{design} {mode}: the not-counted subject disappeared");
            }
        }

        // ---- the sample card is a real GRADES card: nothing numeric, rank by hand, promotion filled
        [Fact]
        public void A_GRADES_sample_has_no_marks_percentage_or_automatic_rank_and_carries_the_hand_written_parts()
        {
            var m = ReportCardSamples.Build(new RcSampleOptions { TemplateKey = "skill-grid", Mode = "GRADES", Skills = 5, Terms = 2, Subjects = 4 });
            Assert.Equal("GRADES", m.Mode);
            Assert.All(m.Subjects, s => Assert.All(s.Terms, t => { Assert.Null(t.Percent); Assert.Equal(0, t.OutOf); Assert.All(t.Cells, c => Assert.Equal(5, c.Skills.Count)); }));
            Assert.All(m.TermSummaries, t => { Assert.Null(t.Percent); Assert.Null(t.Rank); Assert.False(string.IsNullOrWhiteSpace(t.RankText)); Assert.NotEmpty(t.Activities); });
            Assert.Null(m.Overall.Percent);
            Assert.Equal("PROMOTED", m.Promotion.Decision);
            Assert.All(m.Legend, b => Assert.Null(b.Min));
            var d = ReportCardSamples.Build(new RcSampleOptions { TemplateKey = "playful-report", Mode = "GRADES", Skills = 0, Terms = 2, Subjects = 7 });
            Assert.False(string.IsNullOrWhiteSpace(d.Student.ClassTeacher));
            Assert.All(d.Subjects, s => Assert.All(s.Terms, t => Assert.All(t.Cells, c => Assert.Empty(c.Skills))));
            Assert.Contains(d.Subjects.SelectMany(s => s.Terms), t => !string.IsNullOrWhiteSpace(t.Improvement));
        }

        // ---- a scheme can only select a design that fits its mode; the refusal says what to choose
        [Theory]
        [InlineData("skill-grid", "MARKS", "Skill Grid", "Term Spread")]
        [InlineData("playful-report", "MARKS", "Playful Report", "Board Style")]
        [InlineData("term-spread", "GRADES", "Term Spread", "Skill Grid")]
        [InlineData("classic-card", "GRADES", "Classic Card", "Playful Report")]
        public void An_incompatible_design_is_refused_with_a_message_that_names_the_design_and_the_ones_that_fit(string design, string mode, string mustName, string mustOffer)
        {
            var msg = ReportCardTemplates.Incompatible(ReportCardTemplates.Find(design), mode);
            Assert.Contains(mustName, msg); Assert.Contains(mustOffer, msg);
            Assert.DoesNotContain("not available yet", msg);
        }

        [Fact]
        public void The_compatibility_table_is_the_one_the_task_set()
        {
            string[] Modes(string key) => ReportCardTemplates.Find(key)!.Modes;
            Assert.Equal(new[] { "MARKS" }, Modes("classic-card")); Assert.Equal(new[] { "MARKS" }, Modes("term-spread"));
            Assert.Equal(new[] { "MARKS", "GRADES" }, Modes("minimal-bw").OrderByDescending(x => x)); Assert.Equal(new[] { "GRADES" }, Modes("skill-grid"));
            Assert.Equal(new[] { "GRADES" }, Modes("playful-report")); Assert.Equal(new[] { "MARKS", "GRADES" }, Modes("board-style").OrderByDescending(x => x));
            Assert.All(ReportCardTemplates.All, t => Assert.True(t.Implemented));
            Assert.Equal(6, ReportCardDesigns.All.Count);
        }
    }
}
