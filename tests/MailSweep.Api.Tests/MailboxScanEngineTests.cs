using System.Globalization;
using MailSweep.Api.Mailbox.Contracts;
using MailSweep.Api.Mailbox.Scanning;

namespace MailSweep.Api.Tests;

public sealed class MailboxScanEngineTests
{
    [Fact]
    public void QueriesUseExactFrozenUtcEpochSecondsAndFixedReservations()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            var plan = new MailboxScanPlan(new(2024, 2, 29, 23, 59, 59, TimeSpan.FromHours(-5)));
            Assert.Equal(new DateTimeOffset(2024, 3, 1, 4, 59, 59, TimeSpan.Zero), plan.StartedAt);
            Assert.Equal(1646110799, plan.TwoYearCutoff);
            Assert.Equal(1677646799, plan.OneYearCutoff);
            Assert.Equal(new[]
            {
                "larger:25M -is:starred -is:important -in:sent -in:drafts",
                "category:promotions before:1646110799 -is:starred -is:important -in:sent -in:drafts",
                "is:unread in:inbox before:1677646799 -is:starred -is:important -in:sent -in:drafts"
            }, plan.Queries.Select(q => q.Query));
            Assert.Equal(new[] { 60, 25, 15 }, plan.Queries.Select(q => q.ReservedAttempts));
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
    }

    [Fact]
    public async Task PaginationPreservesQueriesAndFinishesAllListsBeforeAnyGet()
    {
        var h = new ScanHarness();
        h.Source.List = (request, _) =>
        {
            h.Clock.Advance(TimeSpan.FromSeconds(2));
            return Task.FromResult(request.PageToken is null
                ? new MailboxIdPage(["a", "a"], "next") : new MailboxIdPage(["a", "b"], null));
        };
        h.Source.Get = (id, _) =>
        {
            Assert.Equal(6, h.Source.Lists.Count);
            return Task.FromResult(ScanHarness.Metadata(id));
        };
        var outcome = await h.RunAsync();
        Assert.Equal(MailboxScanStatus.Completed, outcome.Progress.Status);
        Assert.Equal(2, outcome.Progress.UniqueIdsEnumerated);
        Assert.Equal(2, h.Source.Gets.Count);
        Assert.All(outcome.Progress.Cohorts, c =>
        {
            Assert.True(c.CountComplete);
            Assert.Equal(2, c.PagesEnumerated);
            Assert.Equal(2, c.ObservedCandidateCount);
        });
        var plan = new MailboxScanPlan(ScanHarness.Started);
        Assert.Equal(plan.Queries.SelectMany(q => new[] { q.Query, q.Query }), h.Source.Lists.Select(r => r.Query));
        Assert.All(h.Source.Lists, r => { Assert.Equal(500, r.MaxResults); Assert.False(r.IncludeSpamTrash); });
        Assert.Equal(new string?[] { null, "next", null, "next", null, "next" }, h.Source.Lists.Select(r => r.PageToken));
    }

    [Theory]
    [InlineData(1, "cohort_page_limit_reached", 1)]
    [InlineData(500, "cohort_id_limit_reached", 10000)]
    public async Task EnumerationCapsProduceAtLeastCounts(int perPage, string reason, int observed)
    {
        var h = new ScanHarness();
        h.Source.List = (request, _) =>
        {
            var page = int.Parse(request.PageToken ?? "0", CultureInfo.InvariantCulture);
            return Task.FromResult(new MailboxIdPage(perPage == 1 ? ["same"] :
                ScanHarness.Ids($"{request.Query[0]}-{page}-", perPage), (page + 1).ToString(CultureInfo.InvariantCulture)));
        };
        var outcome = await h.RunAsync();
        Assert.Equal(60, h.Source.Lists.Count);
        Assert.True(outcome.Progress.LimitedByBudget);
        Assert.Equal(reason, outcome.Progress.StatusReason);
        Assert.All(outcome.Summary!.Cohorts, c =>
        {
            Assert.Equal(20, c.PagesEnumerated);
            Assert.Equal(observed, c.ObservedCandidateCount);
            Assert.Equal(CohortEnumerationStatus.Truncated, c.EnumerationStatus);
            Assert.False(c.CountComplete);
        });
    }

    [Fact]
    public async Task FinalPageAtPageAndIdLimitsStillHasCompleteCount()
    {
        var h = new ScanHarness();
        h.Source.List = (request, _) =>
        {
            var page = int.Parse(request.PageToken ?? "0", CultureInfo.InvariantCulture);
            return Task.FromResult(new MailboxIdPage(ScanHarness.Ids($"{request.Query[0]}-{page}-", 500),
                page == 19 ? null : (page + 1).ToString(CultureInfo.InvariantCulture)));
        };
        var outcome = await h.RunAsync();
        Assert.All(outcome.Progress.Cohorts, c => { Assert.True(c.CountComplete); Assert.Equal(10000, c.ObservedCandidateCount); });
        Assert.True(outcome.Progress.LimitedByBudget); // Only enrichment is capped.
        Assert.Equal("get_attempt_limit_reached", outcome.Progress.StatusReason);
    }

    [Fact]
    public async Task OversizedSourcePageCannotExceedDistinctIdCap()
    {
        var h = new ScanHarness();
        h.Source.Cohorts(ScanHarness.Ids("large", 10001));
        var result = (await h.RunAsync()).Progress;
        Assert.Equal(10000, result.UniqueIdsEnumerated);
        Assert.False(result.Cohorts[0].CountComplete);
        Assert.Equal("cohort_id_limit_reached", result.StatusReason);
    }

    [Fact]
    public async Task ListRetriesConsumeThe24AttemptBudgetPerCohort()
    {
        var h = new ScanHarness();
        h.Source.List = (_, _) => h.Source.Lists.Count % 3 != 0
            ? throw new MailboxSourceException(MailboxSourceFailure.Transient)
            : Task.FromResult(new MailboxIdPage(["a"], "next"));
        var result = (await h.RunAsync()).Progress;
        Assert.Equal(72, h.Source.Lists.Count);
        Assert.All(result.Cohorts, c => { Assert.Equal(8, c.PagesEnumerated); Assert.False(c.CountComplete); });
        Assert.Equal("cohort_list_attempt_limit_reached", result.StatusReason);
        Assert.True(result.LimitedByBudget);
    }

    [Fact]
    public async Task RepeatedListFailureStopsAfterThreeAttemptsAndContinuesOtherCohorts()
    {
        var h = new ScanHarness();
        h.Source.List = (request, _) => request.Query.StartsWith("larger:", StringComparison.Ordinal)
            ? throw new MailboxSourceException(MailboxSourceFailure.Transient)
            : Task.FromResult(new MailboxIdPage([], null));
        var result = (await h.RunAsync()).Progress;
        Assert.Equal(5, h.Source.Lists.Count);
        Assert.Equal(CohortEnumerationStatus.Truncated, result.Cohorts[0].EnumerationStatus);
        Assert.False(result.Cohorts[0].CountComplete);
        Assert.True(result.Cohorts[1].CountComplete);
        Assert.Equal(MailboxScanStatus.Completed, result.Status);
    }

    [Fact]
    public async Task CohortDeadlineIncludesRequestsAndReturnsPartialObservations()
    {
        var h = new ScanHarness();
        h.Source.List = (_, token) =>
        {
            h.Clock.Advance(TimeSpan.FromSeconds(23));
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new MailboxIdPage(["a"], "next"));
        };
        var result = (await h.RunAsync()).Progress;
        Assert.Equal(6, h.Source.Lists.Count);
        Assert.All(result.Cohorts, c =>
        {
            Assert.Equal(1, c.ObservedCandidateCount);
            Assert.Equal(1, c.PagesEnumerated);
            Assert.False(c.CountComplete);
        });
        Assert.Equal("cohort_time_limit_reached", result.StatusReason);
        Assert.True(result.LimitedByBudget);
    }

    [Fact]
    public async Task FiveMinuteDeadlineDuringEnumerationLeavesLaterCohortsNotStarted()
    {
        var h = new ScanHarness();
        h.Source.List = (_, token) =>
        {
            h.Clock.Advance(TimeSpan.FromMinutes(5));
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException();
        };
        var outcome = await h.RunAsync();
        Assert.Single(h.Source.Lists);
        Assert.Empty(h.Source.Gets);
        Assert.Equal(MailboxScanStatus.Completed, outcome.Progress.Status);
        Assert.True(outcome.Progress.LimitedByBudget);
        Assert.Equal("scan_deadline_reached", outcome.Progress.StatusReason);
        Assert.Equal(CohortEnumerationStatus.Truncated, outcome.Progress.Cohorts[0].EnumerationStatus);
        Assert.All(outcome.Progress.Cohorts.Skip(1), c => Assert.Equal(CohortEnumerationStatus.NotStarted, c.EnumerationStatus));
        Assert.NotNull(outcome.Summary);
    }

    [Fact]
    public async Task DeadlineDuringMetadataKeepsSuccessfulBytesAndCompleteCounts()
    {
        var h = new ScanHarness();
        h.Source.Cohorts(["a", "b", "c"]);
        h.Source.Get = (id, token) =>
        {
            if (id == "b") h.Clock.Advance(TimeSpan.FromMinutes(5));
            token.ThrowIfCancellationRequested();
            return Task.FromResult(ScanHarness.Metadata(id));
        };
        var outcome = await h.RunAsync();
        Assert.Equal(2, outcome.Progress.GetAttemptsUsed);
        Assert.Equal(1, outcome.Progress.GetSucceeded);
        Assert.Equal(10, outcome.Summary!.EstimatedMatchingMessageBytes);
        Assert.All(outcome.Progress.Cohorts, c => Assert.True(c.CountComplete));
        Assert.True(outcome.Progress.LimitedByBudget);
    }

    [Fact]
    public async Task ReservedPassesHonorPriorityAndHardCap()
    {
        var h = new ScanHarness();
        var promotions = ScanHarness.Ids("p", 100);
        h.Source.Cohorts(ScanHarness.Ids("l", 100), promotions, ScanHarness.Ids("u", 100));
        var outcome = await h.RunAsync();
        Assert.Equal(ScanHarness.Ids("l", 60)
            .Concat(MailboxScanSampling.DistributedOrder(promotions).Take(25))
            .Concat(ScanHarness.Ids("u", 15)), h.Source.Gets);
        Assert.Equal(new[] { 60, 25, 15 }, outcome.Progress.Cohorts.Select(c => c.EnrichedMessages));
        Assert.Equal(100, outcome.Progress.GetAttemptsUsed);
        Assert.True(outcome.Progress.LimitedByBudget);
        Assert.Equal(TimeSpan.FromSeconds(50), h.Clock.GetUtcNow() - ScanHarness.Started);
    }

    [Fact]
    public async Task UnusedReservationFlowsBackInPriorityOrder()
    {
        var h = new ScanHarness();
        h.Source.Cohorts(ScanHarness.Ids("l", 80), [], ScanHarness.Ids("u", 40));
        var outcome = await h.RunAsync();
        Assert.Equal(ScanHarness.Ids("l", 60).Concat(ScanHarness.Ids("u", 15))
            .Concat(Enumerable.Range(60, 20).Select(i => $"l{i}"))
            .Concat(Enumerable.Range(15, 5).Select(i => $"u{i}")), h.Source.Gets);
        Assert.Equal(new[] { 80, 0, 20 }, outcome.Progress.Cohorts.Select(c => c.EnrichedMessages));
    }

    [Fact]
    public async Task OverlappingMembershipFetchesOnceAndBytesAreUnionNotSum()
    {
        var h = new ScanHarness();
        h.Source.Cohorts(["a", "b", "a"], ["b", "c"], ["a", "c"]);
        var outcome = await h.RunAsync();
        Assert.Equal(new[] { "a", "b", "c" }, h.Source.Gets);
        Assert.Equal(3, outcome.Progress.MessagesAttempted);
        Assert.Equal(3, outcome.Progress.GetSucceeded);
        Assert.Equal(30, outcome.Summary!.EstimatedMatchingMessageBytes);
        Assert.All(outcome.Progress.Cohorts, c => { Assert.Equal(2, c.EnrichedMessages); Assert.Equal(20, c.EstimatedMatchingMessageBytes); });
        Assert.All(outcome.Summary.MessagePreviews, p => Assert.Equal(2, p.MatchingCohorts.Count));
        Assert.Equal(2, outcome.Summary.PromotionInsights.AnalyzedMessageCount);
        Assert.Equal(20, outcome.Summary.PromotionInsights.AnalyzedMessageBytes);
        Assert.Equal(2, Assert.Single(outcome.Summary.PromotionInsights.TopSenders).MessageCount);
        Assert.Equal(h.Source.Gets.Count, h.Source.Gets.Distinct(StringComparer.Ordinal).Count());
        Assert.False(outcome.Progress.LimitedByBudget);
    }

    [Fact]
    public async Task RetriesSpendReservedAndGlobalAttemptSlots()
    {
        var h = new ScanHarness();
        h.Source.Cohorts(ScanHarness.Ids("l", 100), ScanHarness.Ids("p", 100), ScanHarness.Ids("u", 100));
        h.Source.Get = (_, _) => throw new MailboxSourceException(MailboxSourceFailure.Transient);
        var result = (await h.RunAsync()).Progress;
        Assert.Equal(100, h.Source.Gets.Count);
        Assert.Equal(100, result.GetAttemptsUsed);
        Assert.Equal(34, result.MessagesAttempted);
        Assert.Equal(60, h.Source.Gets.Count(id => id.StartsWith('l')));
        Assert.Equal(25, h.Source.Gets.Count(id => id.StartsWith('p')));
        Assert.Equal(15, h.Source.Gets.Count(id => id.StartsWith('u')));
        Assert.All(h.Source.Gets.GroupBy(id => id), g => Assert.InRange(g.Count(), 1, 3));
        Assert.True(result.LimitedByBudget);
    }

    [Fact]
    public async Task TransientRetryThenSuccessIsOneEnrichedMessage()
    {
        var h = new ScanHarness();
        h.Source.Cohorts(["a"]);
        h.Source.Get = (id, _) => h.Source.Gets.Count < 3
            ? throw new MailboxSourceException(MailboxSourceFailure.Transient)
            : Task.FromResult(ScanHarness.Metadata(id, 123));
        var outcome = await h.RunAsync();
        Assert.Equal(3, outcome.Progress.GetAttemptsUsed);
        Assert.Equal(1, outcome.Progress.MessagesAttempted);
        Assert.Equal(1, outcome.Progress.GetSucceeded);
        Assert.Equal(123, outcome.Summary!.EstimatedMatchingMessageBytes);
        Assert.False(outcome.Progress.LimitedByBudget);
        Assert.Contains(TimeSpan.FromSeconds(1), h.Delays);
        Assert.Contains(TimeSpan.FromSeconds(2), h.Delays);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(100)]
    public async Task ExactlyExhaustingBudgetWithNoRemainingWorkIsNotLimited(int count)
    {
        var h = new ScanHarness();
        h.Source.Cohorts(ScanHarness.Ids("l", count));
        var result = (await h.RunAsync()).Progress;
        Assert.Equal(count, result.GetAttemptsUsed);
        Assert.False(result.LimitedByBudget);
    }

    [Fact]
    public async Task MissingOrInvalidRequiredFieldsAreNotInvented()
    {
        var h = new ScanHarness();
        h.Source.Cohorts(["size", "date", "negative", "thread", "id", "good"]);
        h.Source.Get = (id, _) => Task.FromResult(id switch
        {
            "size" => ScanHarness.Metadata(id, null),
            "date" => ScanHarness.Metadata(id) with { ReceivedAt = null },
            "negative" => ScanHarness.Metadata(id, -1),
            "thread" => ScanHarness.Metadata(id) with { ThreadId = "" },
            "id" => ScanHarness.Metadata("different"),
            _ => ScanHarness.Metadata(id, 42)
        });
        var outcome = await h.RunAsync();
        Assert.Equal(6, outcome.Progress.GetAttemptsUsed);
        Assert.Equal(1, outcome.Progress.GetSucceeded);
        Assert.Equal(42, outcome.Summary!.EstimatedMatchingMessageBytes);
        Assert.Equal("good", Assert.Single(outcome.Summary.MessagePreviews).MessageId);
    }

    [Theory]
    [InlineData("STARRED")]
    [InlineData("IMPORTANT")]
    [InlineData("SENT")]
    [InlineData("DRAFT")]
    [InlineData("SPAM")]
    [InlineData("TRASH")]
    public async Task ChangedProtectedLabelsExcludeMessageFromAllSubtotals(string label)
    {
        var h = new ScanHarness();
        h.Source.Cohorts(["a"], ["a"], ["a"]);
        h.Source.Get = (id, _) => Task.FromResult(ScanHarness.Metadata(id,
            labels: [label, "CATEGORY_PROMOTIONS", "INBOX", "UNREAD"]));
        var outcome = await h.RunAsync();
        Assert.Equal(1, outcome.Progress.GetSucceeded);
        Assert.Equal(0, outcome.Summary!.EstimatedMatchingMessageBytes);
        Assert.Empty(outcome.Summary.MessagePreviews);
        Assert.All(outcome.Progress.Cohorts, c => Assert.Equal(0, c.EnrichedMessages));
    }

    [Fact]
    public async Task RechecksFrozenAgeAndLabelsButDoesNotInferLargePredicateFromEstimate()
    {
        var h = new ScanHarness();
        h.Source.Cohorts(["a", "b"], ["a", "b", "c"], ["a", "b", "c"]);
        h.Source.Get = (id, _) => Task.FromResult(id switch
        {
            "a" => ScanHarness.Metadata(id, 1, ScanHarness.Started.AddYears(-2)),
            "b" => ScanHarness.Metadata(id, 2, ScanHarness.Started.AddYears(-1)),
            _ => ScanHarness.Metadata(id, labels: [])
        });
        var outcome = await h.RunAsync();
        Assert.Equal(new[] { 2, 0, 1 }, outcome.Progress.Cohorts.Select(c => c.EnrichedMessages));
        Assert.Equal(3, outcome.Summary!.EstimatedMatchingMessageBytes);
        Assert.Equal(new[] { MailboxScanCohort.LargeMail, MailboxScanCohort.OldUnreadInbox },
            outcome.Summary.MessagePreviews[0].MatchingCohorts);
    }

    [Fact]
    public async Task DeletedMessageDoesNotRetryOrPreventOtherPreviews()
    {
        var h = new ScanHarness();
        h.Source.Cohorts(["deleted", "good"]);
        h.Source.Get = (id, _) => id == "deleted"
            ? throw new MailboxSourceException(MailboxSourceFailure.Unavailable)
            : Task.FromResult(ScanHarness.Metadata(id));
        var outcome = await h.RunAsync();
        Assert.Equal(2, h.Source.Gets.Count);
        Assert.Equal(MailboxScanStatus.Completed, outcome.Progress.Status);
        Assert.Equal(1, outcome.Progress.GetSucceeded);
    }

    [Theory]
    [InlineData(MailboxSourceFailure.Authentication, "authentication_required")]
    [InlineData(MailboxSourceFailure.Permission, "permission_denied")]
    [InlineData(MailboxSourceFailure.InvalidRequest, "invalid_source_request")]
    public async Task PermanentErrorsFailSafelyWithoutRetry(MailboxSourceFailure failure, string reason)
    {
        var h = new ScanHarness();
        h.Source.Cohorts(["a", "b"]);
        h.Source.Get = (_, _) => throw new MailboxSourceException(failure);
        var outcome = await h.RunAsync();
        Assert.Single(h.Source.Gets);
        Assert.Equal(MailboxScanStatus.Failed, outcome.Progress.Status);
        Assert.Equal(reason, outcome.Progress.StatusReason);
        Assert.Null(outcome.Summary);
    }

    [Fact]
    public async Task CancellationDuringEnumerationIsNotRetriedAndProducesNoSummary()
    {
        var h = new ScanHarness();
        using var cancellation = new CancellationTokenSource();
        h.Source.List = (_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException();
        };
        var outcome = await h.RunAsync(cancellation.Token);
        Assert.Single(h.Source.Lists);
        Assert.Empty(h.Source.Gets);
        Assert.Equal(MailboxScanStatus.Cancelled, outcome.Progress.Status);
        Assert.Equal(CohortEnumerationStatus.Cancelled, outcome.Progress.Cohorts[0].EnumerationStatus);
        Assert.Null(outcome.Summary);
    }

    [Fact]
    public async Task CancellationDuringGetKeepsAttemptButDoesNotRetry()
    {
        var h = new ScanHarness();
        using var cancellation = new CancellationTokenSource();
        h.Source.Cohorts(["a", "b"]);
        h.Source.Get = (_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException();
        };
        var outcome = await h.RunAsync(cancellation.Token);
        Assert.Single(h.Source.Gets);
        Assert.Equal(1, outcome.Progress.GetAttemptsUsed);
        Assert.Equal(MailboxScanStatus.Cancelled, outcome.Progress.Status);
    }

    [Fact]
    public async Task ProgressSnapshotsCannotBeMutatedAndDoNotChangeAfterPublication()
    {
        var h = new ScanHarness();
        h.Source.Cohorts(["a", "b"]);
        var snapshots = new List<MailboxScanProgress>();
        var outcome = await h.Engine.RunAsync(Guid.NewGuid(), h.Source, 12, snapshots.Add);
        Assert.Equal(0, snapshots[0].UniqueIdsEnumerated);
        Assert.Equal(0, snapshots[0].GetAttemptsUsed);
        Assert.Equal(CohortEnumerationStatus.NotStarted, snapshots[0].Cohorts[0].EnumerationStatus);
        Assert.Throws<NotSupportedException>(() => ((IList<MailboxScanCohortResult>)snapshots[0].Cohorts).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<MailboxMessagePreview>)outcome.Summary!.MessagePreviews).Clear());
        Assert.Equal(12, outcome.Summary!.ProfileMessageCount);
    }
}
