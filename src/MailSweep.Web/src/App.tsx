import './App.css'

function App() {
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
          <div>
            <p className="eyebrow">Connection</p>
            <h2 id="connection-heading">No Gmail account connected</h2>
            <p className="supporting-text">
              Gmail connection will be available in a future version.
            </p>
          </div>
          <button type="button" disabled>Connect Gmail</button>
        </section>

        <section className="overview" aria-labelledby="overview-heading">
          <div className="section-heading">
            <h2 id="overview-heading">Mailbox overview</h2>
            <p>Mailbox statistics will appear here after an account is connected.</p>
          </div>
          <dl className="stats-grid">
            <div className="stat-card">
              <dt>Storage used</dt>
              <dd>Not available</dd>
            </div>
            <div className="stat-card">
              <dt>Messages</dt>
              <dd>Not available</dd>
            </div>
            <div className="stat-card">
              <dt>Cleanup candidates</dt>
              <dd>Not available</dd>
            </div>
          </dl>
        </section>
      </main>
    </div>
  )
}

export default App
