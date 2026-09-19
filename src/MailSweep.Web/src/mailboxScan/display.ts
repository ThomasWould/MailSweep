import type { CohortEnumerationStatus, MailboxScanCohort, MailboxScanCohortResult } from './contracts.ts'

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
