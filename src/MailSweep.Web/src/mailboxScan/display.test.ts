import assert from 'node:assert/strict'
import test from 'node:test'
import type { MailboxScanCohortResult, PromotionInsightsSummary } from './contracts.ts'
import {
  enumeratedCohortCount,
  formatBytes,
  observedCountLabel,
  promotionCoverageLabel,
  promotionDomainRows,
  promotionMessageCountLabel,
  promotionSenderRows,
} from './display.ts'

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

const promotionInsights: PromotionInsightsSummary = {
  analyzedMessageCount: 3,
  analyzedMessageBytes: 3_500,
  unknownSenderMessageCount: 1,
  unknownSenderMessageBytes: 500,
  topSenders: [
    { emailAddress: 'offers@example.com', domain: 'example.com', messageCount: 2, estimatedBytes: 3_000 },
  ],
  topDomains: [
    { domain: 'example.com', messageCount: 2, estimatedBytes: 3_000 },
  ],
}

test('promotion coverage wording identifies the bounded analyzed sample', () => {
  assert.equal(
    promotionCoverageLabel(promotionInsights, 1_250),
    'Based on 3 analyzed promotion messages selected across 1,250 observed old-promotion candidates in this bounded scan.',
  )
})

test('promotion insight rows display sender, domain, counts, sizes, and unknown senders', () => {
  assert.deepEqual(promotionSenderRows(promotionInsights), [
    { key: 'offers@example.com', label: 'offers@example.com', detail: 'example.com', messageCount: 2, estimatedBytes: 3_000 },
    { key: 'unknown-sender', label: 'Unknown sender', messageCount: 1, estimatedBytes: 500 },
  ])
  assert.deepEqual(promotionDomainRows(promotionInsights), [
    { key: 'example.com', label: 'example.com', messageCount: 2, estimatedBytes: 3_000 },
  ])
  assert.equal(promotionMessageCountLabel(1), '1 message')
  assert.equal(promotionMessageCountLabel(2), '2 messages')
})
