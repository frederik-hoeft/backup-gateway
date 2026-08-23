namespace BackupGateway.Web.Services.Lifecycle.Transports;

internal sealed class TargetLifecycleTransportException : Exception
{
    public TargetLifecycleTransportException(string failureCode, string message)
        : base(message)
    {
        FailureCode = failureCode;
    }

    public TargetLifecycleTransportException(string failureCode, string message, Exception innerException)
        : base(message, innerException)
    {
        FailureCode = failureCode;
    }

    public string FailureCode { get; }
}
