using DgLabSocketSpire2.Configuration;

namespace DgLabSocketSpire2.Bridge;

internal interface IDgLabClient : IDisposable
{
    bool IsConnected { get; }
    bool IsBound { get; }
    string? ClientId { get; }
    string? TargetId { get; }
    int StrengthA { get; }
    int StrengthB { get; }
    int LimitA { get; }
    int LimitB { get; }
    string LastFeedback { get; }
    string LastError { get; }
    string LastNotice { get; }
    void Start(int port);
    Task SendStrengthAsync(ChannelRef channel, StrengthOperation operation, int value);
    Task SendWaveAsync(ChannelRef channel, string[] frames, int durationSeconds);
    Task ClearChannelAsync(ChannelRef channel);
}
