namespace Framework.Model
{
    public enum SchedulerExecutionTimeline
    {
        BeforeExecution,
        AfterExecution
    }

    public class SchedulerEventArgs : EventArgs
    {
        public Exception Exception { get; init; }
        public required DateTimeOffset ExecutionTime { get; init; }
        public required string JobKey { get; init; }
        public required SchedulerExecutionTimeline Timeline { get; init; }
    }
}