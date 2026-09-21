using MailSweep.Api.Mailbox.Contracts;

namespace MailSweep.Api.Mailbox.Scanning;

public sealed record MailboxScanOutcome(MailboxScanProgress Progress, MailboxScanSummary? Summary);

/// <summary>Pure, bounded metadata scan. All mutable state belongs to one async invocation.</summary>
public sealed class MailboxScanEngine(TimeProvider? timeProvider = null, MailboxRetryPolicy? retryPolicy = null,
    Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly MailboxRetryPolicy retries = retryPolicy ?? new();

    public Task<MailboxScanOutcome> RunAsync(Guid scanId, IMailboxScanSource source,
        long profileMessageCount, Action<MailboxScanProgress>? report = null,
        CancellationToken cancellationToken = default) =>
        new Run(scanId, source, profileMessageCount, clock, retries,
            delayAsync ?? ((delay, token) => Task.Delay(delay, clock, token)), report).ExecuteAsync(cancellationToken);

    private sealed class Cohort(MailboxCohortQuery query)
    {
        public MailboxCohortQuery Query { get; } = query;
        public CohortEnumerationStatus Status { get; set; }
        public int Pages { get; set; }
        public int Attempts { get; set; }
        public int Observed { get; set; }
        public List<string> Ids { get; } = [];
        public int Enriched { get; set; }
        public long Bytes { get; set; }
        public MailboxScanCohortResult Snapshot() => new(Query.Cohort, Status, Pages, Observed, Enriched, Bytes);
    }

    private sealed class Run(Guid scanId, IMailboxScanSource source, long profileCount,
        TimeProvider clock, MailboxRetryPolicy retries, Func<TimeSpan, CancellationToken, Task> delayAsync,
        Action<MailboxScanProgress>? report)
    {
        private readonly MailboxScanPlan plan = new(clock.GetUtcNow());
        private readonly Dictionary<string, int> membership = new(StringComparer.Ordinal);
        private readonly HashSet<string> attempted = new(StringComparer.Ordinal);
        private readonly List<MailboxMessagePreview> previews = [];
        private readonly PromotionInsightsAggregator promotionInsights = new();
        private Cohort[] cohorts = [];
        private MailboxScanStatus status = MailboxScanStatus.Running;
        private MailboxScanStage? stage = MailboxScanStage.Enumerating;
        private int getAttempts;
        private int succeeded;
        private long bytes;
        private bool limited;
        private string? reason;
        private DateTimeOffset? finished;
        private long cooldownStarted;
        private TimeSpan cooldown;

        public async Task<MailboxScanOutcome> ExecuteAsync(CancellationToken cancellationToken)
        {
            cohorts = plan.Queries.Select(q => new Cohort(q)).ToArray();
            using var deadline = new CancellationTokenSource(MailboxScanPlan.JobDuration, clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
            using var limiter = new MailboxAttemptLimiter(clock, delayAsync);
            try
            {
                Publish();
                for (var i = 0; i < cohorts.Length; i++)
                {
                    linked.Token.ThrowIfCancellationRequested();
                    await EnumerateAsync(i, linked.Token);
                }
                linked.Token.ThrowIfCancellationRequested();
                stage = MailboxScanStage.Enriching;
                Publish();
                // Reserve attempts, not successful messages. Overlapping IDs spend one owner's slots.
                for (var i = 0; i < cohorts.Length; i++)
                    await EnrichPassAsync(i, getAttempts + cohorts[i].Query.ReservedAttempts, limiter, linked.Token);
                for (var i = 0; i < cohorts.Length; i++)
                    await EnrichPassAsync(i, MailboxScanPlan.GetAttemptBudget, limiter, linked.Token);
                if (attempted.Count < membership.Count)
                    Limit("get_attempt_limit_reached");
                linked.Token.ThrowIfCancellationRequested();
                status = MailboxScanStatus.Completed;
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    status = MailboxScanStatus.Cancelled;
                    reason = "cancelled";
                    MarkActive(CohortEnumerationStatus.Cancelled);
                }
                else
                {
                    status = MailboxScanStatus.Completed;
                    Limit("scan_deadline_reached");
                    MarkActive(CohortEnumerationStatus.Truncated);
                }
            }
            catch (MailboxSourceException ex)
            {
                status = MailboxScanStatus.Failed;
                reason = ex.Failure switch
                {
                    MailboxSourceFailure.Authentication => "authentication_required",
                    MailboxSourceFailure.Permission => "permission_denied",
                    MailboxSourceFailure.InvalidRequest => "invalid_source_request",
                    _ => "source_unavailable"
                };
                MarkActive(CohortEnumerationStatus.Truncated);
            }
            catch (Exception)
            {
                status = MailboxScanStatus.Failed;
                reason = "scan_failed";
                MarkActive(CohortEnumerationStatus.Truncated);
            }
            stage = null;
            finished = clock.GetUtcNow();
            var progress = Snapshot();
            report?.Invoke(progress);
            var summary = status == MailboxScanStatus.Completed
                ? new MailboxScanSummary(scanId, "bounded-v1", profileCount, progress.Cohorts,
                    membership.Count, attempted.Count, getAttempts, MailboxScanPlan.GetAttemptBudget,
                    succeeded, bytes, limited, promotionInsights.Snapshot(), previews.AsReadOnly(), finished.Value)
                : null;
            return new(progress, summary);
        }

        private async Task EnumerateAsync(int index, CancellationToken jobToken)
        {
            var cohort = cohorts[index];
            cohort.Status = CohortEnumerationStatus.Enumerating;
            Publish();
            using var deadline = new CancellationTokenSource(MailboxScanPlan.CohortDuration, clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(jobToken, deadline.Token);
            string? pageToken = null;
            try
            {
                while (true)
                {
                    MailboxIdPage? page = null;
                    for (var retry = 1; retry <= 3; retry++)
                    {
                        linked.Token.ThrowIfCancellationRequested();
                        if (cohort.Attempts >= MailboxScanPlan.ListAttemptBudget)
                        {
                            Truncate(cohort, "cohort_list_attempt_limit_reached");
                            return;
                        }
                        await WaitForCooldownAsync(linked.Token);
                        cohort.Attempts++;
                        try
                        {
                            page = await source.ListAsync(new(cohort.Query.Query, pageToken), linked.Token);
                            linked.Token.ThrowIfCancellationRequested();
                            break;
                        }
                        catch (MailboxSourceException ex) when (ex.Failure == MailboxSourceFailure.Transient)
                        {
                            if (cohort.Attempts == MailboxScanPlan.ListAttemptBudget)
                            {
                                await BackoffAsync(retry, ex.RetryAfter, linked.Token);
                                Truncate(cohort, "cohort_list_attempt_limit_reached");
                                return;
                            }
                            if (retry == 3)
                            {
                                // Respect the source's cooldown before moving to the next cohort too.
                                await BackoffAsync(retry, ex.RetryAfter, linked.Token);
                                cohort.Status = CohortEnumerationStatus.Truncated;
                                reason ??= "cohort_source_unavailable";
                                Publish();
                                return;
                            }
                            await BackoffAsync(retry, ex.RetryAfter, linked.Token);
                        }
                    }
                    cohort.Pages++;
                    var bit = 1 << index;
                    var droppedId = false;
                    foreach (var id in page!.MessageIds)
                    {
                        linked.Token.ThrowIfCancellationRequested();
                        if (string.IsNullOrWhiteSpace(id))
                            throw new MailboxSourceException(MailboxSourceFailure.InvalidRequest);
                        membership.TryGetValue(id, out var mask);
                        if ((mask & bit) != 0) continue;
                        if (cohort.Observed == MailboxScanPlan.IdBudget)
                        {
                            droppedId = true;
                            break;
                        }
                        membership[id] = mask | bit;
                        cohort.Ids.Add(id);
                        cohort.Observed++;
                    }
                    pageToken = page.NextPageToken;
                    if (droppedId)
                        Truncate(cohort, "cohort_id_limit_reached");
                    else if (string.IsNullOrEmpty(pageToken))
                        cohort.Status = CohortEnumerationStatus.Completed;
                    else if (cohort.Observed == MailboxScanPlan.IdBudget)
                        Truncate(cohort, "cohort_id_limit_reached");
                    else if (cohort.Pages == MailboxScanPlan.PageBudget)
                        Truncate(cohort, "cohort_page_limit_reached");
                    Publish();
                    if (cohort.Status != CohortEnumerationStatus.Enumerating) return;
                }
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested && !jobToken.IsCancellationRequested)
            {
                Truncate(cohort, "cohort_time_limit_reached");
            }
        }

        private async Task EnrichPassAsync(int index, int passLimit, IMailboxAttemptLimiter limiter,
            CancellationToken cancellationToken)
        {
            var limit = Math.Min(passLimit, MailboxScanPlan.GetAttemptBudget);
            var ids = cohorts[index].Query.Cohort == MailboxScanCohort.OldPromotions
                ? MailboxScanSampling.DistributedOrder(cohorts[index].Ids)
                : cohorts[index].Ids;
            foreach (var id in ids)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (getAttempts >= limit) return;
                if ((membership[id] & (1 << index)) == 0 || attempted.Contains(id)) continue;
                for (var retry = 1; retry <= 3; retry++)
                {
                    await WaitForCooldownAsync(cancellationToken);
                    await limiter.WaitAsync(cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    attempted.Add(id);
                    getAttempts++; // Charged immediately before the source's single outgoing attempt.
                    Publish();
                    try
                    {
                        var metadata = await source.GetMetadataAsync(id, cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        Aggregate(id, metadata);
                        break;
                    }
                    catch (MailboxSourceException ex) when (ex.Failure is
                        MailboxSourceFailure.Transient or MailboxSourceFailure.Unavailable)
                    {
                        if (ex.Failure == MailboxSourceFailure.Unavailable) break;
                        var retryThisId = retry < 3 && getAttempts < limit;
                        if (retry < 3 && getAttempts >= limit)
                        {
                            Limit("enrichment_reservation_limit_reached");
                        }
                        // A rate limit pauses the worker even when this ID has exhausted its retries
                        // or reservation. Otherwise the next ID could ignore Retry-After entirely.
                        if (getAttempts < MailboxScanPlan.GetAttemptBudget &&
                            (retryThisId || attempted.Count < membership.Count))
                            await BackoffAsync(retry, ex.RetryAfter, cancellationToken);
                        if (!retryThisId) break;
                    }
                }
                Publish();
            }
        }

        private void Aggregate(string id, MailboxMessageMetadata metadata)
        {
            // Required contract fields are never invented. Missing size/date means unavailable preview.
            if (metadata.MessageId != id || string.IsNullOrWhiteSpace(metadata.ThreadId) ||
                metadata.ReceivedAt is not { } received || metadata.EstimatedBytes is not >= 0)
                return;
            succeeded++;
            var labels = new HashSet<string>(metadata.Labels, StringComparer.Ordinal);
            if (labels.Overlaps(["STARRED", "IMPORTANT", "SENT", "DRAFT", "SPAM", "TRASH"])) return;
            var matches = new List<MailboxScanCohort>(3);
            for (var i = 0; i < cohorts.Length; i++)
            {
                if ((membership[id] & (1 << i)) == 0) continue;
                var matchesNow = cohorts[i].Query.Cohort switch
                {
                    MailboxScanCohort.LargeMail => true, // Search predicate; sizeEstimate is separate accounting.
                    MailboxScanCohort.OldPromotions => labels.Contains("CATEGORY_PROMOTIONS") &&
                        received < DateTimeOffset.FromUnixTimeSeconds(plan.TwoYearCutoff),
                    MailboxScanCohort.OldUnreadInbox => labels.Contains("INBOX") && labels.Contains("UNREAD") &&
                        received < DateTimeOffset.FromUnixTimeSeconds(plan.OneYearCutoff),
                    _ => false
                };
                if (!matchesNow) continue;
                matches.Add(cohorts[i].Query.Cohort);
                cohorts[i].Enriched++;
                cohorts[i].Bytes = checked(cohorts[i].Bytes + metadata.EstimatedBytes.Value);
            }
            if (matches.Count == 0) return;
            bytes = checked(bytes + metadata.EstimatedBytes.Value);
            if (matches.Contains(MailboxScanCohort.OldPromotions))
                promotionInsights.Add(metadata.From, metadata.EstimatedBytes.Value);
            previews.Add(new(id, metadata.ThreadId, matches.AsReadOnly(), received,
                metadata.EstimatedBytes.Value, metadata.From, metadata.Subject));
        }

        private void Limit(string code) { limited = true; reason ??= code; }
        private Task BackoffAsync(int failedAttempt, TimeSpan? retryAfter, CancellationToken token)
        {
            cooldownStarted = clock.GetTimestamp();
            cooldown = retries.GetDelay(failedAttempt, retryAfter);
            return WaitForCooldownAsync(token);
        }

        private async Task WaitForCooldownAsync(CancellationToken token)
        {
            // A cohort deadline may interrupt its wait. Carry any remaining cooldown into the
            // next cohort and into enrichment instead of issuing another request too soon.
            var remaining = cooldown - clock.GetElapsedTime(cooldownStarted);
            if (remaining > TimeSpan.Zero) await delayAsync(remaining, token);
            token.ThrowIfCancellationRequested();
        }

        private void Truncate(Cohort cohort, string code)
        {
            cohort.Status = CohortEnumerationStatus.Truncated;
            Limit(code);
            Publish();
        }
        private void MarkActive(CohortEnumerationStatus value)
        {
            foreach (var cohort in cohorts.Where(c => c.Status == CohortEnumerationStatus.Enumerating))
                cohort.Status = value;
        }
        private MailboxScanProgress Snapshot() => new(scanId, status, stage,
            Array.AsReadOnly(cohorts.Select(c => c.Snapshot()).ToArray()), membership.Count,
            attempted.Count, getAttempts, MailboxScanPlan.GetAttemptBudget, succeeded, limited,
            plan.StartedAt, finished, reason);
        private void Publish() => report?.Invoke(Snapshot());
    }
}
