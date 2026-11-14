using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.helper.idcard;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// The school logo on the ID card: present / absent / missing file / corrupt image on every design, never stretched or cropped, inside its box, never over the QR code or the student
    /// photo; one renderer for the single PDF, the bulk sheets and the preview; the logo read once per run; one bad logo never breaks a bulk run.
    /// </summary>
    public class IdCardLogoTests
    {
        private const int Dpi = 200;
        private const double PxPerMm = Dpi / 25.4;

        public static IEnumerable<object[]> Designs() => IdCardLogoKit.Templates.Select(t => new object[] { t });
        public static IEnumerable<object[]> DesignsAndShapes() =>
            IdCardLogoKit.Templates.SelectMany(t => new[] { "wide", "tall", "square" }.Select(s => new object[] { t, s }));
        private static byte[] Shape(string s) => s switch { "wide" => IdCardLogoKit.Wide, "tall" => IdCardLogoKit.Tall, _ => IdCardLogoKit.Square };

        private static (Pixels Front, Pixels Back) Render(string template, byte[]? logo, IdCardModel? card = null)
        {
            var (f, b) = IdCardPdf.RenderPreview(IdCardLogoKit.School(logo), IdCardLogoKit.Brand(template), card ?? IdCardLogoKit.Card(), Dpi);
            return (Pixels.Decode(f), Pixels.Decode(b));
        }

        /// <summary>A real PNG header followed by rubbish: the size can be read, the picture cannot be drawn.</summary>
        private static byte[] CorruptPng() => IdCardLogoKit.Square.Take(60).Concat(Enumerable.Repeat((byte)0x5A, 200)).ToArray();

        private static int ImageCount(byte[] pdf) => System.Text.RegularExpressions.Regex.Matches(System.Text.Encoding.Latin1.GetString(pdf), "/Subtype\\s*/Image").Count;

        // ------------------------------------------------------------------------------------------------ present / absent / missing / corrupt
        private static readonly (byte R, byte G, byte B) Frame = (0x7B, 0x1F, 0xA2);
        private static (int X0, int Y0, int X1, int Y1)? FrameBox(Pixels p) => p.ColourBox(Frame.R, Frame.G, Frame.B, tol: 40);

        [Theory]
        [MemberData(nameof(DesignsAndShapes))]
        public void LogoPresent_IsDrawnOnTheFrontAndTheBack_NotStretched_NotCropped_AndInsideItsBox(string template, string shape)
        {
            var none = Render(template, null);
            var with = Render(template, Shape(shape));
            var aspect = shape switch { "wide" => 4.0, "tall" => 0.25, _ => 1.0 };
            foreach (var (face, a, b) in new[] { ("front", none.Front, with.Front), ("back", none.Back, with.Back) })
            {
                Assert.True(Pixels.DiffBox(a, b) != null, $"{template} {shape}: the {face} did not change when a logo was added");
                Assert.True(FrameBox(a) == null, $"{template}: the test colour is part of the design itself");
                var f = FrameBox(b);
                Assert.True(f != null, $"{template} {shape} {face}: the logo is not in the output");
                var (x0, y0, x1, y1) = f!.Value;
                double w = x1 - x0 + 1, h = y1 - y0 + 1;
                Assert.True(w / PxPerMm <= 16 && h / PxPerMm <= 16, $"{template} {shape} {face}: logo {w / PxPerMm:0.0} x {h / PxPerMm:0.0} mm is outside the largest logo slot");
                Assert.True(w / PxPerMm >= 3 || h / PxPerMm >= 3, $"{template} {shape} {face}: logo is too small to read");
                Assert.InRange(w / h / aspect, 0.9, 1.1);   // same aspect ratio as the file: not stretched
                // the logo's own centre dot is a circle and sits inside the frame
                var dot = b.ColourBox(255, 0, 255);
                Assert.True(dot != null, $"{template} {shape} {face}: the logo's inner pixels are missing");
                var dw = dot!.Value.X1 - dot.Value.X0 + 1; var dh = dot.Value.Y1 - dot.Value.Y0 + 1;
                Assert.InRange((double)dw / dh, 0.85, 1.18);
                Assert.True(dot.Value.X0 >= x0 && dot.Value.X1 <= x1 && dot.Value.Y0 >= y0 && dot.Value.Y1 <= y1);
                // not cropped: all four corners of the picture reach the output (a round plate would cut them off)
                foreach (var (cx, cy) in new[] { (x0 + 1, y0 + 1), (x1 - 1, y0 + 1), (x0 + 1, y1 - 1), (x1 - 1, y1 - 1) })
                {
                    var c = b.At(cx, cy);
                    Assert.True(Math.Abs(c.R - Frame.R) + Math.Abs(c.G - Frame.G) + Math.Abs(c.B - Frame.B) <= 60, $"{template} {shape} {face}: a corner of the logo is cut off");
                }
            }
        }

        [Theory]
        [MemberData(nameof(Designs))]
        public void NoLogoMissingFileOrCorruptImage_RenderTheSameInitialsLayout_AndNeverFail(string template)
        {
            var absent = IdCardPdf.RenderPreview(IdCardLogoKit.School(null), IdCardLogoKit.Brand(template), IdCardLogoKit.Card(), Dpi);
            Assert.NotEmpty(absent.Front);
            foreach (var bad in new byte[]?[] { null, Array.Empty<byte>(), CorruptPng(), new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, System.Text.Encoding.ASCII.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"/>") })
            {
                var school = new IdCardSchoolModel("Greenfield Public School", IdCardPicture.TryCreateVerified(bad), "12 Rajpur Road, Dehradun - 248001", "0135 2740 000");
                Assert.Null(school.Logo);
                var pdf = IdCardPdf.RenderSingle(school, IdCardLogoKit.Brand(template), IdCardLogoKit.Card());
                Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
                var (f, b) = IdCardPdf.RenderPreview(school, IdCardLogoKit.Brand(template), IdCardLogoKit.Card(), Dpi);
                Assert.Equal(absent.Front, f);
                Assert.Equal(absent.Back, b);
            }
        }

        [Fact]
        public void ACorruptBody_PassesTheHeaderCheck_ButNotTheVerifiedLoad()
        {
            Assert.NotNull(IdCardPicture.TryCreate(CorruptPng()));           // why the probe exists: this alone would only fail later, when the card is drawn
            Assert.Null(IdCardPicture.TryCreateVerified(CorruptPng()));
            Assert.NotNull(IdCardPicture.TryCreateVerified(IdCardLogoKit.Wide));
        }

        // ------------------------------------------------------------------------------------------------ nothing else is covered
        [Theory]
        [MemberData(nameof(DesignsAndShapes))]
        public void TheLogo_NeverCoversTheStudentPhoto_OrTheQrCode(string template, string shape)
        {
            var noLogo = Render(template, null, IdCardLogoKit.Card(photo: true));
            var withLogo = Render(template, Shape(shape), IdCardLogoKit.Card(photo: true));
            var green = noLogo.Front.ColourBox(0, 255, 0, tol: 30);
            Assert.True(green != null, $"{template}: the test photo is not on the front");
            var logoFront = FrameBox(withLogo.Front)!.Value;
            Assert.False(Pixels.Overlap(logoFront, green!.Value), $"{template} {shape}: the logo overlaps the student photo");
            Assert.Equal(CountColour(noLogo.Front, 0, 255, 0), CountColour(withLogo.Front, 0, 255, 0));   // every photo pixel is still there

            // QR area: where two different payloads differ
            var other = IdCardLogoKit.Card() with { QrPayload = IdCardQr.Payload("99999999999", "ZZ9999999") };
            var a = Render(template, null);
            var b = Render(template, null, other);
            var qr = Pixels.DiffBox(a.Back, b.Back)!.Value;
            var logoBack = FrameBox(Render(template, Shape(shape)).Back)!.Value;
            Assert.False(Pixels.Overlap(logoBack, qr), $"{template} {shape}: the logo overlaps the QR code");
        }

        private static int CountColour(Pixels p, byte r, byte g, byte b)
        {
            var n = 0;
            for (var y = 0; y < p.H; y++) for (var x = 0; x < p.W; x++) { var c = p.At(x, y); if (Math.Abs(c.R - r) + Math.Abs(c.G - g) + Math.Abs(c.B - b) <= 30) n++; }
            return n;
        }

        [Theory]
        [MemberData(nameof(Designs))]
        public void ALogoWithItsOwnDarkBackground_StillSitsOnALightPlate(string template)
        {
            var dark = IdCardLogoKit.LogoPng(200, 200, "#050505", "#050505", "#050505");
            var with = Render(template, dark);
            var box = with.Front.ColourBox(5, 5, 5, tol: 20)!.Value;
            // the plate shows as a margin around the picture: sample just outside the left edge of the picture
            var c = with.Front.At(box.X0 - 3, box.Y0 + (box.Y1 - box.Y0) / 2);
            Assert.True(c.R + c.G + c.B > 330, $"{template}: no light plate behind the logo (got {c})");
        }

        // ------------------------------------------------------------------------------------------------ one renderer for all three outputs
        [Theory]
        [MemberData(nameof(Designs))]
        public void SinglePdf_BulkSheet_AndPreview_AllCarryTheLogo(string template)
        {
            var school = IdCardLogoKit.School(IdCardLogoKit.Square);
            var bare = IdCardLogoKit.School(null);
            var brand = IdCardLogoKit.Brand(template);
            var card = IdCardLogoKit.Card();
            Assert.True(ImageCount(IdCardPdf.RenderSingle(school, brand, card)) > ImageCount(IdCardPdf.RenderSingle(bare, brand, card)));
            Assert.True(ImageCount(IdCardPdf.RenderSheets(school, brand, new[] { card, card }).Pdf) > ImageCount(IdCardPdf.RenderSheets(bare, brand, new[] { card, card }).Pdf));
            var withLogo = IdCardPdf.RenderPreview(school, brand, card, Dpi);
            var without = IdCardPdf.RenderPreview(bare, brand, card, Dpi);
            Assert.NotEqual(without.Front, withLogo.Front);
            Assert.NotEqual(without.Back, withLogo.Back);
        }

        [Fact]
        public void ThereIsOnlyOneCardDrawingCode_PreviewSingleAndSheetsAreTheOnlyWaysIn()
        {
            var entry = typeof(IdCardPdf).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name.StartsWith("Render")).Select(m => m.Name).OrderBy(n => n).ToArray();
            Assert.Equal(new[] { "RenderPreview", "RenderSheets", "RenderSingle" }, entry);
        }

        // ------------------------------------------------------------------------------------------------ the service: loaded once, one bad logo never breaks a run, limits hold
        public class Storage : DispatchProxy
        {
            public byte[]? Logo;
            public int Opens;
            public List<string> Asked = new();
            protected override object? Invoke(MethodInfo? m, object?[]? args)
            {
                if (m!.Name == nameof(IFileStorageService.OpenUpload)) { Opens++; Asked.Add((string)args![0]!); return Logo == null ? null : new MemoryStream(Logo); }
                return m.ReturnType.IsValueType && m.ReturnType != typeof(void) ? Activator.CreateInstance(m.ReturnType) : null;
            }
        }

        private static IdCardStudentRow Row(int i) => new($"ST{i}", $"ADM{i:000}", $"Student{i}", null, "Kumar", i.ToString(), null, 1, 1, "Class 5", "A", new DateTime(2015, 1, 1), null, "9999999999", "1 Road", null, null, null, null, null, null);

        private static (StudentIdCardService Svc, Storage Store) Service(byte[]? logo, int students, string template = "classic-blue")
        {
            var store = DispatchProxy.Create<IFileStorageService, Storage>();
            var s = (Storage)(object)store;
            s.Logo = logo;
            var rows = Enumerable.Range(1, students).Select(Row).ToList();
            var repo = Fake.Of<IStudentIdCardRepository>(new()
            {
                ["GetSchoolAsync"] = new IdCardSchoolRow("SCH1", "12345678910", "Greenfield Public School", "/uploads/12345678910/logo/logo_1.png", "1 Main Rd", null, "Dehradun", "Uttarakhand", "248001", "0135 274000"),
                ["GetCurrentYearAsync"] = new IdCardYearRow(5, "2026-27"),
                ["SectionBelongsToSchoolAsync"] = true, ["StudentExistsAsync"] = true, ["GetActiveStatusIdAsync"] = (int?)1,
                ["GetSectionStudentsAsync"] = rows,
                ["GetStudentAsync"] = rows.Count > 0 ? rows[0] : null,
                ["GetGuardiansAsync"] = new List<IdCardGuardianRow>(),
                ["GetBloodGroupNamesAsync"] = new Dictionary<int, string>(),
                ["GetCityNamesAsync"] = new Dictionary<(int, int, int, int), string>(),
            });
            var settings = Fake.Of<IIdCardSettingsService>(new() { ["GetBrandAsync"] = IdCardLogoKit.Brand(template) });
            return (new StudentIdCardService(repo, settings, store, Fake.Of<IStudentRepository>(), NullLogger<StudentIdCardService>.Instance), s);
        }

        [Fact]
        public async Task TheLogo_IsReadOnceForAWholeBulkRun_NotOncePerCard()
        {
            var (svc, store) = Service(IdCardLogoKit.Wide, 25);
            var r = await svc.GetSectionCardsAsync("SCH1", 1, CancellationToken.None);
            Assert.Equal(200, r.StatusCode);
            Assert.Equal(25, r.Data!.CardCount);
            Assert.Equal(1, store.Opens);
            Assert.Equal(new[] { "/uploads/12345678910/logo/logo_1.png" }, store.Asked);   // the path comes from the token school's own row and goes through OpenUpload's path guard
        }

        [Fact]
        public async Task OneBadLogo_NeverBreaksABulkRun_OrASingleCard_OrAPreview()
        {
            foreach (var bad in new byte[]?[] { null, CorruptPng(), new byte[] { 9, 9, 9 } })
            {
                var (svc, _) = Service(bad, 12);
                var bulk = await svc.GetSectionCardsAsync("SCH1", 1, CancellationToken.None);
                Assert.Equal(200, bulk.StatusCode);
                Assert.Equal(12, bulk.Data!.CardCount);
                Assert.Equal(2, bulk.Data.SheetCount);
                Assert.Equal(200, (await svc.GetStudentCardAsync("SCH1", "ST1", CancellationToken.None)).StatusCode);
                Assert.Equal(200, (await svc.PreviewAsync("SCH1", new IdCardPreviewRequest { TemplateCode = "classic-blue" }, CancellationToken.None)).StatusCode);
            }
        }

        [Fact]
        public async Task BulkLimit_Still100Cards_100PrintOn12Sheets_101IsRefused()
        {
            var (svc, store) = Service(IdCardLogoKit.Square, 100);
            var ok = await svc.GetSectionCardsAsync("SCH1", 1, CancellationToken.None);
            Assert.Equal(200, ok.StatusCode);
            Assert.Equal(100, ok.Data!.CardCount);
            Assert.Equal(12, ok.Data.SheetCount);
            Assert.Equal(1, store.Opens);
            var (svc2, store2) = Service(IdCardLogoKit.Square, 101);
            var refused = await svc2.GetSectionCardsAsync("SCH1", 1, CancellationToken.None);
            Assert.Equal(400, refused.StatusCode);
            Assert.Equal(0, store2.Opens);   // refused before any drawing work
        }

        [Fact]
        public async Task SinglePdf_Bulk_AndPreview_EachReadTheLogoOnce_AndShowIt()
        {
            var (svc, store) = Service(IdCardLogoKit.Square, 3);
            var single = await svc.GetStudentCardAsync("SCH1", "ST1", CancellationToken.None);
            var preview = await svc.PreviewAsync("SCH1", new IdCardPreviewRequest { TemplateCode = "classic-blue" }, CancellationToken.None);
            var bulk = await svc.GetSectionCardsAsync("SCH1", 1, CancellationToken.None);
            Assert.Equal(3, store.Opens);   // one per run, three runs
            Assert.True(ImageCount(single.Data!.Content) >= 2);
            Assert.True(ImageCount(Convert.FromBase64String(bulk.Data!.PdfBase64!)) >= 2);
            var (none, _) = Service(null, 3);
            var barePreview = await none.PreviewAsync("SCH1", new IdCardPreviewRequest { TemplateCode = "classic-blue" }, CancellationToken.None);
            Assert.NotEqual(barePreview.Data!.FrontPng, preview.Data!.FrontPng);   // the preview shows the logo too
        }

        [Fact]
        public async Task ANewLogoShowsInTheNextPreview_NothingIsKept()
        {
            var (svc, store) = Service(IdCardLogoKit.Wide, 1);
            var first = await svc.PreviewAsync("SCH1", new IdCardPreviewRequest { TemplateCode = "classic-blue" }, CancellationToken.None);
            store.Logo = IdCardLogoKit.Tall;   // the school uploads another logo
            var second = await svc.PreviewAsync("SCH1", new IdCardPreviewRequest { TemplateCode = "classic-blue" }, CancellationToken.None);
            Assert.NotEqual(first.Data!.FrontPng, second.Data!.FrontPng);
            Assert.Equal(2, store.Opens);
        }

        [Fact]
        public void ThePreviewEndpoint_IsNeverCached()
        {
            var m = typeof(smss_api.Controllers.IdCardSettingsController).GetMethod("Preview")!;
            var attr = m.GetCustomAttribute<Microsoft.AspNetCore.Mvc.ResponseCacheAttribute>();
            Assert.NotNull(attr);
            Assert.True(attr!.NoStore);
        }

        [Fact]
        public void OnlyTheLogoAndPhotosAreRead_TheSignatureStaysPrivate_AndNoNewUrlExists()
        {
            var src = File.ReadAllText(FindUp("smss-api-service-layer/service/StudentIdCardService.cs"));
            Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(src, "_storage\\.OpenUpload\\(").Count);
            Assert.DoesNotContain("OpenPrivate", src);   // the signature is read by IdCardSettingsService from private storage, never here
        }

        private static string FindUp(string rel, [System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", rel));   // relative to this source file, wherever the build output goes
    }
}
