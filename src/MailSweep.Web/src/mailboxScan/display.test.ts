import assert from 'node:assert/strict'
import test from 'node:test'
import type { MailboxScanCohortResult } from './contracts.ts'
import { enumeratedCohortCount, formatBytes, observedCountLabel } from './display.ts'

function cohort(overrides: Partial<MailboxScanCohortResult> = {}): MailboxScanCohortResult {
  return {
    cohort: 'oldPromotions',
    enumerationStatus: 'completed',
    pagesEnumerated: 1,
    observedCandidateCount: 1_250,
    enrichedMessages: 4,
    estimatedMatchingMessageBytes: 2_000,
    countComplete: true,
    ...overrides,
  }
}

test('complete observations render as a count', () => {
  assert.equal(observedCountLabel(cohort()), '1,250')
})

test('truncated observations are explicitly lower bounds', () => {
  assert.equal(observedCountLabel(cohort({ enumerationStatus: 'truncated', countComplete: false })), 'At least 1,250')
})

test('only completed and truncated cohorts count as enumerated', () => {
  assert.equal(enumeratedCohortCount([
    cohort(),
    cohort({ cohort: 'largeMail', enumerationStatus: 'truncated', countComplete: false }),
    cohort({ cohort: 'oldUnreadInbox', enumerationStatus: 'enumerating', countComplete: false }),
  ]), 2)
})

test('analyzed bytes use readable binary units without implying recoverability', () => {
  assert.equal(formatBytes(1_478_700_000), '1.4 GB')
  assert.equal(formatBytes(0), '0 B')
})
