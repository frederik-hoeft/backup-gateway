using System.Collections.Concurrent;

namespace BackupGateway.Web.Services.Leases;

public sealed class TargetLeaseMutationSerializer
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new(StringComparer.Ordinal);

    public async Task<T> RunAsync<T>(string targetId, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        ArgumentNullException.ThrowIfNull(action);

        SemaphoreSlim semaphore = _semaphores.GetOrAdd(targetId, static _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            return await action(cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
    }
}
