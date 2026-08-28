using System.Net;
using System.Net.Http.Json;
using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Tests.ApiTests;

namespace ProductAssetManager.Api.Tests.ApiTests.Auth;

[TestFixture]
public class RegisterEndpointTests : ApiTestBase
{
    [Test]
    public async Task When_RegisteringWithNewValidUser_Should_ReturnCreatedWithUserDetails()
    {
        var request = new RegisterRequest
        {
            Name = "New Valid User",
            Email = "new.valid.user@example.com",
            Password = "Passw0rd!"
        };

        var response = await Client.PostAsJsonAsync("/api/auth/register", request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var body = await response.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.Email, Is.EqualTo(request.Email));
        Assert.That(body.Id, Is.Not.Empty);
    }

    [Test]
    public async Task When_RegisteringWithAlreadyUsedEmail_Should_ReturnBadRequest()
    {
        var request = new RegisterRequest
        {
            Name = "Duplicate Email User",
            Email = "duplicate.email.user@example.com",
            Password = "Passw0rd!"
        };

        var firstResponse = await Client.PostAsJsonAsync("/api/auth/register", request);
        Assert.That(firstResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var secondRequest = new RegisterRequest
        {
            Name = "A Different Name",
            Email = request.Email,
            Password = "AnotherPassw0rd!"
        };

        var secondResponse = await Client.PostAsJsonAsync("/api/auth/register", secondRequest);

        Assert.That(secondResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }
}
