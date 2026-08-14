using Framework.Common;

namespace Framework.Test;

public class ThreeSecondDelayJob : IJob
{
    public Task Execute(IJobContext context)
    {
        return Task.Delay(3000);
    }
}