import type { MailboxScanClient, MailboxScanProgress, MailboxScanSummary } from './contracts.ts'

export class MailboxScanApiError extends Error {
  readonly status: number

  constructor(status: number) {
    super(`Mailbox scan request failed with status ${status}`)
    this.name = 'MailboxScanApiError'
    this.status = status
  }
}

async function readJson<T>(response: Response): Promise<T> {
  if (!response.ok) throw new MailboxScanApiError(response.status)
  return response.json() as Promise<T>
}

export function createMailboxScanClient(baseUrl: string, fetcher: typeof fetch = fetch): MailboxScanClient {
  const request = (path: string, init?: RequestInit) => fetcher(`${baseUrl}${path}`, {
    credentials: 'include',
    cache: 'no-store',
    ...init,
  })

  return {
    async start(signal) {
      return readJson<MailboxScanProgress>(await request('/api/mailbox/scans', { method: 'POST', signal }))
    },
    async getProgress(scanId, signal) {
      return readJson<MailboxScanProgress>(await request(`/api/mailbox/scans/${encodeURIComponent(scanId)}`, { signal }))
    },
    async getSummary(scanId, signal) {
      return readJson<MailboxScanSummary>(await request(`/api/mailbox/scans/${encodeURIComponent(scanId)}/summary`, { signal }))
    },
    async cancel(scanId, signal) {
      return readJson<MailboxScanProgress>(await request(`/api/mailbox/scans/${encodeURIComponent(scanId)}/cancel`, {
        method: 'POST', signal,
      }))
    },
  }
}
