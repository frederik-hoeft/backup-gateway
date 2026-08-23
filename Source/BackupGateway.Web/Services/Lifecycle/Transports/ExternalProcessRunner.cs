using System.Diagnostics;

namespace BackupGateway.Web.Services.Lifecycle.Transports;

internal sealed class ExternalProcessRunner : IExternalProcessRunner
{
    public async Task<ExternalProcessResult> RunAsync(
        ExternalProcessInvocation invocation,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new(invocation.FileName)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in invocation.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start required process '{invocation.FileName}'.");
        }

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(invocation.Timeout);
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(timeout.Token);
        Task<string> standardError = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return new ExternalProcessResult(
                process.ExitCode,
                await standardOutput,
                await standardError);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}
