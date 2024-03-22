using Framework.Common;

namespace Framework.Model;

public record JobContext : JobMetadata, IJobContext
{
    public CancellationToken CancellationToken { get; init; }
}