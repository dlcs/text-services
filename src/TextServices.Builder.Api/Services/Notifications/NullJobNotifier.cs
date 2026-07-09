namespace TextServices.Builder.Api.Services.Notifications;

internal sealed class NullJobNotifier : IJobNotifier
{
    public Task Notify(JobCompletionNotification notification, CancellationToken ct = default)
        => Task.CompletedTask;
}
