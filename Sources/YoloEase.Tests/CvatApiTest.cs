using System.Net.Http.Headers;
using CvatApi;
using Shouldly;

namespace YoloEase.Tests;

/// <summary>
/// Disabled-by-default integration tests for the generated CVAT API client.
/// </summary>
[Ignore("Integration test that requires a reachable CVAT server and YOLOEASE_CVAT_TEST_* credentials.")]
public class CvatApiIntegrationFixture
{
    /// <summary>
    /// WHAT: Verifies that the generated CVAT auth client can exchange configured credentials for a token.
    /// HOW: Reads the integration endpoint and credentials from YOLOEASE_CVAT_TEST_* environment variables.
    /// </summary>
    [Test]
    public async Task ShouldConnect()
    {
        // Given
        var settings = ReadSettings();
        using var httpClient = new HttpClient {BaseAddress = settings.BaseAddress};
        var authClient = new CvatAuthClient(httpClient);

        // When
        var token = await authClient.Auth_create_loginAsync(new LoginSerializerExRequest
        {
            Username = settings.Username,
            Password = settings.Password,
        });

        // Then
        token.ShouldNotBeNull();
        token.Key.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// WHAT: Verifies that an authenticated generated CVAT tasks client can list tasks.
    /// HOW: Logs in with environment-provided credentials, attaches the token header, and requests the task page.
    /// </summary>
    [Test]
    public async Task ShouldGetTasks()
    {
        // Given
        var settings = ReadSettings();
        using var httpClient = new HttpClient {BaseAddress = settings.BaseAddress};
        var authClient = new CvatApi.CvatAuthClient(httpClient);
        var token = await authClient.Auth_create_loginAsync(new LoginSerializerExRequest
        {
            Username = settings.Username,
            Password = settings.Password,
        });
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", token.Key);

        // When
        var taskClient = new CvatTasksClient(httpClient);
        var tasks = await taskClient.Tasks_listAsync();

        // Then
        tasks.ShouldNotBeNull();
    }

    private static CvatIntegrationSettings ReadSettings()
    {
        var baseAddress = Environment.GetEnvironmentVariable("YOLOEASE_CVAT_TEST_URL");
        var username = Environment.GetEnvironmentVariable("YOLOEASE_CVAT_TEST_USERNAME");
        var password = Environment.GetEnvironmentVariable("YOLOEASE_CVAT_TEST_PASSWORD");

        if (string.IsNullOrWhiteSpace(baseAddress) ||
            string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password))
        {
            Assert.Inconclusive("Set YOLOEASE_CVAT_TEST_URL, YOLOEASE_CVAT_TEST_USERNAME, and YOLOEASE_CVAT_TEST_PASSWORD to run this integration fixture.");
        }

        return new CvatIntegrationSettings(new Uri(baseAddress!), username!, password!);
    }

    private sealed record CvatIntegrationSettings(Uri BaseAddress, string Username, string Password);
}
