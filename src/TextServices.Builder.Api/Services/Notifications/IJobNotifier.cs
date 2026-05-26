namespace TextServices.Builder.Api.Services.Notifications;

/// <summary>
/// Publishes a notification when a job reaches a terminal state.
/// Implementations must not throw — errors are handled and logged internally.
/// </summary>
public interface IJobNotifier
{
    Task Notify(JobCompletionNotification notification, CancellationToken ct = default);
}
