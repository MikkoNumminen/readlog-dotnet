using System.Net;
using ReadLog.Tests.Infrastructure;

namespace ReadLog.Tests.Smoke;

/// <summary>
/// Boots the real application (against an isolated temp database) and verifies the
/// home page is served — the "does it actually run, migrations and all?" guard.
/// </summary>
public class HomePageSmokeTests : IClassFixture<ReadLogAppFactory>
{
    private readonly ReadLogAppFactory _factory;

    public HomePageSmokeTests(ReadLogAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_Home_returns_200_and_renders_brand()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("ReadLog", html);
    }
}
