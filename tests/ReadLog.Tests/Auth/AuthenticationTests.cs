using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using ReadLog.Tests.Infrastructure;

namespace ReadLog.Tests.Auth;

/// <summary>
/// End-to-end auth tests: they drive the real Login/Register/Logout Razor Pages over
/// HTTP (antiforgery tokens and cookies included) against an isolated temp database.
/// </summary>
public class AuthenticationTests : IClassFixture<ReadLogAppFactory>
{
    private readonly ReadLogAppFactory _factory;

    public AuthenticationTests(ReadLogAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Signin_page_renders_the_login_form()
    {
        var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/signin");

        Assert.Contains("Sign in", html);
        Assert.Contains("__RequestVerificationToken", html);
    }

    [Fact]
    public async Task Register_page_renders_the_form()
    {
        var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/register");

        Assert.Contains("Create account", html);
    }

    [Fact]
    public async Task Registering_signs_the_user_in()
    {
        var client = _factory.CreateClient();

        await RegisterAsync(client, "newuser@example.com", "Password1");

        var home = await client.GetStringAsync("/");
        Assert.Contains("Sign out", home);
        Assert.Contains("newuser@example.com", home);
    }

    [Fact]
    public async Task Register_logout_login_round_trip_restores_the_session()
    {
        var client = _factory.CreateClient();
        const string email = "roundtrip@example.com";
        const string password = "Password1";

        await RegisterAsync(client, email, password);
        await LogoutAsync(client);

        var loggedOut = await client.GetStringAsync("/");
        Assert.DoesNotContain("Sign out", loggedOut);
        Assert.Contains("Sign in", loggedOut);

        await LoginAsync(client, email, password);

        var loggedIn = await client.GetStringAsync("/");
        Assert.Contains("Sign out", loggedIn);
    }

    [Fact]
    public async Task Login_with_a_wrong_password_is_rejected()
    {
        var client = _factory.CreateClient();
        const string email = "wrongpass@example.com";
        await RegisterAsync(client, email, "Password1");
        await LogoutAsync(client);

        var response = await PostFormAsync(client, "/signin", new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = "WrongPassword9",
            ["ReturnUrl"] = "/",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // stayed on the login page
        Assert.Contains("Invalid login attempt", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Registering_with_a_mismatched_confirmation_is_rejected()
    {
        var client = _factory.CreateClient();

        var response = await PostFormAsync(client, "/register", new Dictionary<string, string>
        {
            ["Input.Email"] = "mismatch@example.com",
            ["Input.Password"] = "Password1",
            ["Input.ConfirmPassword"] = "Password2",
            ["ReturnUrl"] = "/",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("passwords do not match", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Registering_a_duplicate_email_is_rejected()
    {
        var client = _factory.CreateClient();
        const string email = "duplicate@example.com";
        await RegisterAsync(client, email, "Password1");
        await LogoutAsync(client);

        var response = await PostFormAsync(client, "/register", new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = "Password2",
            ["Input.ConfirmPassword"] = "Password2",
            ["ReturnUrl"] = "/",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // re-rendered, not signed in
        Assert.Contains("already taken", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Navbar_greets_a_named_user_by_display_name()
    {
        var client = _factory.CreateClient();

        var response = await PostFormAsync(client, "/register", new Dictionary<string, string>
        {
            ["Input.Name"] = "Ada Lovelace",
            ["Input.Email"] = "ada@example.com",
            ["Input.Password"] = "Password1",
            ["Input.ConfirmPassword"] = "Password1",
            ["ReturnUrl"] = "/",
        });
        response.EnsureSuccessStatusCode();

        var home = await client.GetStringAsync("/");
        Assert.Contains("Ada Lovelace", home);
    }

    [Fact]
    public async Task A_GET_to_signout_does_not_sign_the_user_out()
    {
        // No-redirect client so we can observe the raw 302 responses.
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var token = HtmlFormHelper.ExtractAntiforgeryToken(await client.GetStringAsync("/register"));
        var register = await client.PostAsync("/register", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = "getsignout@example.com",
            ["Input.Password"] = "Password1",
            ["Input.ConfirmPassword"] = "Password1",
            ["__RequestVerificationToken"] = token,
            ["ReturnUrl"] = "/",
        }));
        Assert.Equal(HttpStatusCode.Redirect, register.StatusCode); // signed in, redirected home

        var getSignout = await client.GetAsync("/signout");
        Assert.Equal(HttpStatusCode.Redirect, getSignout.StatusCode); // GET only redirects

        var home = await client.GetStringAsync("/");
        Assert.Contains("Sign out", home); // still authenticated
    }

    [Fact]
    public async Task A_GET_to_external_login_redirects_to_signin()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/external-login");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/signin", response.Headers.Location!.OriginalString);
    }

    private static async Task RegisterAsync(HttpClient client, string email, string password)
    {
        var response = await PostFormAsync(client, "/register", new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["Input.ConfirmPassword"] = password,
            ["ReturnUrl"] = "/",
        });
        response.EnsureSuccessStatusCode();
    }

    private static async Task LoginAsync(HttpClient client, string email, string password)
    {
        var response = await PostFormAsync(client, "/signin", new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["ReturnUrl"] = "/",
        });
        response.EnsureSuccessStatusCode();
    }

    private static async Task LogoutAsync(HttpClient client)
    {
        // The sign-out form (with its antiforgery token) lives in the navbar of any page,
        // so take the token from the home page rather than GET /signout (which redirects).
        var token = HtmlFormHelper.ExtractAntiforgeryToken(await client.GetStringAsync("/"));
        var response = await client.PostAsync("/signout",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        response.EnsureSuccessStatusCode();
    }

    /// <summary>GETs the page to grab the antiforgery token + cookie, then POSTs the form.</summary>
    private static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client, string url, Dictionary<string, string> fields)
    {
        var html = await client.GetStringAsync(url);
        fields["__RequestVerificationToken"] = HtmlFormHelper.ExtractAntiforgeryToken(html);
        return await client.PostAsync(url, new FormUrlEncodedContent(fields));
    }
}
