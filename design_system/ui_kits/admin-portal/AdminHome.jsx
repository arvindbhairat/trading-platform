// AdminHome.jsx — Legal Posture widget + system health.

const AdminHome = () => (
  <div style={{ padding: 24, display: 'flex', flexDirection: 'column', gap: 18, maxWidth: 1400, margin: '0 auto' }}>
    <div>
      <div style={{ fontSize: 10, fontWeight: 600, letterSpacing: '0.08em', textTransform: 'uppercase', color: 'var(--fg-3)' }}>Admin</div>
      <h1 style={{ margin: 0 }}>Platform overview</h1>
    </div>

    <Card style={{ padding: 20 }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 14 }}>
        <Icon name="shield" size={18} color="var(--warn-500)"/>
        <h2 style={{ margin: 0 }}>Legal Posture</h2>
        <Pill tone="warn">3 items need attention</Pill>
      </div>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 12 }}>
        {[
          { l: 'Operating phase', v: 'A · private eval', tone: 'warn' },
          { l: 'Approved users', v: '18 / 30', tone: null },
          { l: 'FYERS app', v: 'Personal (Phase A)', tone: 'warn' },
          { l: 'SEBI classification', v: 'Pending opinion', tone: 'down' },
          { l: 'ToS version', v: 'v1.2', tone: null },
          { l: 'Privacy Policy', v: 'v1.1', tone: null },
          { l: 'Disclaimer (long)', v: 'v1.0', tone: null },
          { l: 'Tester ack', v: 'v1.3', tone: null },
          { l: 'Calendar coverage', v: '30 / 30 days', tone: 'up' },
          { l: 'Runbooks', v: '3 / 3 · reviewed', tone: 'up' },
          { l: 'Legal review', v: 'Missing · blocks B', tone: 'down' },
          { l: 'Restore drill', v: 'Dec 2025', tone: null },
        ].map((k, i) => (
          <div key={i} style={{ padding: '10px 12px', background: 'var(--bg-1)', border: '1px solid var(--line-1)', borderRadius: 8 }}>
            <div style={{ fontSize: 10, fontWeight: 600, letterSpacing: '0.06em', textTransform: 'uppercase', color: 'var(--fg-3)', marginBottom: 4 }}>{k.l}</div>
            <div style={{ fontSize: 13, fontWeight: 500, color: k.tone === 'up' ? 'var(--up-500)' : k.tone === 'down' ? 'var(--down-500)' : k.tone === 'warn' ? 'var(--warn-500)' : 'var(--fg-1)' }}>{k.v}</div>
          </div>
        ))}
      </div>
    </Card>

    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 14 }}>
      <Card>
        <div style={{ padding: '14px 18px', borderBottom: '1px solid var(--line-1)' }}>
          <h2 style={{ margin: 0 }}>System health · last 24h</h2>
        </div>
        <div style={{ padding: 18, display: 'flex', flexDirection: 'column', gap: 10 }}>
          {[
            ['DataSync (DS)', 'Ran 3:35 PM · 499/500 OK', 'up'],
            ['EOD Signal Runner (EODSR)', 'Ran 3:55 PM · 12 signals emitted', 'up'],
            ['Live Market Data Scan (LMDS)', 'Running · 7 positions watched', 'up'],
            ['Live Account Data Scan (LADS)', 'Running · 18 users polling', 'up'],
            ['Notification Delivery (NDJ)', '247 delivered · 0 retries', 'up'],
            ['FYERS admin token', 'Expires in 4h · re-auth due', 'warn'],
          ].map(([n, s, t], i) => (
            <div key={i} style={{ display: 'flex', alignItems: 'center', gap: 10, padding: '8px 10px', background: 'var(--bg-1)', border: '1px solid var(--line-1)', borderRadius: 6 }}>
              <span style={{ width: 8, height: 8, borderRadius: '50%', background: `var(--${t}-500)`, flexShrink: 0 }}/>
              <span style={{ fontSize: 13, fontWeight: 500, width: 200 }}>{n}</span>
              <span style={{ fontSize: 12, color: 'var(--fg-3)', flex: 1 }}>{s}</span>
            </div>
          ))}
        </div>
      </Card>

      <Card>
        <div style={{ padding: '14px 18px', borderBottom: '1px solid var(--line-1)', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <h2 style={{ margin: 0 }}>Pending approvals</h2>
          <Pill tone="warn">4</Pill>
        </div>
        <div>
          {[
            ['priya.shah@email.com', '2 hr ago'],
            ['rahul.m@gmail.com', '5 hr ago'],
            ['arjun.k@outlook.com', 'Yesterday'],
            ['neha.p@yahoo.in', '2 days ago'],
          ].map(([e, t], i) => (
            <div key={i} style={{ padding: '11px 18px', borderBottom: i < 3 ? '1px solid var(--line-1)' : 'none', display: 'flex', alignItems: 'center', gap: 10 }}>
              <span style={{ flex: 1, fontSize: 13, fontFamily: 'var(--font-mono)' }}>{e}</span>
              <span style={{ fontSize: 11, color: 'var(--fg-3)' }}>{t}</span>
              <Btn size="sm" variant="secondary">Review</Btn>
            </div>
          ))}
        </div>
      </Card>
    </div>
  </div>
);

window.AdminHome = AdminHome;
