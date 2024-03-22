namespace Framework.Common;

public interface IJobContext : IJobMetadata
{
    CancellationToken CancellationToken { get; }
}