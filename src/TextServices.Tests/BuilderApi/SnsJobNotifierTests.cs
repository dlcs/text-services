using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using FakeItEasy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TextServices.Builder.Api.Configuration;
using TextServices.Builder.Api.Data;
using TextServices.Builder.Api.Services.Notifications;

namespace TextServices.Tests.BuilderApi;

public sealed class SnsJobNotifierTests
{
    private static JobCompletionNotification MakeNotification(
        string jobId = "my/job",
        JobStatus status = JobStatus.Completed) =>
        new(jobId, status, DateTimeOffset.UtcNow, 10, 500, null, null);

    // -------------------------------------------------------------------------
    // No-op when TopicArn is absent
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Notify_WhenTopicArnAbsent_DoesNotCallSns(string? topicArn)
    {
        var sns = A.Fake<IAmazonSimpleNotificationService>();
        var sut = MakeNotifier(sns, topicArn);

        await sut.Notify(MakeNotification());

        A.CallTo(() => sns.PublishAsync(A<PublishRequest>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    // -------------------------------------------------------------------------
    // Publishes when configured
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Notify_WhenTopicArnConfigured_PublishesToTopic()
    {
        var sns = A.Fake<IAmazonSimpleNotificationService>();
        A.CallTo(() => sns.PublishAsync(A<PublishRequest>._, A<CancellationToken>._))
            .Returns(new PublishResponse());

        var sut = MakeNotifier(sns, "arn:aws:sns:eu-west-1:123:test-topic");

        await sut.Notify(MakeNotification("books/b123", JobStatus.Completed));

        A.CallTo(() => sns.PublishAsync(
                A<PublishRequest>.That.Matches(r =>
                    r.TopicArn == "arn:aws:sns:eu-west-1:123:test-topic" &&
                    r.Message.Contains("books/b123")),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Theory]
    [InlineData(JobStatus.Completed, "JobCompleted")]
    [InlineData(JobStatus.Failed, "JobFailed")]
    public async Task Notify_SetsMessageTypeAttribute(JobStatus status, string expectedType)
    {
        var sns = A.Fake<IAmazonSimpleNotificationService>();
        A.CallTo(() => sns.PublishAsync(A<PublishRequest>._, A<CancellationToken>._))
            .Returns(new PublishResponse());

        var sut = MakeNotifier(sns, "arn:aws:sns:eu-west-1:123:test-topic");

        await sut.Notify(MakeNotification(status: status));

        A.CallTo(() => sns.PublishAsync(
                A<PublishRequest>.That.Matches(r =>
                    r.MessageAttributes["MessageType"].StringValue == expectedType),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Notify_SerialisesMessageWithCamelCaseStringEnumAndOmitsNullErrors()
    {
        var sns = A.Fake<IAmazonSimpleNotificationService>();
        PublishRequest? captured = null;
        A.CallTo(() => sns.PublishAsync(A<PublishRequest>._, A<CancellationToken>._))
            .Invokes((PublishRequest r, CancellationToken _) => captured = r)
            .Returns(new PublishResponse());

        var sut = MakeNotifier(sns, "arn:aws:sns:eu-west-1:123:test-topic");

        await sut.Notify(MakeNotification("books/b123", JobStatus.Waiting));

        captured.ShouldNotBeNull();
        var message = captured.Message;
        message.ShouldContain("\"jobId\":\"books/b123\"");
        message.ShouldContain("\"status\":\"Waiting\"");
        message.ShouldNotContain("errors");
    }

    // -------------------------------------------------------------------------
    // Error handling
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Notify_WhenSnsThrows_DoesNotPropagate()
    {
        var sns = A.Fake<IAmazonSimpleNotificationService>();
        A.CallTo(() => sns.PublishAsync(A<PublishRequest>._, A<CancellationToken>._))
            .ThrowsAsync(new AmazonSimpleNotificationServiceException("SNS error"));

        var sut = MakeNotifier(sns, "arn:aws:sns:eu-west-1:123:test-topic");

        // Must not throw
        await sut.Notify(MakeNotification()).ShouldNotThrowAsync();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static SnsJobNotifier MakeNotifier(IAmazonSimpleNotificationService sns, string? topicArn) =>
        new(sns,
            Options.Create(new TextServicesOptions
            {
                Notifications = new NotificationsOptions { TopicArn = topicArn },
            }),
            NullLogger<SnsJobNotifier>.Instance);
}
