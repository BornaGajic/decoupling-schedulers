using Framework.Common;

namespace Framework.Test;

public class TestJob : IJob
{
    public async Task Execute(IJobContext context)
    {
        await Task.Delay(250);
    }
}