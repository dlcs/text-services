using Serilog.Context;
using Serilog.Core.Enrichers;

namespace TextServices.Builder.Api.Jobs;

internal static class LogContextHelpers
{
    /// <summary>
    /// "CorrelationId" properties to log context, which is then output as part of default log template.
    /// This is useful for filtering logs. Consists of {jobId}:{random-guid}
    /// </summary>
    public static IDisposable SetCorrelationId(string jobId) =>
        LogContext.Push(new PropertyEnricher("CorrelationId", $"{jobId}:{Guid.NewGuid()}"));
}
