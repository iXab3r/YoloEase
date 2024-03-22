using System.ComponentModel;
using JetBrains.Annotations;
using YoloEase.Cvat.Shared;
using YoloEase.UI.Cvat;

namespace YoloEase.UI.Core;

public interface ICvatClient : INotifyPropertyChanged
{
    CvatApiClient Api { get; }
    CvatCliWrapper Cli { get; }
    string Username { get; [UsedImplicitly] set; }
    string Password { get; [UsedImplicitly] set; }
    string ServerUrl { get; [UsedImplicitly] set; }
}