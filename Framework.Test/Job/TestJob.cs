using Framework.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Test;

public class TestJob([FromKeyedServices(nameof(TestJob))] TaskCompletionSource Completion) : IJob
{
    public async Task Execute(IJobContext context)
    {
        await Task.Delay(250);
        Completion.SetResult();
    }
}