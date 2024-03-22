namespace Framework.Model;

public enum JobState
{
    /// <summary>
    /// Indicates that the trigger is in the "normal" state.
    /// </summary>
    Normal,

    /// <summary>
    /// Indicates that the trigger is in the "paused" state.
    /// </summary>
    Paused,

    /// <summary>
    /// "Complete" indicates that the trigger has not remaining fire-times in its schedule.
    /// </summary>
    Complete,

    /// <summary>
    /// When the trigger is in the error state, the scheduler will make no attempts to fire it.
    /// </summary>
    Error,

    /// <summary>
    /// Waiting until job with the same key completes
    /// </summary>
    Blocked,

    /// <summary>
    /// Indicates that the trigger does not exist.
    /// </summary>
    None
}