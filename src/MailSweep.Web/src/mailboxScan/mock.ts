import type {
  MailboxMessagePreview,
  MailboxScanClient,
  MailboxScanCohortResult,
  MailboxScanProgress,
  MailboxScanSummary,
} from './contracts.ts'

export type MockScanScenario = 'all-complete' | 'promotions-truncated' | 'temporary-failure'

const scanId = '712055fc-649f-4429-9f1c-a2e65ce4a1eb'
const startedAt = '2026-09-17T16:00:00Z'

const initialCohorts: MailboxScanCohortResult[] = [
  { cohort: 'largeMail', enumerationStatus: 'notStarted', pagesEnumerated: 0, observedCandidateCount: 0, enrichedMessages: 0, estimatedMatchingMessageBytes: 0, countComplete: false },
  { cohort: 'oldPromotions', enumerationStatus: 'notStarted', pagesEnumerated: 0, observedCandidateCount: 0, enrichedMessages: 0, estimatedMatchingMessageBytes: 0, countComplete: false },
  { cohort: 'oldUnreadInbox', enumerationStatus: 'notStarted', pagesEnumerated: 0, observedCandidateCount: 0, enrichedMessages: 0, estimatedMatchingMessageBytes: 0, countComplete: false },
]

function progress(overrides: Partial<MailboxScanProgress> = {}): MailboxScanProgress {
  return {
    scanId,
    status: 'running',
    stage: 'enumerating',
    cohorts: initialCohorts,
    uniqueIdsEnumerated: 0,
    messagesAttempted: 0,
    getAttemptsUsed: 0,
    getAttemptBudget: 100,
    getSucceeded: 0,
    limitedByBudget: false,
    startedAt,
    finishedAt: null,
    statusReason: null,
    ...overrides,
  }
}

const completedCohorts: MailboxScanCohortResult[] = [
  { cohort: 'largeMail', enumerationStatus: 'completed', pagesEnumerated: 2, observedCandidateCount: 570, enrichedMessages: 35, estimatedMatchingMessageBytes: 1_470_000_000, countComplete: true },
  { cohort: 'oldPromotions', enumerationStatus: 'completed', pagesEnumerated: 7, observedCandidateCount: 3_241, enrichedMessages: 20, estimatedMatchingMessageBytes: 12_400_000, countComplete: true },
  { cohort: 'oldUnreadInbox', enumerationStatus: 'completed', pagesEnumerated: 3, observedCandidateCount: 1_030, enrichedMessages: 10, estimatedMatchingMessageBytes: 3_100_000, countComplete: true },
]

const truncatedCohorts: MailboxScanCohortResult[] = completedCohorts.map((cohort) => cohort.cohort === 'oldPromotions'
  ? { ...cohort, enumerationStatus: 'truncated', pagesEnumerated: 20, observedCandidateCount: 10_000, countComplete: false }
  : cohort)

const previews: MailboxMessagePreview[] = [
  { messageId: '18ab123', threadId: '18ab000', matchingCohorts: ['largeMail'], receivedAt: '2025-04-01T12:00:00Z', estimatedBytes: 27_800_000, from: 'Example News <news@example.com>', subject: 'Monthly update' },
  { messageId: '18ab456', threadId: '18ab444', matchingCohorts: ['oldPromotions'], receivedAt: '2023-07-14T15:32:00Z', estimatedBytes: 843_000, from: null, subject: 'A summer offer' },
  { messageId: '18ab789', threadId: '18ab777', matchingCohorts: ['oldPromotions', 'oldUnreadInbox'], receivedAt: '2023-02-02T09:11:00Z', estimatedBytes: 216_000, from: 'Updates <hello@example.org>', subject: null },
]

function summary(cohorts: MailboxScanCohortResult[], limitedByBudget: boolean): MailboxScanSummary {
  return {
    scanId,
    ruleVersion: 'bounded-v1',
    profileMessageCount: 72_640,
    cohorts,
    uniqueIdsEnumerated: limitedByBudget ? 11_420 : 4_381,
    messagesAttempted: 65,
    getAttemptsUsed: 67,
    getAttemptBudget: 100,
    getSucceeded: 64,
    estimatedMatchingMessageBytes: 1_478_700_000,
    limitedByBudget,
    messagePreviews: previews,
    completedAt: '2026-09-17T16:02:18Z',
  }
}

export function createMockMailboxScanClient(getScenario: () => MockScanScenario): MailboxScanClient {
  let poll = 0
  let cancelled = false
  let activeScenario: MockScanScenario = 'promotions-truncated'

  return {
    async start() {
      poll = 0
      cancelled = false
      activeScenario = getScenario()
      return progress({ status: 'queued', stage: null })
    },
    async getProgress() {
      poll += 1
      if (cancelled) return progress({ status: 'cancelled', stage: null, finishedAt: '2026-09-17T16:00:42Z', statusReason: 'cancelled_by_user' })
      if (activeScenario === 'temporary-failure' && poll >= 3) {
        return progress({ status: 'failed', stage: null, cohorts: completedCohorts.map((cohort, index) => index === 0 ? cohort : initialCohorts[index]), uniqueIdsEnumerated: 570, finishedAt: '2026-09-17T16:00:31Z', statusReason: 'gmail_temporarily_unavailable' })
      }
      if (poll === 1) {
        return progress({ cohorts: [
          { ...completedCohorts[0], enumerationStatus: 'enumerating', pagesEnumerated: 1, observedCandidateCount: 314, enrichedMessages: 0, estimatedMatchingMessageBytes: 0, countComplete: false },
          initialCohorts[1], initialCohorts[2],
        ], uniqueIdsEnumerated: 314 })
      }
      if (poll === 2) {
        return progress({ cohorts: [completedCohorts[0], { ...completedCohorts[1], enumerationStatus: 'enumerating', pagesEnumerated: 4, observedCandidateCount: 1_870, enrichedMessages: 0, estimatedMatchingMessageBytes: 0, countComplete: false }, initialCohorts[2]], uniqueIdsEnumerated: 2_330 })
      }
      if (poll === 3) {
        return progress({ stage: 'enriching', cohorts: activeScenario === 'promotions-truncated' ? truncatedCohorts : completedCohorts, uniqueIdsEnumerated: activeScenario === 'promotions-truncated' ? 11_420 : 4_381, messagesAttempted: 31, getAttemptsUsed: 32, getSucceeded: 30, limitedByBudget: activeScenario === 'promotions-truncated' })
      }
      const cohorts = activeScenario === 'promotions-truncated' ? truncatedCohorts : completedCohorts
      return progress({ status: 'completed', stage: null, cohorts, uniqueIdsEnumerated: activeScenario === 'promotions-truncated' ? 11_420 : 4_381, messagesAttempted: 65, getAttemptsUsed: 67, getSucceeded: 64, limitedByBudget: activeScenario === 'promotions-truncated', finishedAt: '2026-09-17T16:02:18Z', statusReason: activeScenario === 'promotions-truncated' ? 'cohort_page_limit_reached' : null })
    },
    async getSummary() {
      const limited = activeScenario === 'promotions-truncated'
      return summary(limited ? truncatedCohorts : completedCohorts, limited)
    },
    async cancel() {
      cancelled = true
      return progress({ status: 'running', stage: 'enumerating', statusReason: 'cancellation_requested' })
    },
  }
}
