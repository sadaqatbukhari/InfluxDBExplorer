using System.Net;
using Tickbird.Core.Updates;

namespace Tickbird.Tests;

public class ReleaseCheckerTests
{
    private sealed class StubHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond());
    }

    private static Task<UpdateCheckResult> Check(Func<HttpResponseMessage> respond, string installed = "3.0.7") =>
        new ReleaseChecker(new HttpClient(new StubHandler(respond))).CheckAsync(Version.Parse(installed));

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body) };

    [Fact]
    public async Task NewerStableRelease_IsReported_WithEscapedTagLink()
    {
        var r = await Check(() => Json("{\"tag_name\":\"v4.0.0\"}"));
        Assert.True(r.UpdateAvailable);
        Assert.Equal(ReleaseChecker.ReleasesUrl + "/tag/v4.0.0", r.ReleaseUrl);
    }

    [Fact]
    public async Task SameVersion_IsUpToDate_IgnoringMissingRevision()
    {
        var r = await Check(() => Json("{\"tag_name\":\"v3.0.7\"}"), "3.0.7.0");
        Assert.False(r.UpdateAvailable);
        Assert.Equal("You are up to date.", r.Message);
    }

    [Theory]
    [InlineData("{\"tag_name\":\"v9.0.0\",\"prerelease\":true}")]
    [InlineData("{\"tag_name\":\"v9.0.0\",\"draft\":true}")]
    public async Task PrereleasesAndDrafts_AreIgnored(string body)
    {
        Assert.False((await Check(() => Json(body))).UpdateAvailable);
    }

    [Fact]
    public async Task NotFound_ReportsNoRelease()
    {
        var r = await Check(() => Json("", HttpStatusCode.NotFound));
        Assert.False(r.UpdateAvailable);
        Assert.Contains("No published release", r.Message);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"tag_name\":\"banana\"}")]
    public async Task MalformedResponses_DoNotThrow(string body)
    {
        Assert.False((await Check(() => Json(body))).UpdateAvailable);
    }

    [Fact]
    public async Task ServerError_ReturnsFriendlyMessage()
    {
        var r = await Check(() => Json("", HttpStatusCode.Forbidden));
        Assert.False(r.UpdateAvailable);
        Assert.Contains("Unable to check", r.Message);
    }

    [Fact]
    public void Normalize_FillsMissingComponents() =>
        Assert.Equal(new Version(3, 0, 0, 0), ReleaseChecker.Normalize(new Version(3, 0)));
}
