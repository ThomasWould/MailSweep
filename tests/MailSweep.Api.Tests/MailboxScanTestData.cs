using MailSweep.Api.Mailbox.Scanning;
using Microsoft.Extensions.Time.Testing;

namespace MailSweep.Api.Tests;

internal sealed class ScanHarness
{
    public static readonly DateTimeOffset Started = new(2026, 9, 17, 16, 0, 0, TimeSpan.Zero);
    public FakeTimeProvider Clock { get; } = new(Started);
    public List<TimeSpan> Delays { get; } = [];
    public FakeScanSource Source { get; } = new();

    public Task DelayAsync(TimeSpan delay, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Delays.Add(delay);
        Clock.Advance(delay);
        token.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public MailboxScanEngine Engine => new(Clock, new(() => 0), DelayAsync);
    public MailboxScanService Service => new(Clock, new(() => 0), DelayAsync);
    public Task<MailboxScanOutcome> RunAsync(CancellationToken token = default) =>
        Engine.RunAsync(Guid.NewGuid(), Source, 72_640, cancellationToken: token);

    public static MailboxMessageMetadata Metadata(string id, long? size = 10,
        DateTimeOffset? received = null, string[]? labels = null) => new(id, $"thread-{id}",
            received ?? Started.AddYears(-3), size, labels ?? ["CATEGORY_PROMOTIONS", "INBOX", "UNREAD"],
            "Sender <sender@example.test>", "Subject");

    public static string[] Ids(string prefix, int count) => Enumerable.Range(0, count).Select(i => $"{prefix}{i}").ToArray();
}

internal sealed class FakeScanSource : IMailboxScanSource
{
    public List<MailboxListRequest> Lists { get; } = [];
    public List<string> Gets { get; } = [];
    public Func<MailboxListRequest, CancellationToken, Task<MailboxIdPage>> List { get; set; } =
        (_, _) => Task.FromResult(new MailboxIdPage([], null));
    public Func<string, CancellationToken, Task<MailboxMessageMetadata>> Get { get; set; } =
        (id, _) => Task.FromResult(ScanHarness.Metadata(id));

    public void Cohorts(string[] large, string[]? promotions = null, string[]? unread = null) =>
        List = (request, _) => Task.FromResult(new MailboxIdPage(
            request.Query.StartsWith("larger:", StringComparison.Ordinal) ? large :
            request.Query.StartsWith("category:", StringComparison.Ordinal) ? promotions ?? [] : unread ?? [], null));

    public Task<MailboxIdPage> ListAsync(MailboxListRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Lists.Add(request);
        return List(request, cancellationToken);
    }

    public Task<MailboxMessageMetadata> GetMetadataAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Gets.Add(id);
        return Get(id, cancellationToken);
    }
}
