namespace BuildingBlocks.Messaging;

/// <summary>
/// AWS settings. Set ServiceUrl (e.g. http://localstack:4566) for LocalStack; leave empty for real AWS,
/// where the default credential chain (IAM role) is used - no keys in config.
/// </summary>
public class AwsOptions
{
    public const string Section = "Aws";
    public string Region { get; set; } = "us-east-1";
    public string? ServiceUrl { get; set; }
    public string AccessKey { get; set; } = "test";     // only used with ServiceUrl (LocalStack)
    public string SecretKey { get; set; } = "test";     // only used with ServiceUrl (LocalStack)
    public bool AutoProvision { get; set; } = false;    // create queues/topics if missing (dev only)
    public int VisibilityTimeoutSeconds { get; set; } = 30;
    public int MaxReceiveCount { get; set; } = 5;
    public int WaitTimeSeconds { get; set; } = 10;
    public int MaxBackoffSeconds { get; set; } = 300;
}
