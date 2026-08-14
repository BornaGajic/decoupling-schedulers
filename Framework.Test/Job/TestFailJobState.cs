using Framework.Common;

namespace Framework.Test;

public class TestFailJobState : IJob
{
    public Task Execute(IJobContext context)
    {
        throw new NotImplementedException();
    }
}