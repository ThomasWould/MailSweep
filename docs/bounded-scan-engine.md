# Bounded V1 scan engine integration

`Mailbox/Scanning` implements the pure engine and an optional instance-local job service.
There is no route registration, credential handling, Google SDK dependency in this layer,
database, or frontend change. Use `MailboxScanEngine.RunAsync` directly for fake-data
testing, or register `MailboxScanService` as a singleton during API integration.

## Source boundary

Implement `IMailboxScanSource` with `ListAsync(MailboxListRequest, CancellationToken)`
and `GetMetadataAsync(messageId, CancellationToken)`. Each call represents **one** source
attempt. Disable SDK/HTTP automatic retries; all retries must come through the engine.
Honor the supplied token during network calls and waits. The adapter must remain usable
for the job lifetime outside its starting HTTP request. The adapter/integration layer
owns its credentials and disposal.

List requests specify the exact query, page token, 500 maximum results and no spam/trash.
Return IDs and the next token; no result estimate is accepted as a count. Return normalized
metadata only: ID, thread ID, received timestamp, nullable size estimate, labels, From,
and Subject. Do not return bodies or retain SDK response objects. The real adapter must
perform the metadata-field gate described in the scanner design before choosing a single
retrieval format. This branch performs no live Google requests.

Map 429, retryable 5xx, rate-limit-specific 403, and appropriate transient transport
failures to `MailboxSourceFailure.Transient`. Supply a relative `RetryAfter` duration
when known. Map missing messages to `Unavailable`; authentication, other permission
failures and deterministic bad requests have separate non-retryable classifications.
Raw exception messages never appear in progress. Unknown exceptions fail with a safe
`scan_failed` code. Missing or invalid required metadata yields no preview and does not
increment `GetSucceeded`; the current preview contract requires both date and size.

The supplied profile count is context obtained by the integration layer; the engine
does not fetch it or use it as a denominator. Service lookups require the account key
and scan ID. Future routes still need authenticated ownership, CSRF protection, and
no-store response headers.

## Execution and limits

The plan freezes UTC start time and calendar-year cutoffs expressed as invariant Unix
seconds. All three cohorts enumerate before enrichment. Each keeps distinct observed
counts, returned-page order, and global membership. Incomplete enumeration never exposes
`CountComplete=true`; consumers must render its observed count as “at least N.” A
`NotStarted` cohort is unknown, not an observed zero.

Every cohort stops at 20 returned pages, 24 list attempts, 10,000 distinct IDs, or
45 seconds. Reaching the actual end page exactly at a page/ID cap is still complete.
The job has a five-minute cancellation deadline. A deadline returns `Completed` with
`LimitedByBudget=true`, preserving observations; later unvisited cohorts stay `NotStarted`.
An exhausted transient list failure is truncated with `cohort_source_unavailable`;
this is not itself a budget limit.

One async metadata worker uses a shared per-job limiter, spacing attempts by at least
500 ms including an initial wait. The reserved passes spend up to 60/25/15 attempts,
including retries. Unused slots then go to remaining IDs in cohort priority order.
Shared IDs are attempted under one owner and credited to every still-matching cohort.
A failed ID is not selected again by another pass; retries are bounded at three total
attempts and by its owner's remaining reservation. The hard union cap is 100 attempts.
Backoff starts at one second, doubles, adds injectable jitter, and honors longer
Retry-After values. A transient metadata failure also pauses subsequent IDs when its
own retry allowance is exhausted.

The limiter is deliberately scoped to this pure V1 job. A real integration must also
coordinate account/project quota across jobs and other API calls, and charge list/profile
quota. This branch makes no claim of enforcing shared project-wide Google quotas.

Valid metadata increments `GetSucceeded`. Protected labels, spam/trash, and changed
age/category/unread predicates exclude mismatches from enriched counts and bytes. The
large-mail search predicate is not reconstructed from `sizeEstimate`. Overall matching
bytes count each matching ID once; cohort subtotals may overlap. No count or size is
extrapolated. `LimitedByBudget` does not become true just because the hundredth attempt
finished the last candidate.

## Job lifetime and synchronization

`Start`, `GetProgress`, `GetResult`, and `CancelAsync` expose a short-lived job.
Start returns queued state, with one active scan per account and a maximum of four active
jobs per service. There is no queue or `Task.Run` worker. The service holds at most 64 jobs
including retained terminal jobs and rejects admission at capacity. Finished jobs expire
after 30 minutes using monotonic elapsed time; starts and lookups remove expired jobs.
Idle memory remains bounded even without cleanup traffic. Restart loses all jobs.

Progress and results are immutable records with read-only copied collections, published
under a short lock. No source operation or cancellation callback runs under that lock.
Cancellation belongs to the job, not a status request. `CancelAsync` requests cancellation
and awaits worker shutdown. Service disposal cancels and awaits all workers and disposes
their cancellation sources. The source must cooperate with cancellation.

`TimeProvider` drives timers and monotonic pacing. An optional async delay delegate and
jitter hook allow fake-time tests to advance immediately; production uses `Task.Delay`
with the same provider. Tests use only fake mailbox data and never load user secrets.
