using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ReadLog.Tests.Smoke;

/// <summary>
/// Boots the real application in-memory and verifies the home page is served.
/// This is the scaffold's "does it actually run?" guard for CI.
/// </summary>
public class HomePageSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HomePageSmokeTests(WebApplicationFactory<Program> factory)
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
