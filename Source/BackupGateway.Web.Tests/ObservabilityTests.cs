using BackupGateway.Web.Services.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace BackupGateway.Web.Tests;

[TestClass]
public sealed class ObservabilityTests
{
    [TestMethod]
    public async Task CorrelationContextIsSharedByHttpResponseAndAuditEventAsync()
    {
        CorrelationContext correlationContext = new();
        AuditEventFactory auditEventFactory = new(correlationContext, TimeProvider.System);
        DefaultHttpContext httpContext = new();
        CorrelationMiddleware middleware = new(
            static _ => Task.CompletedTask,
            NullLogger<CorrelationMiddleware>.Instance);

        await middleware.InvokeAsync(httpContext, correlationContext);
        Data.Model.AuditEvent auditEvent = auditEventFactory.Create("test.event", "success");

        Assert.AreEqual(correlationContext.Id.ToString(), httpContext.Response.Headers["X-Correlation-ID"].ToString());
        Assert.AreEqual(correlationContext.Id, auditEvent.CorrelationId);
    }

    [TestMethod]
    public void LifecycleMetricsAggregateOnlyConfiguredDimensionsProvidedByCaller()
    {
        LifecycleMetrics metrics = new();
        metrics.Record("backup-1", "wake", "success", TimeSpan.FromSeconds(1));
        metrics.Record("backup-1", "wake", "success", TimeSpan.FromSeconds(2));

        IReadOnlyList<LifecycleMetricSnapshot> snapshots = metrics.Snapshot();
        Assert.HasCount(1, snapshots);
        LifecycleMetricSnapshot snapshot = snapshots[0];

        Assert.AreEqual("backup-1", snapshot.TargetId);
        Assert.AreEqual("wake", snapshot.Operation);
        Assert.AreEqual("success", snapshot.Outcome);
        Assert.AreEqual(2, snapshot.Count);
        Assert.AreEqual(3d, snapshot.DurationSeconds);
    }
}
