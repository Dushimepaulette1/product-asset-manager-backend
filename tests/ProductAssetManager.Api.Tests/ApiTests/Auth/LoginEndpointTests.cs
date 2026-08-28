using System.Net;
using System.Net.Http.Json;
using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Tests.ApiTests;

namespace ProductAssetManager.Api.Tests.ApiTests.Auth;

[TestFixture]
public class LoginEndpointTests : ApiTestBase
{
    private const string TestUserEmail = "login.test.user@example.com";
    private const string TestUserPassword = "Passw0rd!";

    [SetUp]
    public async Task RegisterTestUser()
    {
        var request = new RegisterRequest
        {
            Name = "Login Test User",
            Email = TestUserEmail,
            Password = TestUserPassword
        };

        var response = await Client.PostAsJsonAsync("/api/auth/register", request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
    }

    [Test]
    public async Task When_LoggingInWithCorrectCredentials_Should_ReturnOkWithNonEmptyToken()
    {
        var request = new LoginRequest
        {
            Email = TestUserEmail,
            Password = TestUserPassword
        };

        var response = await Client.PostAsJsonAsync("/api/auth/login", request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.Token, Is.Not.Empty);
    }

    [Test]
    public async Task When_LoggingInWithIncorrectPassword_Should_ReturnUnauthorizedWithGenericErrorMessage()
    {
        var request = new LoginRequest
        {
            Email = TestUserEmail,
            Password = "TheWrongPassword!"
        };

        var response = await Client.PostAsJsonAsync("/api/auth/login", request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        var body = await response.Content.ReadAsStringAsync();
        Assert.That(body, Does.Contain("Invalid email or password"));
    }
}
