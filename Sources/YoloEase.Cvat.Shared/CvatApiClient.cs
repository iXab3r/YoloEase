using CvatApi;

namespace YoloEase.Cvat.Shared;

public class CvatApiClient
{
    private readonly IHttpClientFactory httpClientFactory;

    static CvatApiClient()
    {
    }
    
    public CvatApiClient(IHttpClientFactory httpClientFactory)
    {
        this.httpClientFactory = httpClientFactory;
    }

    public string Token
    {
        get; set;
    }

    public bool IsLoggedIn => !string.IsNullOrEmpty(Token);
    
    public string Username { get; set; }
    
    public string Password { get; set; }
    
    public Uri BaseAddress { get; set; }

    public async Task Logout()
    {
        if (!IsLoggedIn)
        {
            throw new InvalidOperationException($"User {Username} is not logged in");
        }

        try
        {
            using var httpClient = await CreateAuthenticatedClient();
            var authClient = new CvatAuthClient(httpClient);
            await authClient.Auth_create_logoutAsync();
        }
        finally
        {
            Token = default;
        }
    }
    
    public async Task<MetaUser> Login()
    {
        if (IsLoggedIn)
        {
            throw new InvalidOperationException($"User {Username} is already logged in, logout first");
        }
        Token = await AuthenticateViaBasicAuth();
        return await RetrieveSelf();
    }
    
    public async Task<MetaUser> RetrieveSelf()
    {
        return await RunAuthenticated(async httpClient =>
        {
            var organizationsClient = new CvatUsersClient(httpClient);
            var user = await organizationsClient.Users_retrieve_selfAsync();
            if (user == null)
            {
                throw new InvalidOperationException($"Failed to get current user");
            }

            return user;
        });
    }
    
    public async Task RunAuthenticated(Func<HttpClient, Task> supplier)
    {
        var attempt = 0;
        while (true)
        {
            using var httpClient = await CreateAuthenticatedClient();

            try
            {
                await supplier(httpClient);
                return;
            }
            catch (ApiException e)
            {
                if (e.StatusCode is 401 or 403 && attempt < 1)
                {
                    ReportAuthenticationIsBroken();
                }
                else
                {
                    throw;
                }
            }
            finally
            {
                attempt++;
            }
        }
    }

    public async Task<T> RunAuthenticated<T>(Func<HttpClient, Task<T>> supplier)
    {
        var attempt = 0;
        while (true)
        {
            using var httpClient = await CreateAuthenticatedClient();

            try
            {
                var result = await supplier(httpClient);
                return result;
            }
            catch (ApiException e)
            {
                if (e.StatusCode is 401 or 403 && attempt < 1)
                {
                    ReportAuthenticationIsBroken();
                }
                else
                {
                    throw;
                }
            }
            finally
            {
                attempt++;
            }
        }
    }

    public void ReportAuthenticationIsBroken()
    {
        Token = null;
    }

    private async Task EnsureAuthenticated()
    {
        if (string.IsNullOrEmpty(Token))
        {
            Token = await AuthenticateViaBasicAuth();
        }
        if (string.IsNullOrEmpty(Token))
        {
            throw new ArgumentException("Failed to retrieve authentication token");
        }
    }

    private async Task<string> AuthenticateViaBasicAuth()
    {
        if (string.IsNullOrEmpty(Username))
        {
            throw new ArgumentException("Username must be specified");
        }
        if (string.IsNullOrEmpty(Password))
        {
            throw new ArgumentException("Password must be specified");
        }

        // there is an issue with CSRF being required for reused connections, thus the new client is created on each authentication attempt
        using var httpClient = new HttpClient();
        httpClient.BaseAddress = BaseAddress;
        httpClient.DefaultRequestHeaders.Referrer = new Uri($"{BaseAddress}api/auth/login");
        var authClient = new CvatAuthClient(httpClient);
        var token = await authClient.Auth_create_loginAsync(new LoginSerializerExRequest() {Username = Username, Password = Password});
        if (token == null || string.IsNullOrEmpty(token.Key))
        {
            throw new InvalidOperationException($"Failed to authenticate as {Username} - token is not specified");
        }

        return token.Key;
    }

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient();
        client.BaseAddress = BaseAddress;
        return client;
    }

    private async Task<HttpClient> CreateAuthenticatedClient()
    {
        await EnsureAuthenticated();

        var httpClient = CreateClient();
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Token {Token}");
        return httpClient;
    }
}