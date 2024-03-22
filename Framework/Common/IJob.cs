namespace Framework.Common;

public interface IJob
{
    Task Execute(IJobContext context);
}