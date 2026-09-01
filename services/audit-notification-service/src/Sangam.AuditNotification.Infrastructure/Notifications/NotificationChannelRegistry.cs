using Sangam.AuditNotification.Application.Notifications.Delivery;
using Sangam.AuditNotification.Domain.Notifications;

namespace Sangam.AuditNotification.Infrastructure.Notifications;

/// <summary>
/// Maps a channel to the adapter that carries it.
/// </summary>
/// <remarks>
/// Two adapters registered for one channel is a configuration mistake with no
/// safe interpretation - taking the last one registered would mean the choice of
/// who sends a member's messages depends on registration order. It fails at
/// startup instead, where somebody is watching.
/// </remarks>
public sealed class NotificationChannelRegistry : INotificationChannelRegistry
{
    private readonly Dictionary<NotificationChannel, INotificationChannel> _channels = [];

    public NotificationChannelRegistry(IEnumerable<INotificationChannel> channels)
    {
        foreach (var channel in channels)
        {
            if (!_channels.TryAdd(channel.Channel, channel))
            {
                throw new InvalidOperationException(
                    $"Two providers are registered for {channel.Channel} notifications: "
                    + $"{_channels[channel.Channel].GetType().Name} and {channel.GetType().Name}. "
                    + "Register exactly one.");
            }
        }
    }

    public INotificationChannel? For(NotificationChannel channel) =>
        _channels.GetValueOrDefault(channel);

    public IReadOnlyCollection<NotificationChannel> Configured => _channels.Keys;
}
