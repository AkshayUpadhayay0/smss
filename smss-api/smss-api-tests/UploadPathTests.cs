using Microsoft.Extensions.Logging.Abstractions;
using smss_api_service_layer.helper;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// How a stored upload path (tb_schools.logo_url) is read back. The upload code always writes "/uploads/{code}/logo/{file}"; rows from an import, an older release or a hand edit can
    /// spell it differently, and every harmless spelling must still find the file while anything that leaves the uploads folder must not.
    /// </summary>
    public class UploadPathTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "smss-upl-" + Guid.NewGuid().ToString("N"));
        private readonly string _private = Path.Combine(Path.GetTempPath(), "smss-prv-" + Guid.NewGuid().ToString("N"));
        private readonly LocalFileStorageService _storage;
        private const string Code = "SunRise_Up-03";
        private const string File1 = "logo_20261114100121581.png";
        private static readonly byte[] Bytes = { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };

        public UploadPathTests()
        {
            Directory.CreateDirectory(Path.Combine(_root, Code, "logo"));
            File.WriteAllBytes(Path.Combine(_root, Code, "logo", File1), Bytes);
            Directory.CreateDirectory(Path.Combine(_private, Code, "id-card"));
            File.WriteAllBytes(Path.Combine(_private, Code, "id-card", "signature_1.png"), new byte[] { 9 });
            _storage = new LocalFileStorageService(_root, "/uploads", _private, NullLogger<LocalFileStorageService>.Instance);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); Directory.Delete(_private, true); } catch { /* temp */ }
        }

        private const string Bs = "\\";
        private static readonly string Canonical = $"/uploads/{Code}/logo/{File1}";

        public static IEnumerable<object[]> Spellings()
        {
            var c = Canonical;
            yield return new object[] { "canonical", c };
            yield return new object[] { "backslashes", c.Replace("/", Bs) };
            yield return new object[] { "mixed separators", "/uploads" + Bs + Code + Bs + "logo/" + File1 };
            yield return new object[] { "no leading slash", c.TrimStart('/') };
            yield return new object[] { "double slashes", c.Replace("/uploads/", "//uploads//") };
            yield return new object[] { "full https URL", "https://localhost:7037" + c };
            yield return new object[] { "full URL with a query", "https://localhost:7037" + c + "?v=3" };
            yield return new object[] { "query suffix", c + "?v=3" };
            yield return new object[] { "fragment suffix", c + "#top" };
            yield return new object[] { "surrounding spaces", "  " + c + " " };
            yield return new object[] { "percent-encoded character", c.Replace("logo_2026", "logo_%32026") };
            yield return new object[] { "percent-encoded slashes", c.Replace("/logo/", "%2Flogo%2F") };
            yield return new object[] { "capital U in the prefix", c.Replace("/uploads/", "/Uploads/") };
        }

        [Theory]
        [MemberData(nameof(Spellings))]
        public void EveryHarmlessSpelling_FindsTheFile(string _, string stored)
        {
            using var s = _storage.OpenUpload(stored);
            Assert.NotNull(s);
            using var ms = new MemoryStream(); s!.CopyTo(ms);
            Assert.Equal(Bytes, ms.ToArray());
            // the photo reader routes a value to the public folder only when it LOOKS public (slash, backslash, URL); a bare "uploads/..." could be a private key, so it is not guessed
            if (stored.TrimStart().StartsWith('/') || stored.TrimStart().StartsWith((char)92) || stored.TrimStart().StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                using var viaPhoto = _storage.OpenPhoto(stored);
                Assert.NotNull(viaPhoto);
            }
        }

        [Fact]
        public void AnAbsolutePathInsideTheUploadsFolder_Works_ButOnlyInside()
        {
            using var ok = _storage.OpenUpload(Path.Combine(_root, Code, "logo", File1));
            Assert.NotNull(ok);
            using var outside = _storage.OpenUpload(Path.Combine(_private, Code, "id-card", "signature_1.png"));
            Assert.Null(outside);                           // the private folder is not the uploads folder
        }

        [Theory]
        [InlineData("/uploads/SunRise_Up-03/logo/../../../x.png")]
        [InlineData("/uploads/../x.png")]
        [InlineData("/uploads/./SunRise_Up-03/logo/logo_20261114100121581.png")]
        [InlineData("\\uploads\\..\\..\\SunRise_Up-03\\logo\\logo_20261114100121581.png")]
        [InlineData("/uploads/SunRise_Up-03/logo/%2e%2e/%2e%2e/x.png")]
        [InlineData("/uploads/SunRise_Up-03/logo/logo_20261114100121581.png:stream")]
        [InlineData("/uploads/C:/Windows/win.ini")]
        [InlineData("/uploads/")]
        [InlineData("/uploads")]
        [InlineData("/other/SunRise_Up-03/logo/logo_20261114100121581.png")]
        [InlineData("ftp://host/uploads/SunRise_Up-03/logo/logo_20261114100121581.png")]
        [InlineData("")]
        [InlineData("   ")]
        public void AnythingThatLeavesTheUploadsFolder_OrIsNotAnUploadPath_FindsNothing(string stored)
        {
            Assert.Null(_storage.OpenUpload(stored));
            Assert.Null(UploadPathReader.RelativeTo(stored, "/uploads", _root));
        }

        [Fact]
        public void ANullByte_IsRefused() =>
            Assert.Null(_storage.OpenUpload(Canonical + "%00.png"));

        [Fact]
        public void APrivateKey_IsNeverTreatedAsPublic_EvenForASchoolCalledUploads()
        {
            // OpenPhoto decides public vs private by the spelling: "{code}/students/..." is private, whatever the code is
            Directory.CreateDirectory(Path.Combine(_private, "uploads", "students", "ST1"));
            File.WriteAllBytes(Path.Combine(_private, "uploads", "students", "ST1", "photo_1.jpg"), new byte[] { 7 });
            using var s = _storage.OpenPhoto("uploads/students/ST1/photo_1.jpg");
            Assert.NotNull(s);
            using var ms = new MemoryStream(); s!.CopyTo(ms);
            Assert.Equal(new byte[] { 7 }, ms.ToArray());   // read from the private store, not looked up under /uploads
        }

        [Fact]
        public void Delete_UsesTheSameReading_AndStillStaysInsideTheUploadsFolder()
        {
            var victim = Path.Combine(_private, Code, "id-card", "signature_1.png");
            _storage.Delete("/uploads/" + Code + "/logo/../../../" + Path.GetRelativePath(Path.GetTempPath(), victim).Replace(Bs, "/"));
            Assert.True(File.Exists(victim));
            _storage.Delete(Bs + "uploads" + Bs + Code + Bs + "logo" + Bs + File1);
            Assert.False(File.Exists(Path.Combine(_root, Code, "logo", File1)));
        }

        [Fact]
        public void TheSavedLogoPath_IsAlwaysTheCanonicalForm_SoNewRowsNeverNeedTheLeniency()
        {
            var url = _storage.SaveSchoolLogoAsync(Code, new MemoryStream(Bytes), ".png", CancellationToken.None).GetAwaiter().GetResult();
            Assert.StartsWith($"/uploads/{Code}/logo/logo_", url);
            Assert.DoesNotContain(Bs, url);
            Assert.NotNull(_storage.OpenUpload(url));
        }
    }
}
