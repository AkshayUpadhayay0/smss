using smss_api_db_layer.repository;
using Xunit;

namespace smss_api_tests
{
    public class SearchWordsTests
    {
        [Fact]
        public void EachWordBecomesItsOwnPattern_SoAFullNameMatchesWithOrWithoutAMiddleName() =>
            Assert.Equal(new[] { "%Anil%", "%Kumar%" }, SearchWords.LikePatterns("  Anil   Kumar "));

        [Fact]
        public void WildcardsTypedByTheUserAreEscaped() =>
            Assert.Equal(new[] { "%50\\%%", "%a\\_b%", "%x\\\\y%" }, SearchWords.LikePatterns("50% a_b x\\y"));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void BlankMeansNoFilter(string? s) => Assert.Empty(SearchWords.LikePatterns(s));
    }
}
