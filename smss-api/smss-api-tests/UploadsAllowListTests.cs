using Microsoft.AspNetCore.Http;
using smss_api.Helpers;
using Xunit;

namespace smss_api_tests
{
    /// <summary>The public /uploads space serves only /uploads/{schoolCode}/logo/{file}.png|jpg|jpeg|webp; everything else is a 404.</summary>
    public class UploadsAllowListTests
    {
        [Theory]
        [InlineData("/uploads/12345678912/logo/logo_20261029072052465.png")]   // the shape the upload code writes
        [InlineData("/uploads/LAS-DDN-2026/logo/logo_20261114104417044.jpeg")]
        [InlineData("/uploads/SunRise_Up-03/logo/logo_1.jpg")]
        [InlineData("/uploads/ABC/logo/logo_1.webp")]
        [InlineData("/uploads/ABC/logo/LOGO_1.PNG")]                            // extension case does not matter
        [InlineData("/Uploads/ABC/Logo/logo_1.png")]
        public void Logo_paths_are_allowed(string path) => Assert.True(UploadsAllowListMiddleware.IsAllowedLogoPath(path));

        [Theory]
        // other folders and file kinds
        [InlineData("/uploads/ABC/students/ST1/photo_1.png")]
        [InlineData("/uploads/ABC/employees/E1/documents/id.pdf")]
        [InlineData("/uploads/ABC/homework/1/a.pdf")]
        [InlineData("/uploads/ABC/id-card/signature_1.png")]
        [InlineData("/uploads/ABC/logo/logo_1.pdf")]
        [InlineData("/uploads/ABC/logo/logo_1.svg")]
        [InlineData("/uploads/ABC/logo/logo_1.gif")]
        [InlineData("/uploads/ABC/logo/logo_1.png.exe")]
        [InlineData("/uploads/ABC/logo/logo_1")]
        [InlineData("/uploads/ABC/logo/.png")]
        [InlineData("/uploads/ABC/logo/.htaccess")]
        // directories and shapes that are not exactly code/logo/file
        [InlineData("/uploads")]
        [InlineData("/uploads/")]
        [InlineData("/uploads/ABC")]
        [InlineData("/uploads/ABC/")]
        [InlineData("/uploads/ABC/logo")]
        [InlineData("/uploads/ABC/logo/")]
        [InlineData("/uploads/ABC/logo/sub/logo_1.png")]
        [InlineData("/uploads/logo/logo_1.png")]
        [InlineData("/uploads//logo/logo_1.png")]
        [InlineData("/uploads/ABC//logo_1.png")]
        // traversal, plain and encoded (Request.Path is already decoded once, so %2e%2e arrives as ".."; %252e arrives as "%2e")
        [InlineData("/uploads/ABC/logo/../students/ST1/photo_1.png")]
        [InlineData("/uploads/ABC/logo/../../../appsettings.json")]
        [InlineData("/uploads/../appsettings.json")]
        [InlineData("/uploads/ABC/logo/..%2f..%2fx.png")]
        [InlineData("/uploads/ABC/logo/%2e%2e/x.png")]
        [InlineData("/uploads/ABC/logo/%252e%252e/x.png")]
        [InlineData("/uploads/ABC/logo/..\\x.png")]
        [InlineData("/uploads/ABC\\logo/logo_1.png")]
        [InlineData("/uploads/A.B/logo/logo_1.png")]                           // a school code has no dots
        [InlineData("/uploads/ABC/logo/a..png")]
        [InlineData("/uploads/ABC/logo/logo_1.png\n")]                         // no trailing newline trick
        [InlineData("/uploads/ABC/logo/logo_1.png%00.txt")]
        [InlineData("/uploads/ABC/logo/logo_1.png:stream")]
        [InlineData("")]
        [InlineData(null)]
        public void Everything_else_is_refused(string? path) => Assert.False(UploadsAllowListMiddleware.IsAllowedLogoPath(path));

        private static async Task<(int Status, bool ReachedNext)> Send(string path)
        {
            var reached = false;
            var mw = new UploadsAllowListMiddleware(_ => { reached = true; return Task.CompletedTask; });
            var ctx = new DefaultHttpContext();
            ctx.Request.Path = path;
            await mw.InvokeAsync(ctx);
            return (ctx.Response.StatusCode, reached);
        }

        [Fact]
        public async Task Allowed_logo_goes_on_to_the_static_files_middleware()
        {
            var (status, next) = await Send("/uploads/ABC/logo/logo_1.png");
            Assert.True(next);
            Assert.Equal(200, status);
        }

        [Theory]
        [InlineData("/uploads/ABC/students/ST1/photo_1.png")]
        [InlineData("/uploads/ABC/logo/../students/ST1/photo_1.png")]
        [InlineData("/uploads")]
        [InlineData("/uploads/")]
        [InlineData("/UPLOADS/ABC/employees/E1/doc.pdf")]
        public async Task Blocked_paths_answer_404_and_never_reach_the_file_server(string path)
        {
            var (status, next) = await Send(path);
            Assert.False(next);
            Assert.Equal(404, status);
        }

        [Theory]
        [InlineData("/api/public/partner-schools")]
        [InlineData("/api/Auth/login")]
        [InlineData("/uploadsX/file.png")]
        [InlineData("/")]
        public async Task Paths_outside_uploads_are_left_alone(string path)
        {
            var (status, next) = await Send(path);
            Assert.True(next);
            Assert.Equal(200, status);
        }

        [Fact]
        public void Every_logo_url_the_upload_code_writes_is_allowed()
        {
            // SaveSchoolLogoAsync: $"{_requestPath}/{schoolCode}/logo/logo_{yyyyMMddHHmmssfff}{ext}", ext from LogoRules.AllowedExtensions
            foreach (var ext in smss_api_service_layer.helper.LogoRules.AllowedExtensions)
                Assert.True(UploadsAllowListMiddleware.IsAllowedLogoPath($"/uploads/DPS-001_x/logo/logo_{DateTime.UtcNow:yyyyMMddHHmmssfff}{ext}"));
        }
    }
}
