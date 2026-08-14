using Framework.Common;

namespace Framework.Test;

public class FailInstantiatingJob([FromKeyedServices(nameof(FailTestJob))] TaskCompletionSource Completion) : IJob
{
    public Task Execute(IJobContext context)
    {
        Completion.SetResult();
        return Task.CompletedTask;
    }
}