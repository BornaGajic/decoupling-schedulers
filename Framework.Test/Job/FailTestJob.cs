using Framework.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Test;

public class FailTestJob([FromKeyedServices(nameof(FailTestJob))] TaskCompletionSource Completion) : IJob
{
    public async Task Execute(IJobContext context)
    {
        await Task.Delay(250);

        try
        {
            throw new Exception("Test should fail.");
        }
        catch (Exception ex)
        {
            Completion.SetException(ex);
            throw;
        }
    }
}