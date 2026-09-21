import { useEffect, useRef, useState } from 'react'
import type { MailboxScanClient, MailboxScanCohort, MailboxScanProgress, MailboxScanSummary } from './mailboxScan/contracts.ts'
import {
  cohortLabels,
  enumeratedCohortCount,
  enumerationLabel,
  formatBytes,
  observedCountLabel,
  promotionCoverageLabel,
  promotionDomainRows,
  promotionMessageCountLabel,
  promotionSenderRows,
} from './mailboxScan/display.ts'
import type { MockScanScenario } from './mailboxScan/mock.ts'

const numberFormatter = new Intl.NumberFormat()
const dateFormatter = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' })
const allCohorts: MailboxScanCohort[] = ['largeMail', 'oldPromotions', 'oldUnreadInbox']

type ViewState =
  | { kind: 'idle' }
  | { kind: 'starting' }
  | { kind: 'active'; progress: MailboxScanProgress }
  | { kind: 'completed'; progress: MailboxScanProgress; summary: MailboxScanSummary }
  | { kind: 'cancelled'; progress: MailboxScanProgress }
  | { kind: 'failed'; progress?: MailboxScanProgress }

interface MailboxCleanupProps {
  client: MailboxScanClient
  mockScenario?: MockScanScenario
  onMockScenarioChange?: (scenario: MockScanScenario) => void
}

function readableDate(value: string): string {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? 'Date unavailable' : dateFormatter.format(date)
}

function statusReason(reason: string | null | undefined): string {
  switch (reason) {
    case 'gmail_temporarily_unavailable':
    case 'source_unavailable': return 'Gmail is temporarily unavailable. No email was changed.'
    case 'authentication_required': return 'Your Gmail connection needs to be refreshed before another analysis.'
    default: return 'The analysis could not be completed right now. No email was changed.'
  }
}

