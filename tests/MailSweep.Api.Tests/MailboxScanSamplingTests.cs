using MailSweep.Api.Mailbox.Scanning;

namespace MailSweep.Api.Tests;

public sealed class MailboxScanSamplingTests
{
    [Fact]
    public void ReservedSelectionIsSpreadAcrossLargeObservedCohort()
    {
        var ids = ScanHarness.Ids("p", 10_000);

        var selectedIndexes = MailboxScanSampling.DistributedOrder(ids)
            .Take(25)
            .Select(id => int.Parse(id.AsSpan(1)))
            .ToArray();

        Assert.Equal(25, selectedIndexes.Distinct().Count());
        Assert.NotEqual(Enumerable.Range(0, 25), selectedIndexes);
        Assert.InRange(selectedIndexes.Min(), 0, 999);
        Assert.InRange(selectedIndexes.Max(), 9_000, 9_999);
        Assert.All(Enumerable.Range(0, 4), quarter =>
            Assert.True(selectedIndexes.Count(index => index / 2_500 == quarter) >= 5));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(25)]
    public void SmallCohortsYieldEveryObservedIdExactlyOnce(int count)
    {
        var ids = ScanHarness.Ids("p", count);

        var selected = MailboxScanSampling.DistributedOrder(ids).ToArray();

        Assert.Equal(count, selected.Length);
        Assert.Equal(ids.Order(StringComparer.Ordinal), selected.Order(StringComparer.Ordinal));
        Assert.Equal(count, selected.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void SameObservedOrderProducesSameSelection()
    {
        var ids = ScanHarness.Ids("p", 1_000);

        var first = MailboxScanSampling.DistributedOrder(ids).Take(100).ToArray();
        var second = MailboxScanSampling.DistributedOrder(ids).Take(100).ToArray();

        Assert.Equal(first, second);
    }
}
