export type MailboxScanStatus = 'queued' | 'running' | 'completed' | 'cancelled' | 'failed'

export type MailboxScanStage = 'enumerating' | 'enriching'

export type MailboxScanCohort = 'largeMail' | 'oldPromotions' | 'oldUnreadInbox'

export type CohortEnumerationStatus = 'notStarted' | 'enumerating' | 'completed' | 'truncated' | 'cancelled'

export interface MailboxScanCohortResult {
  cohort: MailboxScanCohort
  enumerationStatus: CohortEnumerationStatus
  pagesEnumerated: number
  observedCandidateCount: number
  enrichedMessages: number
  estimatedMatchingMessageBytes: number
  countComplete: boolean
}

export interface MailboxScanProgress {
  scanId: string
  status: MailboxScanStatus
  stage: MailboxScanStage | null
  cohorts: MailboxScanCohortResult[]
  uniqueIdsEnumerated: number
  messagesAttempted: number
  getAttemptsUsed: number
  getAttemptBudget: number
  getSucceeded: number
  limitedByBudget: boolean
  startedAt: string | null
  finishedAt: string | null
  statusReason: string | null
}

export interface MailboxMessagePreview {
  messageId: string
  threadId: string
  matchingCohorts: MailboxScanCohort[]
  receivedAt: string
  estimatedBytes: number
  from: string | null
  subject: string | null
}

export interface MailboxScanSummary {
  scanId: string
  ruleVersion: string
  profileMessageCount: number
  cohorts: MailboxScanCohortResult[]
  uniqueIdsEnumerated: number
  messagesAttempted: number
  getAttemptsUsed: number
  getAttemptBudget: number
  getSucceeded: number
  estimatedMatchingMessageBytes: number
  limitedByBudget: boolean
  messagePreviews: MailboxMessagePreview[]
  completedAt: string
}

export interface MailboxScanClient {
  start(signal?: AbortSignal): Promise<MailboxScanProgress>
  getProgress(scanId: string, signal?: AbortSignal): Promise<MailboxScanProgress>
  getSummary(scanId: string, signal?: AbortSignal): Promise<MailboxScanSummary>
  cancel(scanId: string, signal?: AbortSignal): Promise<MailboxScanProgress>
}
