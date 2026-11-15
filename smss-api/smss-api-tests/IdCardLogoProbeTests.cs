using System.IO.Compression;
using smss_api_service_layer.helper.idcard;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// "Is this logo paintable?" must not depend on the logo's colours: white, near-white and transparent-with-light-artwork logos are valid (and shown on a dark plate so they can be seen);
    /// a fully transparent or corrupt image is not (the card keeps its initials / text layout). Plus the plate edge and the larger Formal Classic / Playful Blob slots.
    /// </summary>
    public class IdCardLogoProbeTests
    {
        // ---- a real RGBA PNG with exactly the pixels asked for (no renderer between the test and the file) ----------------------------
        private static uint[] _crc;
        private static uint Crc(byte[] data)
        {
            _crc ??= Enumerable.Range(0, 256).Select(n => { var c = (uint)n; for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; return c; }).ToArray();
            var crc = 0xFFFFFFFFu;
            foreach (var b in data) crc = _crc[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }

        private static byte[] Chunk(string type, byte[] body)
        {
            var t = System.Text.Encoding.ASCII.GetBytes(type);
            var all = t.Concat(body).ToArray();
            var len = BitConverter.GetBytes(body.Length).Reverse();
            return len.Concat(all).Concat(BitConverter.GetBytes(Crc(all)).Reverse()).ToArray();
        }

        public static byte[] RawPng(int w, int h, Func<int, int, (byte R, byte G, byte B, byte A)> pixel)
        {
            var raw = new MemoryStream();
            for (var y = 0; y < h; y++)
            {
                raw.WriteByte(0);
                for (var x = 0; x < w; x++) { var p = pixel(x, y); raw.WriteByte(p.R); raw.WriteByte(p.G); raw.WriteByte(p.B); raw.WriteByte(p.A); }
            }
            var z = new MemoryStream();
            using (var zs = new ZLibStream(z, CompressionLevel.Optimal, leaveOpen: true)) zs.Write(raw.ToArray());
            var ihdr = BitConverter.GetBytes(w).Reverse().Concat(BitConverter.GetBytes(h).Reverse()).Concat(new byte[] { 8, 6, 0, 0, 0 }).ToArray();
            return new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.Concat(Chunk("IHDR", ihdr)).Concat(Chunk("IDAT", z.ToArray())).Concat(Chunk("IEND", Array.Empty<byte>())).ToArray();
        }

        /// <summary>A 200 x 200 crest: a 16 px frame and a round centre, in one colour, on a transparent background.</summary>
        public static byte[] Crest((byte R, byte G, byte B, byte A) ink, (byte R, byte G, byte B, byte A)? background = null)
        {
            var bg = background ?? (0, 0, 0, 0);
            return RawPng(200, 200, (x, y) =>
            {
                var frame = x < 16 || y < 16 || x >= 184 || y >= 184;
                var dx = x - 100; var dy = y - 100;
                return frame || dx * dx + dy * dy < 56 * 56 ? ink : bg;
            });
        }

        private static readonly (byte, byte, byte, byte) White = (255, 255, 255, 255);

        // ------------------------------------------------------------------------------------------------ valid or not, whatever the colours
        [Fact]
        public void WhiteNearWhiteAndPaleLogos_OnTransparent_AreValid_AndKnownToBeLight()
        {
            foreach (var ink in new (byte, byte, byte, byte)[] { (255, 255, 255, 255), (250, 250, 250, 255), (244, 244, 244, 255), (255, 253, 245, 255) })
            {
                var pic = IdCardPicture.TryCreateVerified(Crest(ink));
                Assert.NotNull(pic);
                Assert.True(pic!.IsLight, $"{ink} should count as light artwork");
            }
        }

        [Fact]
        public void ALogoThatIsOneSolidWhiteSquare_IsValid()
        {
            var pic = IdCardPicture.TryCreateVerified(RawPng(64, 64, (_, _) => White));
            Assert.NotNull(pic);
        }

        [Fact]
        public void DarkAndColoredLogos_AreValid_AndNotLight()
        {
            foreach (var ink in new (byte, byte, byte, byte)[] { (0, 0, 0, 255), (16, 16, 16, 255), (200, 30, 60, 255), (20, 80, 200, 255), (255, 200, 0, 255) })
            {
                var pic = IdCardPicture.TryCreateVerified(Crest(ink));
                Assert.NotNull(pic);
                Assert.False(pic!.IsLight, $"{ink} should not count as light artwork");
            }
        }

        [Fact]
        public void ALogoOnAnOpaqueWhiteBackground_WithColouredArtwork_IsNotLight()
        {
            var pic = IdCardPicture.TryCreateVerified(Crest((200, 30, 60, 255), White));
            Assert.NotNull(pic);
            Assert.False(pic!.IsLight);
            Assert.False(IdCardPicture.TryCreateVerified(IdCardLogoKit.Square)!.IsLight);   // the ordinary test crest
        }

        [Fact]
        public void AFullyTransparentLogo_IsTreatedAsMissing()
        {
            Assert.NotNull(IdCardPicture.TryCreate(Crest((255, 255, 255, 0))));        // a real, readable PNG ...
            Assert.Null(IdCardPicture.TryCreateVerified(Crest((255, 255, 255, 0))));   // ... that paints nothing
            Assert.Null(IdCardPicture.TryCreateVerified(RawPng(40, 40, (_, _) => (0, 0, 0, 0))));
            Assert.Null(IdCardPicture.TryCreateVerified(RawPng(40, 40, (_, _) => (255, 0, 0, 0))));   // transparent, whatever its colour channel says
        }

        [Theory]
        [InlineData(255, 255, 255)]
        [InlineData(0, 0, 0)]
        [InlineData(128, 128, 128)]
        public void ValidityDoesNotDependOnTheColour_OfAnOpaqueLogo(int r, int g, int b) =>
            Assert.NotNull(IdCardPicture.TryCreateVerified(Crest(((byte)r, (byte)g, (byte)b, 255))));

        // ------------------------------------------------------------------------------------------------ a white logo is actually SEEN on every design
        private static (int X0, int Y0, int X1, int Y1)? PurpleBox(Pixels p) => p.ColourBox(0x7B, 0x1F, 0xA2, tol: 40);

        [Theory]
        [MemberData(nameof(IdCardLogoTests.Designs), MemberType = typeof(IdCardLogoTests))]
        public void AWhiteLogo_SitsOnADarkPlate_AndItsArtworkIsVisibleOnTheFrontAndTheBack(string template)
        {
            var purple = Crest((0x7B, 0x1F, 0xA2, 255));
            var white = Crest(White);
            var locate = IdCardPdf.RenderPreview(IdCardLogoKit.School(purple), IdCardLogoKit.Brand(template), IdCardLogoKit.Card(), 200);
            var seen = IdCardPdf.RenderPreview(IdCardLogoKit.School(white), IdCardLogoKit.Brand(template), IdCardLogoKit.Card(), 200);
            foreach (var (where, a, b) in new[] { ("front", locate.Front, seen.Front), ("back", locate.Back, seen.Back) })
            {
                var box = PurpleBox(Pixels.Decode(a));
                Assert.True(box != null, $"{template} {where}: could not find the logo slot");
                var img = Pixels.Decode(b);
                var (x0, y0, x1, y1) = box!.Value;
                var midY = (y0 + y1) / 2;
                var frame = img.At(x0 + 2, midY);            // inside the white frame of the logo
                var plate = img.At(x0 - 3, midY);            // the plate margin just outside the artwork
                Assert.True(frame.R > 235 && frame.G > 235 && frame.B > 235, $"{template} {where}: the white artwork is not there ({frame})");
                Assert.True((plate.R + plate.G + plate.B) / 3 < 110, $"{template} {where}: the plate behind a white logo is not dark ({plate})");
            }
        }

        [Fact]
        public void ADarkPlate_IsTheTemplatesOwnDarkColour_OrNearBlackWhenThatIsNotDarkEnough()
        {
            Assert.Equal(IdCardPalette.From("#14213D", "#FF7A18").PrimaryDark, Kit.DarkPlate(IdCardPalette.From("#14213D", "#FF7A18")));
            Assert.Equal("#1F2430", Kit.DarkPlate(IdCardPalette.From("#FFF59D", "#FF7A18")));   // a pale brand colour: its "dark" is not dark
            foreach (var t in IdCardTemplates.All)
                Assert.True(IdCardPalette.Luminance(Kit.DarkPlate(IdCardPalette.From(t.DefaultPrimary, t.DefaultSecondary))) < 0.2);
        }

        // ------------------------------------------------------------------------------------------------ a fully transparent logo falls back on every design, and renders
        [Theory]
        [MemberData(nameof(IdCardLogoTests.Designs), MemberType = typeof(IdCardLogoTests))]
        public void AFullyTransparentLogo_FallsBackToTheNoLogoLayout_AndNeverFails(string template)
        {
            var absent = IdCardPdf.RenderPreview(IdCardLogoKit.School(null), IdCardLogoKit.Brand(template), IdCardLogoKit.Card(), 150);
            var school = new IdCardSchoolModel("Greenfield Public School", IdCardPicture.TryCreateVerified(Crest((255, 255, 255, 0))), "12 Rajpur Road, Dehradun - 248001", "0135 2740 000");
            Assert.Null(school.Logo);
            var shown = IdCardPdf.RenderPreview(school, IdCardLogoKit.Brand(template), IdCardLogoKit.Card(), 150);
            Assert.Equal(absent.Front, shown.Front);
            Assert.Equal(absent.Back, shown.Back);
        }

        // ------------------------------------------------------------------------------------------------ plate edge and the larger slots
        private static (int X0, int Y0, int X1, int Y1)? PlateBox(Pixels p, byte r, byte g, byte b) => p.ColourBox(r, g, b, tol: 20);

        [Fact]
        public void PlayfulBlobPlate_HasAVisibleEdge_SoItDoesNotVanishOnTheWhiteBody()
        {
            var (f, _) = IdCardPdf.RenderPreview(IdCardLogoKit.School(Crest((0x7B, 0x1F, 0xA2, 255), White)), IdCardLogoKit.Brand("playful-blob"), IdCardLogoKit.Card(), 200);
            var px = Pixels.Decode(f);
            var box = PurpleBox(px)!.Value;
            var midY = (box.Y0 + box.Y1) / 2;
            // from the logo outwards: padding (plate tint) ... edge line ... white card body
            var row = Enumerable.Range(1, 22).Select(d => px.At(box.X0 - d, midY)).ToList();
            static int Dist((byte R, byte G, byte B) a, (byte R, byte G, byte B) b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
            var plate = row[2];
            var body = row[21];
            Assert.True(Dist(body, (255, 255, 255)) < 12, $"expected the white body outside the plate, got {body}");
            Assert.True(Dist(plate, body) > 8 && Dist(plate, body) < 80, $"the plate should be a pale tint, got {plate}");
            var darkest = row.OrderBy(c => c.R + c.G + c.B).First();
            Assert.True(Dist(darkest, plate) > 60, $"no darker edge line around the plate (darkest {darkest}, plate {plate})");
        }

        private static (double W, double H)? SlotMm(string template, bool front)
        {
            var (f, b) = IdCardPdf.RenderPreview(IdCardLogoKit.School(IdCardPicRawSquare()), IdCardLogoKit.Brand(template), IdCardLogoKit.Card(), 200);
            var box = PurpleBox(Pixels.Decode(front ? f : b));
            return box == null ? null : ((box.Value.X1 - box.Value.X0 + 1) / (200 / 25.4), (box.Value.Y1 - box.Value.Y0 + 1) / (200 / 25.4));
        }
        private static byte[] IdCardPicRawSquare() => Crest((0x7B, 0x1F, 0xA2, 255), White);

        [Fact]
        public void FormalClassic_LogoBesideThePhoto_IsLargerThanBefore_AndPlayfulBlobsToo()
        {
            // artwork size = slot - 2 x padding 0.9 mm. Before: Formal 15 mm slot -> 13.2 mm artwork; Playful 9 mm -> 7.2 mm
            var fw = SlotMm("formal-classic", front: true)!.Value.W; Assert.True(fw is > 14.5 and < 15.6, $"formal artwork {fw:0.0} mm");   // 17 mm slot
            var pw = SlotMm("playful-blob", front: true)!.Value.W; Assert.True(pw is > 7.9 and < 8.7, $"playful front artwork {pw:0.0} mm");       // 10 mm slot
            var pb = SlotMm("playful-blob", front: false)!.Value.W; Assert.True(pb is > 7.9 and < 8.7, $"playful back artwork {pb:0.0} mm");
        }

        [Fact]
        public void TheLargerSlots_StillLeaveThePhotoQrAndTextAlone()
        {
            foreach (var template in new[] { "formal-classic", "playful-blob" })
                foreach (var shape in new[] { IdCardLogoKit.Wide, IdCardLogoKit.Tall, IdCardLogoKit.Square, Crest(White) })
                {
                    var none = IdCardPdf.RenderPreview(IdCardLogoKit.School(null), IdCardLogoKit.Brand(template), IdCardLogoKit.Card(photo: true), 200);
                    var with = IdCardPdf.RenderPreview(IdCardLogoKit.School(shape), IdCardLogoKit.Brand(template), IdCardLogoKit.Card(photo: true), 200);
                    var a = Pixels.Decode(none.Front); var b = Pixels.Decode(with.Front);
                    var green = a.ColourBox(0, 255, 0, tol: 30)!.Value;
                    // every photo pixel is the same with and without the logo
                    for (var y = green.Y0; y <= green.Y1; y += 3)
                        for (var x = green.X0; x <= green.X1; x += 3)
                        {
                            var p = a.At(x, y); var q = b.At(x, y);
                            Assert.True(Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B) < 40, $"{template}: the logo touched the photo at {x},{y}");
                        }
                    // the QR code (back): where two payloads differ is untouched by the logo
                    var other = IdCardLogoKit.Card() with { QrPayload = smss_api_service_layer.helper.IdCardQr.Payload("99999999999", "ZZ9999999") };
                    var q0 = Pixels.Decode(IdCardPdf.RenderPreview(IdCardLogoKit.School(null), IdCardLogoKit.Brand(template), IdCardLogoKit.Card(), 200).Back);
                    var q1 = Pixels.Decode(IdCardPdf.RenderPreview(IdCardLogoKit.School(null), IdCardLogoKit.Brand(template), other, 200).Back);
                    var qr = Pixels.DiffBox(q0, q1)!.Value;
                    var withBack = Pixels.Decode(with.Back);
                    for (var y = qr.Y0; y <= qr.Y1; y += 3)
                        for (var x = qr.X0; x <= qr.X1; x += 3)
                        {
                            var p = q0.At(x, y); var q = withBack.At(x, y);
                            Assert.True(Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B) < 40, $"{template}: the logo touched the QR area at {x},{y}");
                        }
                }
        }
    }
}
