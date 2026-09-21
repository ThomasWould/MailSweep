import { useEffect, useMemo, useState } from 'react'
import './App.css'
import MailboxCleanup from './MailboxCleanup.tsx'
import { createMailboxScanClient } from './mailboxScan/api.ts'
import { createMockMailboxScanClient, type MockScanScenario } from './mailboxScan/mock.ts'

const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL || 'https://localhost:7119').replace(/\/$/, '')
const numberFormatter = new Intl.NumberFormat()
const mockScannerEnabled = import.meta.env.DEV && import.meta.env.VITE_MAILBOX_SCAN_MOCK === 'true'
const liveScanClient = createMailboxScanClient(apiBaseUrl)

type AuthStatus = { authenticated: boolean; emailAddress: string | null }
type GmailProfile = { emailAddress: string; messagesTotal: number; threadsTotal: number }
type ConnectionState =
  | { kind: 'loading' }
  | { kind: 'disconnected' }
  | { kind: 'checking'; emailAddress: string }
  | { kind: 'connected'; profile: GmailProfile }
  | { kind: 'reconnect'; emailAddress: string }
  | { kind: 'error'; stage: 'status' | 'profile'; emailAddress?: string }

function App() {
  const [connection, setConnection] = useState<ConnectionState>(() => mockScannerEnabled
    ? { kind: 'connected', profile: { emailAddress: 'demo@mailsweep.local', messagesTotal: 72_640, threadsTotal: 67_185 } }
    : { kind: 'loading' })
  const [mockScenario, setMockScenario] = useState<MockScanScenario>('promotions-truncated')
  const mockScanClient = useMemo(() => mockScannerEnabled
    ? createMockMailboxScanClient(() => mockScenario)
    : null, [mockScenario])
  const scanClient = mockScanClient ?? liveScanClient
  const [statusAttempt, setStatusAttempt] = useState(0)
  const [profileAttempt, setProfileAttempt] = useState(0)
  const [disconnecting, setDisconnecting] = useState(false)
  const [disconnectError, setDisconnectError] = useState(false)
  const [authNotice, setAuthNotice] = useState(() => {
    const code = new URLSearchParams(window.location.search).get('auth')
    return code === 'denied' || code === 'failed' ? code : null
  })
  const connectedProfile = connection.kind === 'connected' ? connection.profile : null
  const statPlaceholder = connection.kind === 'loading' || connection.kind === 'checking' ? 'Loading…' : '—'

  useEffect(() => {
    const url = new URL(window.location.href)
    if (url.searchParams.has('auth')) {
      url.searchParams.delete('auth')
      window.history.replaceState(window.history.state, '', url)
    }
  }, [])

  useEffect(() => {
    if (mockScannerEnabled) return
    const controller = new AbortController()
    async function loadStatus() {
      try {
        const response = await fetch(`${apiBaseUrl}/api/auth/status`, {
          credentials: 'include', signal: controller.signal,
        })
        if (!response.ok) throw new Error('Connection check failed')
        const status = (await response.json()) as AuthStatus
        if (controller.signal.aborted) return
        if (status.authenticated && status.emailAddress) {
          setConnection({ kind: 'checking', emailAddress: status.emailAddress })
        } else if (!status.authenticated) {
          setConnection({ kind: 'disconnected' })
        } else {
          setConnection({ kind: 'error', stage: 'status' })
        }
      } catch {
        if (!controller.signal.aborted) setConnection({ kind: 'error', stage: 'status' })
      }
    }
    void loadStatus()
    return () => controller.abort()
  }, [statusAttempt])

  useEffect(() => {
    if (mockScannerEnabled) return
    if (connection.kind !== 'checking') return
    const emailAddress = connection.emailAddress
    const controller = new AbortController()
    async function loadProfile() {
      try {
        const response = await fetch(`${apiBaseUrl}/api/gmail/profile`, {
          credentials: 'include', signal: controller.signal,
        })
        if (controller.signal.aborted) return
        if (response.status === 401) {
          setConnection({ kind: 'disconnected' })
          return
        }
        if (response.status === 409) {
          setConnection({ kind: 'reconnect', emailAddress })
          return
        }
        if (!response.ok) throw new Error('Profile request failed')
        const profile = (await response.json()) as GmailProfile
        if (!controller.signal.aborted) setConnection({ kind: 'connected', profile })
      } catch {
        if (!controller.signal.aborted) setConnection({ kind: 'error', stage: 'profile', emailAddress })
      }
    }
    void loadProfile()
    return () => controller.abort()
  }, [connection, profileAttempt])

  function connectGmail() {
    window.location.assign(`${apiBaseUrl}/api/auth/google/connect`)
  }

  async function disconnectGmail() {
    setDisconnecting(true)
    setDisconnectError(false)
    try {
      const response = await fetch(`${apiBaseUrl}/api/auth/logout`, {
        method: 'POST', credentials: 'include',
      })
      if (!response.ok && response.status !== 401) throw new Error('Logout failed')
      setConnection({ kind: 'disconnected' })
    } catch {
      setDisconnectError(true)
    } finally {
      setDisconnecting(false)
    }
  }

  function retry() {
    if (connection.kind === 'error' && connection.stage === 'profile' && connection.emailAddress) {
      setConnection({ kind: 'checking', emailAddress: connection.emailAddress })
      setProfileAttempt((attempt) => attempt + 1)
    } else {
      setConnection({ kind: 'loading' })
      setStatusAttempt((attempt) => attempt + 1)
    }
  }

  return (
    <div className="app-shell">
      <header className="page-header">
        <div className="brand">
          <span className="brand-mark" aria-hidden="true">✦</span>
          <h1>MailSweep</h1>
        </div>
        <p className="subtitle">Clean up Gmail without losing what matters.</p>
      </header>
      <main>
        {authNotice && (
          <p className="auth-notice" role="alert">
            {authNotice === 'denied'
              ? 'Google access was not approved. Reconnect when you are ready.'
              : 'Google sign-in could not be completed. Please try connecting again.'}
            <button type="button" className="text-button" onClick={() => setAuthNotice(null)}>Dismiss</button>
          </p>
        )}
        <section className="panel connection-panel" aria-labelledby="connection-heading">
          <div aria-live="polite">
            <p className="eyebrow">Connection</p>
            {(connection.kind === 'loading' || connection.kind === 'checking') && (
              <>
                <h2 id="connection-heading">Checking Gmail connection</h2>
                <p className="supporting-text">Please wait a moment.</p>
              </>
            )}
            {connection.kind === 'disconnected' && (
              <>
                <h2 id="connection-heading">No Gmail account connected</h2>
                <p className="supporting-text">Connect to view basic mailbox totals.</p>
              </>
            )}
            {connection.kind === 'connected' && (
              <>
                <h2 id="connection-heading">Connected to {connection.profile.emailAddress}</h2>
                <p className="supporting-text">{mockScannerEnabled ? 'Development mock mode — no Gmail connection is used.' : 'MailSweep has read-only Gmail access.'}</p>
              </>
            )}
            {connection.kind === 'reconnect' && (
              <>
                <h2 id="connection-heading">Reconnect Gmail</h2>
                <p className="supporting-text">The saved Google access for {connection.emailAddress} is no longer usable. Reconnect to view mailbox totals.</p>
              </>
            )}
            {connection.kind === 'error' && (
              <>
                <h2 id="connection-heading">
                  {connection.stage === 'profile' ? 'Gmail temporarily unavailable' : 'Connection check unavailable'}
                </h2>
                <p className="supporting-text" role="alert">
                  {connection.stage === 'profile'
                    ? 'Gmail could not be reached right now. Please try again.'
                    : 'Check that the API is running and its HTTPS certificate is trusted.'}
                </p>
              </>
            )}
            {disconnectError && <p className="error-text" role="alert">Could not disconnect. Please try again.</p>}
          </div>
          {connection.kind === 'disconnected' && <button type="button" onClick={connectGmail}>Connect Gmail</button>}
          {connection.kind === 'reconnect' && <button type="button" onClick={connectGmail}>Reconnect Gmail</button>}
          {connection.kind === 'connected' && !mockScannerEnabled && (
            <button type="button" className="secondary-button" onClick={disconnectGmail} disabled={disconnecting}>
              {disconnecting ? 'Disconnecting…' : 'Disconnect'}
            </button>
          )}
          {connection.kind === 'error' && <button type="button" className="secondary-button" onClick={retry}>Retry</button>}
        </section>
        <section className="overview" aria-labelledby="overview-heading">
          <div className="section-heading">
            <h2 id="overview-heading">Mailbox overview</h2>
            <p>{connectedProfile
              ? 'Basic totals from your Gmail profile. No messages are opened or changed.'
              : 'Connect Gmail to see basic mailbox totals.'}</p>
          </div>
          <dl className="stats-grid" aria-live="polite">
            <div className="stat-card">
              <dt>Total messages</dt>
              <dd>{connectedProfile ? numberFormatter.format(connectedProfile.messagesTotal) : statPlaceholder}</dd>
            </div>
            <div className="stat-card">
              <dt>Total threads</dt>
              <dd>{connectedProfile ? numberFormatter.format(connectedProfile.threadsTotal) : statPlaceholder}</dd>
            </div>
          </dl>
        </section>
        {connectedProfile && (
          <MailboxCleanup
            client={scanClient}
            mockScenario={mockScannerEnabled ? mockScenario : undefined}
            onMockScenarioChange={mockScannerEnabled ? setMockScenario : undefined}
          />
        )}
      </main>
    </div>
  )
}

export default App
