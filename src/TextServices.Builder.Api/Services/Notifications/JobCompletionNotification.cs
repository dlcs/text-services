using TextServices.Builder.Api.Data;

namespace TextServices.Builder.Api.Services.Notifications;

public record JobCompletionNotification(
    string JobId,
    JobStatus Status,
    DateTimeOffset? Finished,
    int TotalPages,
    int TotalWordCount,
    string? Errors);
