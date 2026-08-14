namespace Framework.Model
{
    public enum SchedulerEventType
    {
        BeforeExecution,
        AfterExecution,
        Paused,
        Canceled,
        Resumed,
        Error
    }

    public class SchedulerEventArgs : EventArgs
    {
        public SchedulerEventType EventType { get; init; }
        public Exception Exception { get; init; }
        public TimeSpan ExecutionTime { get; init; }
        public string JobKey { get; init; }
        public string Message { get; init; }
        public DateTimeOffset? NextFireTimeUtc { get; init; }
        public DateTimeOffset? PreviousFireTimeUtc { get; init; }
    }
}