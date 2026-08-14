namespace Framework.Common;

public interface IJobMetadata
{
    DateTimeOffset? NextFireTimeUtc { get; }
    DateTimeOffset? PreviousFireTimeUtc { get; }
    TimeSpan? PreviousRunDuration { get; init; }
}