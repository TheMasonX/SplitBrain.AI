using Orchestrator.Core.Models;
using Orchestrator.Infrastructure.Metrics;

namespace Orchestrator.Tests.Infrastructure;

public sealed class MetricsCollectorTests
{
    private readonly InMemoryMetricsCollector _sut = new();

    private static RequestMetric MakeMetric(string nodeId = "A", bool success = true, int latencyMs = 100) =>
        new()
        {
            NodeId    = nodeId,
            Model     = "test-model",
            TaskType  = "Review",
            TokensIn  = 50,
            TokensOut = 100,
            LatencyMs = latencyMs,
            Success   = success
        };

    [Fact]
    public void GetRecent_ReturnsEmptyWhenNoRecords()
    {
        _sut.GetRecent().Should().BeEmpty();
    }

    [Fact]
    public void Record_ThenGetRecent_ContainsEntry()
    {
        _sut.Record(MakeMetric("B"));

        var recent = _sut.GetRecent(10);

        recent.Should().ContainSingle(m => m.NodeId == "B");
    }

    [Fact]
    public void GetSummary_ReturnsEmptySummaryWhenNoRecords()
    {
        var summary = _sut.GetSummary();

        summary.TotalRequests.Should().Be(0);
        summary.SuccessCount.Should().Be(0);
        summary.FailureCount.Should().Be(0);
    }

    [Fact]
    public void GetSummary_AggregatesCorrectly()
    {
        _sut.Record(MakeMetric("A", success: true,  latencyMs: 200));
        _sut.Record(MakeMetric("A", success: true,  latencyMs: 400));
        _sut.Record(MakeMetric("B", success: false, latencyMs: 600));

        var summary = _sut.GetSummary();

        summary.TotalRequests.Should().Be(3);
        summary.SuccessCount.Should().Be(2);
        summary.FailureCount.Should().Be(1);
        summary.AvgLatencyMs.Should().BeApproximately(400.0, 0.01);
        summary.RequestsByNode["A"].Should().Be(2);
        summary.RequestsByNode["B"].Should().Be(1);
    }

    [Fact]
    public void GetRecent_RespectsCountParameter()
    {
        for (var i = 1; i <= 5; i++)
            _sut.Record(MakeMetric(latencyMs: i * 100));

        var recent = _sut.GetRecent(3);

        recent.Should().HaveCount(3);
        // All returned records must come from the recorded set
        var validLatencies = new[] { 100, 200, 300, 400, 500 };
        recent.Select(m => m.LatencyMs).Should().OnlyContain(ms => validLatencies.Contains(ms));
    }

    [Fact]
    public void Record_RingBuffer_DoesNotExceedCapacity()
    {
        for (var i = 0; i < 1_100; i++)
            _sut.Record(MakeMetric());

        var recent = _sut.GetRecent(2_000);

        recent.Should().HaveCountLessThanOrEqualTo(1_000);
    }
}
