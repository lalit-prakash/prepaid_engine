using PrepaidEngine.Api.Health;

namespace PrepaidEngine.Tests.Auth;

public class WorkerStatusRegistryTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static WorkerStatus Status(DateTime? success, DateTime? failure = null, int every = 60)
        => new("w", every, success, failure, failure is null ? null : "boom", 1, failure is null ? 0 : 1);

    [Fact]
    public void A_worker_that_never_reported_is_not_yet_run()
        => Assert.Equal("NotYetRun", WorkerStatusRegistry.StateOf(Status(null), Now));

    [Fact]
    public void A_worker_that_only_ever_failed_is_failing()
        => Assert.Equal("Failing", WorkerStatusRegistry.StateOf(Status(null, Now.AddSeconds(-5)), Now));

    [Fact]
    public void A_recent_success_is_healthy()
        => Assert.Equal("Healthy", WorkerStatusRegistry.StateOf(Status(Now.AddSeconds(-30)), Now));

    [Fact]
    public void Three_intervals_plus_grace_is_still_healthy_and_beyond_it_is_stale()
    {
        Assert.Equal("Healthy", WorkerStatusRegistry.StateOf(Status(Now.AddSeconds(-(60 * 3 + 29))), Now));
        Assert.Equal("Stale", WorkerStatusRegistry.StateOf(Status(Now.AddSeconds(-(60 * 3 + 31))), Now));
    }

    [Fact]
    public void A_failure_after_the_last_success_is_failing_but_a_later_success_recovers()
    {
        Assert.Equal("Failing", WorkerStatusRegistry.StateOf(Status(Now.AddSeconds(-20), Now.AddSeconds(-10)), Now));
        Assert.Equal("Healthy", WorkerStatusRegistry.StateOf(Status(Now.AddSeconds(-5), Now.AddSeconds(-10)), Now));
    }

    [Fact]
    public void Reports_are_counted_and_the_registry_lists_workers_by_name()
    {
        var registry = new WorkerStatusRegistry();
        registry.Expect("B", 60);
        registry.Success("A", 60);
        registry.Success("A", 60);
        registry.Failure("A", 60, new InvalidOperationException("db down"));

        var snapshot = registry.Snapshot();

        Assert.Equal(new[] { "A", "B" }, snapshot.Select(w => w.Name));
        var a = snapshot[0];
        Assert.Equal(3, a.Runs);
        Assert.Equal(1, a.Failures);
        Assert.Contains("db down", a.LastError);
        Assert.Equal(0, snapshot[1].Runs); // expected but has not run
    }
}
