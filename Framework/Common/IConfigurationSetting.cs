namespace Framework.Common;

public interface IConfigurationSetting
{
    /// <summary>
    /// Configuration key, i.e. name to which this setting should bind to.
    /// </summary>
    static abstract string ConfigurationKey { get; }
}