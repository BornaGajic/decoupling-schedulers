using Framework.Common;

namespace Framework.Model;

public record JobMetadata : IJobMetadata
{
    public DateTimeOffset? NextFireTimeUtc { get; init; }
    public DateTimeOffset? PreviousFireTimeUtc { get; init; }
}