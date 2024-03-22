using JetBrains.Annotations;
using YoloEase.Cvat.Shared;
using YoloEase.UI.Cvat;

namespace YoloEase.UI.Core;

public class CvatClient : DisposableReactiveObjectWithLogger, ICvatClient
{
    private static readonly Binder<CvatClient> Binder = new();

    static CvatClient()
    {
        Binder.Bind(x => x.Username).To(x => x.Api.Username);
        Binder.Bind(x => x.Password).To(x => x.Api.Password);
        Binder.Bind(x => ParseDefault(x.ServerUrl)).To(x => x.Api.BaseAddress);

        Binder.Bind(x => x.Username).To(x => x.Cli.Username);
        Binder.Bind(x => x.Password).To(x => x.Cli.Password);
        Binder.Bind(x => ParseDefault(x.ServerUrl)).To(x => x.Cli.BaseAddress);
    }

    public CvatApiClient Api { get; }
    
    public CvatCliWrapper Cli { get; }

    public string Username { get; [UsedImplicitly] set; }

    public string Password { get; [UsedImplicitly] set; }

    public string ServerUrl { get; [UsedImplicitly] set; }

    public CvatClient(
        CvatApiClient cvatApiClient,
        CvatCliWrapper cvatCliWrapper)
    {
        Api = cvatApiClient;
        Cli = cvatCliWrapper;

        Binder.Attach(this).AddTo(Anchors);
    }
    
    private static Uri ParseDefault(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var result))
        {
            return result;
        }

        return default;
    }
}