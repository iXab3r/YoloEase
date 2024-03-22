using CvatApi;
using Shouldly;

namespace YoloEase.Tests;

[Ignore("Integration")]
public class Tests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public async Task ShouldConnect()
    {
        //Given
        var httpClient = new HttpClient() {BaseAddress = new Uri("https://cvat.eyeauras.net")};
        var authClient = new CvatApi.CvatAuthClient( httpClient);

        //When
        var token = await authClient.Auth_create_loginAsync(new LoginSerializerExRequest() {Username = "", Password = ""});

        //Then
        token.ShouldNotBeNull();
    }

    [Test]
    public async Task ShouldGetTasks()
    {
        //Given
        var httpClient = new HttpClient() {BaseAddress = new Uri("https://cvat.eyeauras.net")};
        var authClient = new CvatApi.CvatAuthClient(httpClient);
        var token = await authClient.Auth_create_loginAsync(new LoginSerializerExRequest() {Username = "", Password = ""});

        //When
        var taskClient = new CvatTasksClient(httpClient);
        var tasks = await taskClient.Tasks_listAsync();

        //Then

    }

    [Test]
    public void Test1()
    {
        Assert.Pass();
    }
}