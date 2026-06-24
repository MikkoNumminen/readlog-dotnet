using ReadLog.Tests.Infrastructure;

namespace ReadLog.Tests.Pages;

/// <summary>Pins the security response headers, including the CSP carve-out the Google
/// OAuth handoff needs (form-action is enforced on redirects).</summary>
public class SecurityHeadersTests
{
    [Fact]
    public async Task Every_response_carries_the_security_headers()
    {
        using var factory = new ReadLogAppFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));

        var csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("script-src 'self'", csp);
        // Regression guard: the Google sign-in form POSTs to /signin then 302-redirects to
        // accounts.google.com, and form-action is checked against the redirect target — so a
        // bare "form-action 'self'" silently blocks Google sign-in in the browser.
        Assert.Contains("form-action 'self' https://accounts.google.com", csp);
    }
}
