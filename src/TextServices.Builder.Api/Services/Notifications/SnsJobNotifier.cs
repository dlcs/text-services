using System.Text.Json;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Microsoft.Extensions.Options;
using TextServices.Builder.Api.Configuration;
using TextServices.Builder.Api.Data;

namespace TextServices.Builder.Api.Services.Notifications;

internal sealed class SnsJobNotifier(
    IAmazonSimpleNotificationService sns,
    IOptions<TextServicesOptions> options,
    ILogger<SnsJobNotifier> logger) : IJobNotifier
{
    public async Task Notify(JobCompletionNotification notification, CancellationToken ct = default)
    {
        var topicArn = options.Value.Notifications.TopicArn;
        if (string.IsNullOrEmpty(topicArn)) return;

        try
        {
            var messageType = notification.Status == JobStatus.Completed ? "JobCompleted" : "JobFailed";

            await sns.PublishAsync(new PublishRequest
            {
                TopicArn = topicArn,
                Message = JsonSerializer.Serialize(notification),
                MessageAttributes = new Dictionary<string, MessageAttributeValue>
                {
                    ["MessageType"] = new()
                    {
                        DataType = "String",
                        StringValue = messageType,
                    },
                },
            }, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to publish job notification for {JobId}", notification.JobId);
        }
    }
}
