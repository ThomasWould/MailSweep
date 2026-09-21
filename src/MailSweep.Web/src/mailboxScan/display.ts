import type {
  CohortEnumerationStatus,
  MailboxScanCohort,
  MailboxScanCohortResult,
  PromotionInsightsSummary,
} from './contracts.ts'

const numberFormatter = new Intl.NumberFormat()
const byteFormatter = new Intl.NumberFormat(undefined, { maximumFractionDigits: 1 })

export const cohortLabels: Record<MailboxScanCohort, string> = {
  largeMail: 'Large messages',
  oldPromotions: 'Old promotions',
  oldUnreadInbox: 'Old unread inbox mail',
}

export function observedCountLabel(cohort: MailboxScanCohortResult): string {
  if (cohort.enumerationStatus === 'notStarted') return 'Not started'
  const count = numberFormatter.format(cohort.observedCandidateCount)
  return cohort.countComplete ? count : `At least ${count}`
}

export function enumerationLabel(status: CohortEnumerationStatus): string {
  switch (status) {
    case 'notStarted': return 'Waiting to enumerate'
    case 'enumerating': return 'Enumeration in progress'
    case 'completed': return 'Count complete'
    case 'truncated': return 'Count limited by scan bounds'
    case 'cancelled': return 'Enumeration cancelled'
  }
}

export function enumeratedCohortCount(cohorts: MailboxScanCohortResult[]): number {
  return cohorts.filter(({ enumerationStatus }) => enumerationStatus === 'completed' || enumerationStatus === 'truncated').length
}

export function formatBytes(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes <= 0) return '0 B'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  const unitIndex = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), units.length - 1)
  return `${byteFormatter.format(bytes / (1024 ** unitIndex))} ${units[unitIndex]}`
}

export interface PromotionInsightRow {
  key: string
  label: string
  detail?: string
  messageCount: number
  estimatedBytes: number
}

export function promotionCoverageLabel(
  insights: PromotionInsightsSummary,
  observedCandidateCount: number,
): string {
  const analyzed = numberFormatter.format(insights.analyzedMessageCount)
  const observed = numberFormatter.format(observedCandidateCount)
  const noun = insights.analyzedMessageCount === 1 ? 'message' : 'messages'
  return `Based on ${analyzed} analyzed promotion ${noun} selected across ${observed} observed old-promotion candidates in this bounded scan.`
}

export function promotionMessageCountLabel(count: number): string {
  return `${numberFormatter.format(count)} ${count === 1 ? 'message' : 'messages'}`
}

export function promotionSenderRows(insights: PromotionInsightsSummary): PromotionInsightRow[] {
  const rows: PromotionInsightRow[] = insights.topSenders.map((sender) => ({
    key: sender.emailAddress,
    label: sender.emailAddress,
    detail: sender.domain,
    messageCount: sender.messageCount,
    estimatedBytes: sender.estimatedBytes,
  }))
  if (insights.unknownSenderMessageCount > 0) {
    rows.push({
      key: 'unknown-sender',
      label: 'Unknown sender',
      messageCount: insights.unknownSenderMessageCount,
      estimatedBytes: insights.unknownSenderMessageBytes,
    })
  }
  return rows
}

export function promotionDomainRows(insights: PromotionInsightsSummary): PromotionInsightRow[] {
  return insights.topDomains.map((domain) => ({
    key: domain.domain,
    label: domain.domain,
    messageCount: domain.messageCount,
    estimatedBytes: domain.estimatedBytes,
  }))
}
