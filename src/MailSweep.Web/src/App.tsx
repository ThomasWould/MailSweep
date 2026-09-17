import { useEffect, useState } from 'react'
import './App.css'

const apiBaseUrl = 'https://localhost:7119'
const numberFormatter = new Intl.NumberFormat()

type AuthStatus = {
  connected: boolean
  emailAddress: string | null
}

type GmailProfile = {
  emailAddress: string
  messagesTotal: number
  threadsTotal: number
}

type ConnectionState =
  | { kind: 'loading' }
  | { kind: 'disconnected' }
  | { kind: 'connected'; emailAddress: string }
  | { kind: 'error' }

type ProfileState =
  | { kind: 'idle' }
  | { kind: 'loading' }
  | { kind: 'ready'; profile: GmailProfile }
  | { kind: 'error' }

function App() {
  const [connection, setConnection] = useState<ConnectionState>({ kind: 'loading' })
  const [profile, setProfile] = useState<ProfileState>({ kind: 'idle' })
  const [statusAttempt, setStatusAttempt] = useState(0)
  const [profileAttempt, setProfileAttempt] = useState(0)
  const [disconnecting, setDisconnecting] = useState(false)
  const [disconnectError, setDisconnectError] = useState(false)
  const connectedEmail = connection.kind === 'connected' ? connection.emailAddress : null
  const statPlaceholder = connectedEmail && profile.kind !== 'error' ? 'Loading…' : '—'

  useEffect(() => {
    const url = new URL(window.location.href)
    if (url.searchParams.get('gmailConnected') === 'true') {
      url.searchParams.delete('gmailConnected')
      window.history.replaceState(window.history.state, '', url)
    }
  }, [])

  useEffect(() => {
    const controller = new AbortController()

    async function loadStatus() {
      try {
        const response = await fetch(`${apiBaseUrl}/api/auth/status`, {
          credentials: 'include',
          signal: controller.signal,
        })
        if (!response.ok) throw new Error('Connection check failed')

        const status = (await response.json()) as AuthStatus
        if (controller.signal.aborted) return
        if (status.connected && status.emailAddress) {
          setConnection({ kind: 'connected', emailAddress: status.emailAddress })
        } else if (!status.connected) {
          setConnection({ kind: 'disconnected' })
        } else {
          setConnection({ kind: 'error' })
        }
      } catch {
        if (!controller.signal.aborted) setConnection({ kind: 'error' })
      }
    }

    void loadStatus()
    return () => controller.abort()
  }, [statusAttempt])

  useEffect(() => {
    if (!connectedEmail) return
    const controller = new AbortController()

    async function loadProfile() {
      setProfile({ kind: 'loading' })
      try {
        const response = await fetch(`${apiBaseUrl}/api/gmail/profile`, {
          credentials: 'include',
          signal: controller.signal,
        })
        if (response.status === 401) {
          if (!controller.signal.aborted) {
            setConnection({ kind: 'disconnected' })
            setProfile({ kind: 'idle' })
          }
          return
        }
        if (!response.ok) throw new Error('Profile request failed')

        const gmailProfile = (await response.json()) as GmailProfile
        if (!controller.signal.aborted) setProfile({ kind: 'ready', profile: gmailProfile })
      } catch {
        if (!controller.signal.aborted) setProfile({ kind: 'error' })
      }
    }

    void loadProfile()
    return () => controller.abort()
  }, [connectedEmail, profileAttempt])

  function connectGmail() {
    window.location.assign(`${apiBaseUrl}/api/auth/google/connect`)
  }

  async function disconnectGmail() {
    setDisconnecting(true)
    setDisconnectError(false)
    try {
      const response = await fetch(`${apiBaseUrl}/api/auth/logout`, {
        method: 'POST',
        credentials: 'include',
        headers: { 'X-MailSweep-Request': 'logout' },
      })
      if (!response.ok && response.status !== 401) throw new Error('Logout failed')
      setConnection({ kind: 'disconnected' })
      setProfile({ kind: 'idle' })
    } catch {
      setDisconnectError(true)
    } finally {
      setDisconnecting(false)
    }
  }

  function retryStatus() {
    setConnection({ kind: 'loading' })
    setStatusAttempt((attempt) => attempt + 1)
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
        <section className="panel connection-panel" aria-labelledby="connection-heading">
          <div aria-live="polite">
            <p className="eyebrow">Connection</p>
            {connection.kind === 'loading' && (
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
                <h2 id="connection-heading">Connected to {connection.emailAddress}</h2>
                <p className="supporting-text">MailSweep has read-only Gmail access.</p>
              </>
            )}
            {connection.kind === 'error' && (
              <>
                <h2 id="connection-heading">Connection status unavailable</h2>
                <p className="supporting-text" role="alert">
                  Check that the API is running and its HTTPS certificate is trusted.
                </p>
              </>
            )}
            {disconnectError && (
              <p className="error-text" role="alert">Could not disconnect. Please try again.</p>
            )}
          </div>

          {connection.kind === 'disconnected' && (
            <button type="button" onClick={connectGmail}>Connect Gmail</button>
          )}
          {connection.kind === 'connected' && (
            <button type="button" className="secondary-button" onClick={disconnectGmail} disabled={disconnecting}>
              {disconnecting ? 'Disconnecting…' : 'Disconnect'}
            </button>
          )}
          {connection.kind === 'error' && (
            <button type="button" className="secondary-button" onClick={retryStatus}>Retry</button>
          )}
        </section>

        <section className="overview" aria-labelledby="overview-heading">
          <div className="section-heading">
            <h2 id="overview-heading">Mailbox overview</h2>
            <p>
              {connectedEmail
                ? 'Basic totals from your Gmail profile. No messages are opened or changed.'
                : 'Connect Gmail to see basic mailbox totals.'}
            </p>
          </div>
          <dl className="stats-grid" aria-live="polite">
            <div className="stat-card">
              <dt>Total messages</dt>
              <dd>{profile.kind === 'ready' && connectedEmail
                ? numberFormatter.format(profile.profile.messagesTotal)
                : statPlaceholder}</dd>
            </div>
            <div className="stat-card">
              <dt>Total threads</dt>
              <dd>{profile.kind === 'ready' && connectedEmail
                ? numberFormatter.format(profile.profile.threadsTotal)
                : statPlaceholder}</dd>
            </div>
          </dl>
          {connectedEmail && profile.kind === 'error' && (
            <div className="profile-error" role="alert">
              <p>Could not load Gmail profile totals.</p>
              <button type="button" className="text-button" onClick={() => setProfileAttempt((attempt) => attempt + 1)}>
                Try again
              </button>
            </div>
          )}
        </section>
      </main>
    </div>
  )
}

export default App
