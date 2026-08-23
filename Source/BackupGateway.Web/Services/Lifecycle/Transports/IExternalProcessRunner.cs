namespace BackupGateway.Web.Services.Lifecycle.Transports;

internal interface IExternalProcessRunner
{
    Task<ExternalProcessResult> RunAsync(ExternalProcessInvocation invocation, CancellationToken cancellationToken);
}

internal sealed record ExternalProcessInvocation(string FileName, IReadOnlyList<string> Arguments, TimeSpan Timeout);

internal sealed record ExternalProcessResult(int ExitCode, string StandardOutput, string StandardError);
