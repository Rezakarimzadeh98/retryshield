using RetryShield.Application;
using RetryShield.Domain;

namespace RetryShield.Domain.Tests;

public class RecordStateMachineTests
{
    private static IdempotencyRecord NewRecord() =>
        IdempotencyRecord.Create("tenant", "/payments", "key", "fingerprint", DateTimeOffset.UtcNow.AddHours(1));

    [Fact]
    public void New_record_is_processing() => Assert.Equal(RecordState.Processing, NewRecord().State);

    [Fact]
    public void Processing_can_complete_and_records_timeline()
    {
        var record = NewRecord();
        record.Complete(new(201, new Dictionary<string, string[]>(), [1, 2]));
        Assert.Equal(RecordState.Completed, record.State);
        Assert.Equal(2, record.Timeline.Count);
        Assert.Equal(new byte[] { 1, 2 }, record.Response!.Body);
    }

    [Theory]
    [InlineData(RecordState.Completed)]
    [InlineData(RecordState.Failed)]
    public void Terminal_states_can_expire_but_cannot_transition_after_expiry(RecordState terminal)
    {
        var record = NewRecord();
        if (terminal == RecordState.Completed) record.Complete(new(200, new Dictionary<string, string[]>(), []));
        else record.Fail("failed");
        record.Expire();
        Assert.Equal(RecordState.Expired, record.State);
        Assert.Throws<InvalidOperationException>(() => record.Complete(
            new(200, new Dictionary<string, string[]>(), [])));
    }

    [Fact]
    public void Indeterminate_can_be_administratively_resolved()
    {
        var record = NewRecord();
        record.MarkIndeterminate("lost connection");
        record.ResolveCompleted(new(201, new Dictionary<string, string[]>(), [9]));
        Assert.Equal(RecordState.Completed, record.State);
    }

    [Fact]
    public void Indeterminate_can_be_resolved_as_failed()
    {
        var record = NewRecord();
        record.MarkIndeterminate("lost connection");
        record.ResolveFailed(new(409, new Dictionary<string, string[]>(), []));
        Assert.Equal(RecordState.Failed, record.State);
    }

    [Fact]
    public void Fingerprint_is_canonical_and_body_sensitive()
    {
        var first = Fingerprints.Compute("post", "/payments?a=1", "Application/Json; charset=utf-8", [1]);
        var same = Fingerprints.Compute("POST", "/payments?a=1", "application/json", [1]);
        var changed = Fingerprints.Compute("POST", "/payments?a=1", "application/json", [2]);
        Assert.Equal(first, same);
        Assert.NotEqual(first, changed);
    }

    [Fact]
    public void Fingerprint_canonicalizes_query_parameter_order()
    {
        var first = Fingerprints.Compute("POST", "/payments?currency=USD&amount=10", "application/json", []);
        var reordered = Fingerprints.Compute("POST", "/payments?amount=10&currency=USD", "application/json", []);
        Assert.Equal(first, reordered);
    }
}

public class ArchitectureTests
{
    [Fact]
    public void Domain_has_no_project_dependencies()
    {
        var references = typeof(IdempotencyRecord).Assembly.GetReferencedAssemblies().Select(x => x.Name).ToArray();
        Assert.DoesNotContain("RetryShield.Application", references);
        Assert.DoesNotContain("RetryShield.Infrastructure", references);
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure()
    {
        var references = typeof(RetryShieldService).Assembly.GetReferencedAssemblies().Select(x => x.Name).ToArray();
        Assert.DoesNotContain("RetryShield.Infrastructure", references);
    }
}
