// Dashboard.jsx — Portfolio overview with KPI strip, open positions, notifications.

const Dashboard = () => {
  const positions = [
    { sym: 'RELIANCE', entry: 2840, ltp: 2894.2, qty: 50, r: 1.1, stop: 2760, status: 'up' },
    { sym: 'TCS', entry: 3670, ltp: 3724.5, qty: 25, r: 0.8, stop: 3580, status: 'up' },
    { sym: 'ICICIBANK', entry: 1082, ltp: 1042.15, qty: 80, r: -0.7, stop: 1048, status: 'down' },
    { sym: 'INFY', entry: 1785, ltp: 1812.3, qty: 40, r: 0.4, stop: 1720, status: 'up' },
    { sym: 'HDFCBANK', entry: 1526, ltp: 1526.0, qty: 60, r: 0.0, stop: 1480, status: 'flat' },
    { sym: 'LT', entry: 3450, ltp: 3512.8, qty: 20, r: 0.9, stop: 3380, status: 'up' },
    { sym: 'AXISBANK', entry: 1124, ltp: 1098.4, qty: 70, r: -0.5, stop: 1092, status: 'down' },
  ];
  const notifs = [
    { icon: '▲', tone: 'up', title: 'Entry signal · Volume Spike', msg: <>Your scan matched <b>RELIANCE</b> at your configured entry ₹2,840.</>, time: '3:31 PM', unread: true },
    { icon: '▼', tone: 'down', title: 'Stop level breached', msg: <><b>ICICIBANK</b> breached your stop at ₹1,042.</>, time: '2:47 PM', unread: true },
    { icon: 'i', tone: 'info', title: 'DataSync completed', msg: <>499/500 symbols synced.</>, time: '3:35 PM' },
    { icon: '▲', tone: 'up', title: 'Add level reached', msg: <><b>LT</b> reached your first add level — +0.5R.</>, time: '12:14 PM' },
  ];

  return (
    <div style={{ padding: 24, display: 'flex', flexDirection: 'column', gap: 18, maxWidth: 1400, margin: '0 auto' }}>
      <div style={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between' }}>
        <div>
          <div style={{ fontSize: 10, fontWeight: 600, letterSpacing: '0.08em', textTransform: 'uppercase', color: 'var(--fg-3)', marginBottom: 4 }}>Portfolio · 20 Apr 2026</div>
          <h1 style={{ margin: 0 }}>Your overview</h1>
        </div>
        <div style={{ display: 'flex', gap: 8 }}>
          <Btn variant="secondary" icon="activity">Reconcile</Btn>
          <Btn variant="primary" icon="plus">New subscription</Btn>
        </div>
      </div>

      {/* KPI strip */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 12 }}>
        <Card accent="brand" style={{ padding: '16px 20px' }}>
          <div style={{ fontSize: 10, fontWeight: 600, letterSpacing: '0.08em', textTransform: 'uppercase', color: 'var(--fg-3)', marginBottom: 8, display: 'flex', justifyContent: 'space-between' }}>
            Portfolio value <span style={{ color: 'var(--brand-300)' }}>live</span>
          </div>
          <Num value="₹24,81,520" size="xl"/>
          <div style={{ fontFamily: 'var(--font-mono)', fontSize: 12, marginTop: 4, color: 'var(--up-500)' }}>+₹42,180 · +1.73% today</div>
        </Card>
        <Card accent="up" style={{ padding: '16px 20px' }}>
          <div style={{ fontSize: 10, fontWeight: 600, letterSpacing: '0.08em', textTransform: 'uppercase', color: 'var(--fg-3)', marginBottom: 8 }}>XIRR · 90d</div>
          <Num value="+18.4%" size="xl" color="var(--up-500)"/>
          <div style={{ fontFamily: 'var(--font-mono)', fontSize: 12, marginTop: 4, color: 'var(--fg-3)' }}>Nifty 500 +5.1%</div>
        </Card>
        <Card accent="down" style={{ padding: '16px 20px' }}>
          <div style={{ fontSize: 10, fontWeight: 600, letterSpacing: '0.08em', textTransform: 'uppercase', color: 'var(--fg-3)', marginBottom: 8 }}>Max drawdown</div>
          <Num value="−4.8%" size="xl" color="var(--down-500)"/>
          <div style={{ fontFamily: 'var(--font-mono)', fontSize: 12, marginTop: 4, color: 'var(--fg-3)' }}>within your profile limit</div>
        </Card>
        <Card style={{ padding: '16px 20px' }}>
          <div style={{ fontSize: 10, fontWeight: 600, letterSpacing: '0.08em', textTransform: 'uppercase', color: 'var(--fg-3)', marginBottom: 8 }}>Open positions</div>
          <Num value="7" size="xl"/>
          <div style={{ fontFamily: 'var(--font-mono)', fontSize: 12, marginTop: 4, color: 'var(--fg-3)' }}>2 at trailing stop</div>
        </Card>
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: '1.7fr 1fr', gap: 14 }}>
        {/* Positions table */}
        <Card>
          <div style={{ padding: '14px 18px', borderBottom: '1px solid var(--line-1)', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <h2 style={{ margin: 0 }}>Open positions</h2>
            <Pill tone="neutral">7 positions</Pill>
          </div>
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
            <thead><tr>{['Symbol','Entry','LTP','P&L','R','Stop','Status'].map((h,i) => (
              <th key={i} style={{ textAlign: i < 1 ? 'left' : 'right', fontSize: 10, fontWeight: 600, letterSpacing: '0.08em', textTransform: 'uppercase', color: 'var(--fg-3)', padding: '10px 16px', borderBottom: '1px solid var(--line-1)' }}>{h}</th>
            ))}</tr></thead>
            <tbody>
              {positions.map(p => {
                const pnl = (p.ltp - p.entry) * p.qty;
                const tone = p.status === 'up' ? 'up' : p.status === 'down' ? 'down' : 'flat';
                return (
                  <tr key={p.sym} style={{ borderBottom: '1px solid var(--line-1)' }}>
                    <td style={{ padding: '11px 16px', fontFamily: 'var(--font-mono)', fontWeight: 600 }}>{p.sym}</td>
                    <td style={{ padding: '11px 16px', textAlign: 'right' }}><Num value={p.entry.toFixed(2)} size="sm"/></td>
                    <td style={{ padding: '11px 16px', textAlign: 'right' }}><Num value={p.ltp.toFixed(2)} size="sm"/></td>
                    <td style={{ padding: '11px 16px', textAlign: 'right' }}><Num value={(pnl >= 0 ? '+' : '') + pnl.toFixed(0)} size="sm" color={pnl > 0 ? 'var(--up-500)' : pnl < 0 ? 'var(--down-500)' : 'var(--fg-3)'}/></td>
                    <td style={{ padding: '11px 16px', textAlign: 'right' }}><Num value={(p.r >= 0 ? '+' : '') + p.r.toFixed(1) + 'R'} size="sm" color={p.r > 0 ? 'var(--up-500)' : p.r < 0 ? 'var(--down-500)' : 'var(--fg-3)'}/></td>
                    <td style={{ padding: '11px 16px', textAlign: 'right' }}><Num value={p.stop.toFixed(0)} size="sm" color="var(--fg-3)"/></td>
                    <td style={{ padding: '11px 16px', textAlign: 'right' }}><Pill tone={tone === 'flat' ? 'neutral' : tone} dot>{tone === 'up' ? 'On' : tone === 'down' ? 'At stop' : 'Flat'}</Pill></td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </Card>

        {/* Notifications feed */}
        <Card>
          <div style={{ padding: '14px 18px', borderBottom: '1px solid var(--line-1)', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <h2 style={{ margin: 0 }}>Alerts</h2>
            <Btn variant="ghost" size="sm">View all</Btn>
          </div>
          <div>
            {notifs.map((n, i) => (
              <div key={i} style={{ padding: '13px 16px', borderBottom: i < notifs.length - 1 ? '1px solid var(--line-1)' : 'none', display: 'flex', gap: 11, background: n.unread ? 'rgba(58,125,255,0.04)' : 'transparent' }}>
                <div style={{ width: 26, height: 26, borderRadius: 6, background: `var(--${n.tone}-bg)`, color: `var(--${n.tone === 'info' ? 'brand-300' : n.tone + '-500'})`, display: 'flex', alignItems: 'center', justifyContent: 'center', fontFamily: 'var(--font-mono)', fontWeight: 700, fontSize: 12, flexShrink: 0 }}>{n.icon}</div>
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div style={{ display: 'flex', justifyContent: 'space-between', gap: 8 }}>
                    <span style={{ fontSize: 13, fontWeight: 600 }}>{n.title}</span>
                    <span style={{ fontFamily: 'var(--font-mono)', fontSize: 11, color: 'var(--fg-3)' }}>{n.time}</span>
                  </div>
                  <div style={{ fontSize: 12.5, color: 'var(--fg-2)', marginTop: 2 }}>{n.msg}</div>
                </div>
              </div>
            ))}
          </div>
        </Card>
      </div>
    </div>
  );
};

window.Dashboard = Dashboard;