export default function MailboxCleanup({ client, mockScenario, onMockScenarioChange }: MailboxCleanupProps) {
  const [view, setView] = useState<ViewState>({ kind: 'idle' })
  const [cancelPending, setCancelPending] = useState(false)
  const [cancelError, setCancelError] = useState(false)
  const outcomeHeadingRef = useRef<HTMLHeadingElement>(null)
  const isActive = view.kind === 'active'

  useEffect(() => {
    if (view.kind === 'completed' || view.kind === 'cancelled' || view.kind === 'failed') {
      outcomeHeadingRef.current?.focus()
    }
  }, [view.kind])

  useEffect(() => {
    if (!isActive) return
    const scanId = view.progress.scanId
    const controller = new AbortController()
    const timer = window.setTimeout(async () => {
      try {
        const next = await client.getProgress(scanId, controller.signal)
        if (next.status === 'completed') {
          const scanSummary = await client.getSummary(scanId, controller.signal)
          setView({ kind: 'completed', progress: next, summary: scanSummary })
        } else if (next.status === 'cancelled') {
          setView({ kind: 'cancelled', progress: next })
        } else if (next.status === 'failed') {
          setView({ kind: 'failed', progress: next })
        } else {
          setView({ kind: 'active', progress: next })
        }
      } catch (error) {
        if (!(error instanceof DOMException && error.name === 'AbortError')) setView({ kind: 'failed' })
      }
    }, mockScenario ? 650 : 1_500)
    return () => {
      controller.abort()
      window.clearTimeout(timer)
    }
  }, [client, isActive, mockScenario, view])

  async function startAnalysis() {
    setView({ kind: 'starting' })
    setCancelError(false)
    try {
      const progress = await client.start()
      setView({ kind: 'active', progress })
    } catch {
      setView({ kind: 'failed' })
    }
  }

  async function cancelAnalysis() {
    if (view.kind !== 'active') return
    setCancelPending(true)
    setCancelError(false)
    try {
      const progress = await client.cancel(view.progress.scanId)
      setView({ kind: 'active', progress })
    } catch {
      setCancelError(true)
    } finally {
      setCancelPending(false)
    }
  }

  const currentProgress = view.kind === 'active' || view.kind === 'completed' || view.kind === 'cancelled' || view.kind === 'failed'
    ? view.progress
    : undefined
  const displayCohorts = allCohorts.map((cohort) => currentProgress?.cohorts.find((result) => result.cohort === cohort) ?? {
    cohort, enumerationStatus: 'notStarted' as const, pagesEnumerated: 0, observedCandidateCount: 0,
    enrichedMessages: 0, estimatedMatchingMessageBytes: 0, countComplete: false,
  })
  const promotionInsights = view.kind === 'completed' ? view.summary.promotionInsights : undefined
  const observedPromotionCandidates = view.kind === 'completed'
    ? view.summary.cohorts.find(({ cohort }) => cohort === 'oldPromotions')?.observedCandidateCount ?? 0
    : 0
  const senderInsights = promotionInsights ? promotionSenderRows(promotionInsights) : []
  const domainInsights = promotionInsights ? promotionDomainRows(promotionInsights) : []

  return (
    <section className="cleanup" aria-labelledby="cleanup-heading">
      <div className="cleanup-heading-row">
        <div className="section-heading">
          <p className="eyebrow">Bounded mailbox scan</p>
          <h2 id="cleanup-heading">Mailbox Cleanup</h2>
          <p>Find examples in three focused categories without attempting to inventory your entire mailbox.</p>
        </div>
        {view.kind === 'idle' && <button type="button" onClick={() => void startAnalysis()}>Analyze Gmail</button>}
      </div>

      <div className="safety-note">
        <span aria-hidden="true">✓</span>
        <p><strong>Read-only analysis</strong> — MailSweep will not change or delete email.</p>
      </div>

      {mockScenario && onMockScenarioChange && view.kind === 'idle' && (
        <div className="mock-controls">
          <label htmlFor="mock-scenario">Development preview</label>
          <select id="mock-scenario" value={mockScenario} onChange={(event) => onMockScenarioChange(event.target.value as MockScanScenario)}>
            <option value="all-complete">All cohorts completed</option>
            <option value="promotions-truncated">Promotions count limited</option>
            <option value="temporary-failure">Temporary failure</option>
          </select>
          <span>Mock data — development only</span>
        </div>
      )}

      {view.kind === 'idle' && (
        <div className="cohort-intro-grid">
          {allCohorts.map((cohort) => (
            <article className="cohort-intro" key={cohort}>
              <span className="cohort-icon" aria-hidden="true">{cohort === 'largeMail' ? '↗' : cohort === 'oldPromotions' ? '◇' : '○'}</span>
              <h3>{cohortLabels[cohort]}</h3>
              <p>{cohort === 'largeMail'
                ? 'Messages matching Gmail’s larger-than-25 MB search.'
                : cohort === 'oldPromotions'
                  ? 'Promotions older than two years, within scan limits.'
                  : 'Unread inbox mail older than one year, within scan limits.'}</p>
            </article>
          ))}
        </div>
      )}

      {view.kind === 'starting' && (
        <div className="scan-status" role="status" aria-live="polite">
          <span className="activity-dot" aria-hidden="true" />
          <div><h3>Starting analysis</h3><p>Preparing the three bounded searches. No messages will be changed.</p></div>
        </div>
      )}

      {currentProgress && view.kind !== 'failed' && (
        <>
          <div className="scan-status" role="status" aria-live="polite">
            <span className={view.kind === 'active' ? 'activity-dot' : 'status-symbol'} aria-hidden="true">
              {view.kind === 'completed' ? '✓' : view.kind === 'cancelled' ? '—' : ''}
            </span>
            <div>
              <h3 ref={view.kind === 'completed' || view.kind === 'cancelled' ? outcomeHeadingRef : undefined} tabIndex={-1}>
                {view.kind === 'active'
                  ? currentProgress.status === 'queued' ? 'Analysis queued' : currentProgress.stage === 'enriching' ? 'Reviewing matching message details' : 'Enumerating bounded cohorts'
                  : view.kind === 'completed' ? 'Analysis complete' : 'Analysis cancelled'}
              </h3>
              <p>{view.kind === 'active'
                ? 'Progress uses observed work only; there is no estimated time or mailbox-wide percentage.'
                : view.kind === 'completed' ? 'Results reflect the messages MailSweep actually observed and analyzed.' : 'Partial observations are shown below. No email was changed.'}</p>
            </div>
            {view.kind === 'active' && (
              <button type="button" className="secondary-button" onClick={() => void cancelAnalysis()} disabled={cancelPending}>
                {cancelPending ? 'Cancelling…' : 'Cancel Analysis'}
              </button>
            )}
          </div>
          {cancelError && <p className="error-text" role="alert">Cancellation could not be requested. The analysis may still be running; try again.</p>}

          <dl className="progress-grid" aria-label="Analysis progress">
            <div><dt>Cohorts</dt><dd>{enumeratedCohortCount(currentProgress.cohorts)} of 3 enumerated</dd></div>
            <div><dt>Messages observed</dt><dd>{numberFormatter.format(currentProgress.uniqueIdsEnumerated)}{view.kind === 'active' ? '+' : ''}</dd></div>
            <div><dt>Messages analyzed</dt><dd>{numberFormatter.format(currentProgress.getSucceeded)} / {currentProgress.getAttemptBudget} max</dd></div>
            <div><dt>Get budget</dt><dd>{currentProgress.getAttemptsUsed} / {currentProgress.getAttemptBudget}</dd></div>
          </dl>

          <div className="cohort-results-grid">
            {displayCohorts.map((cohort) => (
              <article className="cohort-result" key={cohort.cohort}>
                <div className="cohort-result-heading"><h3>{cohortLabels[cohort.cohort]}</h3><span>{enumerationLabel(cohort.enumerationStatus)}</span></div>
                <p className="candidate-count">{observedCountLabel(cohort)}</p>
                <p className="candidate-label">observed candidates</p>
                <dl>
                  <div><dt>Pages enumerated</dt><dd>{cohort.pagesEnumerated}</dd></div>
                  <div><dt>Examples analyzed</dt><dd>{cohort.enrichedMessages}</dd></div>
                </dl>
              </article>
            ))}
          </div>
        </>
      )}

      {view.kind === 'completed' && (
        <div className="results">
          <div className="result-summary">
            <div><p className="eyebrow">Analyzed examples</p><p className="size-total">{formatBytes(view.summary.estimatedMatchingMessageBytes)}</p></div>
            <p>This is the estimated size of matching messages actually analyzed. It is <strong>not necessarily the total reclaimable space</strong> in Gmail.</p>
          </div>
          <div className="coverage-note">
            <h3>What this scan covered</h3>
            <p>MailSweep checked three bounded search cohorts and analyzed {numberFormatter.format(view.summary.getSucceeded)} examples using {view.summary.getAttemptsUsed} of {view.summary.getAttemptBudget} metadata attempts. {view.summary.limitedByBudget ? 'At least one result count was limited by the scan bounds.' : 'All three observed candidate counts completed within the scan bounds.'} This is not an exhaustive mailbox inventory.</p>
          </div>
          {promotionInsights && (
            <section className="promotion-insights" aria-labelledby="promotion-insights-heading">
              <div className="promotion-insights-heading">
                <div>
                  <p className="eyebrow">Bounded-scan coverage</p>
                  <h3 id="promotion-insights-heading">Promotion Insights</h3>
                </div>
                <p>{formatBytes(promotionInsights.analyzedMessageBytes)} analyzed</p>
              </div>
              <p className="promotion-coverage">
                {promotionCoverageLabel(promotionInsights, observedPromotionCandidates)} The selection is deterministic, not random or mailbox-wide.
              </p>
              {promotionInsights.analyzedMessageCount > 0 ? (
                <div className="promotion-rankings">
                  <article>
                    <h4>Frequent senders</h4>
                    <ol className="insight-list">
                      {senderInsights.map((sender) => (
                        <li key={sender.key}>
                          <span><strong>{sender.label}</strong>{sender.detail && <small>{sender.detail}</small>}</span>
                          <span>{promotionMessageCountLabel(sender.messageCount)} · {formatBytes(sender.estimatedBytes)}</span>
                        </li>
                      ))}
                    </ol>
                  </article>
                  <article>
                    <h4>Frequent domains</h4>
                    {domainInsights.length > 0 ? (
                      <ol className="insight-list">
                        {domainInsights.map((domain) => (
                          <li key={domain.key}>
                            <strong>{domain.label}</strong>
                            <span>{promotionMessageCountLabel(domain.messageCount)} · {formatBytes(domain.estimatedBytes)}</span>
                          </li>
                        ))}
                      </ol>
                    ) : <p className="empty-insights">No parseable sender domains were found in this sample.</p>}
                  </article>
                </div>
              ) : <p className="empty-insights">No old-promotion messages were successfully analyzed in this bounded scan.</p>}
            </section>
          )}
          <h3 className="previews-heading">Matching examples</h3>
          {view.summary.messagePreviews.length > 0 ? (
            <div className="preview-table-wrap">
              <table className="preview-table">
                <thead><tr><th scope="col">Sender and subject</th><th scope="col">Date</th><th scope="col">Estimated size</th><th scope="col">Categories</th></tr></thead>
                <tbody>{view.summary.messagePreviews.map((message) => (
                  <tr key={message.messageId}>
                    <td><span className="sender">{message.from || 'Sender unavailable'}</span><span className="subject">{message.subject || 'Subject unavailable'}</span></td>
                    <td>{readableDate(message.receivedAt)}</td>
                    <td>{formatBytes(message.estimatedBytes)}</td>
                    <td><div className="badges">{message.matchingCohorts.map((cohort) => <span className="badge" key={cohort}>{cohortLabels[cohort]}</span>)}</div></td>
                  </tr>
                ))}</tbody>
              </table>
            </div>
          ) : <p className="empty-results">No matching previews were available within this bounded analysis.</p>}
          <button type="button" className="secondary-button analyze-again" onClick={() => setView({ kind: 'idle' })}>Analyze again</button>
        </div>
      )}

      {view.kind === 'cancelled' && (
        <div className="outcome-actions"><button type="button" onClick={() => void startAnalysis()}>Start a new analysis</button></div>
      )}

      {view.kind === 'failed' && (
        <div className="failure-panel" role="alert">
          <h3 ref={outcomeHeadingRef} tabIndex={-1}>Analysis stopped</h3>
          <p>{statusReason(view.progress?.statusReason)}</p>
          <button type="button" onClick={() => void startAnalysis()}>Try again</button>
        </div>
      )}
    </section>
  )
}
