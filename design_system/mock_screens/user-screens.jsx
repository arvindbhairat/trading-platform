// User-facing screens beyond the existing kit:
// notifications feed, backtest results, telegram preview.

const NotificationsFeed = () => {
  const groups = [
    { date: 'Today · 28 Apr 2026', items: [
      { kind: 'entry', sym: 'RELIANCE', title: 'Entry signal · Volume Spike', body: 'Your scan matched RELIANCE at your configured entry ₹2,840.', time: '15:31', unread: true, scan: 'Volume Spike · Large Cap' },
      { kind: 'stop',  sym: 'ICICIBANK', title: 'Stop level breached', body: 'ICICIBANK breached your stop at ₹1,042. Configured exit condition met.', time: '14:47', unread: true, scan: 'Breakout · Banks' },
      { kind: 'add',   sym: 'LT', title: 'Add level reached', body: 'LT reached your first add level — +0.5R. Pyramiding allowed up to 2 adds.', time: '12:14', scan: 'Volume Spike · Large Cap' },
      { kind: 'sys',   sym: null, title: 'DataSync completed', body: '499 / 500 symbols synced. 1 symbol skipped (HFCL — corp action interim).', time: '15:35' },
    ]},
    { date: 'Yesterday · 27 Apr', items: [
      { kind: 'reduce', sym: 'TCS', title: 'Reduce level reached', body: 'TCS reached your first reduce level — locked +1R. Trailing stop ratcheted to ₹3,580.', time: '11:08', scan: 'Volatility · IT' },
      { kind: 'sys', sym: null, title: 'EOD Signal Runner completed', body: '4 entry signals across 3 active subscriptions.', time: '15:38' },
    ]},
  ];
  const tone = (k) => k === 'entry' || k === 'add' ? 'up' : k === 'stop' || k === 'reduce' ? 'down' : 'info';
  const glyph = (k) => k === 'entry' ? '▲' : k === 'add' ? '+' : k === 'stop' ? '▼' : k === 'reduce' ? '½' : 'i';

  return (
    <Shell current="notifications">
      <div style={{ padding: 24, maxWidth: 1100, margin: '0 auto', display: 'flex', flexDirection: 'column', gap: 18 }}>
        <div style={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between' }}>
          <div>
            <Label>Alerts</Label>
            <h1 style={{ marginTop: 4 }}>Notifications</h1>
            <p style={{ color: 'var(--fg-2)', fontSize: 14, marginTop: 4 }}>
              Every alert is also delivered to your Telegram. Read-state syncs across surfaces.
            </p>
          </div>
          <div style={{ display: 'flex', gap: 8 }}>
            <Btn variant="secondary" icon="filter">Filter</Btn>
            <Btn variant="ghost" icon="check">Mark all read</Btn>
          </div>
        </div>

        {/* Filter chips */}
        <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
          {[['All', true, 47], ['Entry', false, 12], ['Stop', false, 4], ['Add / Reduce', false, 9], ['System', false, 22]].map(([l, on, n]) => (
            <span key={l} style={{
              padding: '6px 12px', borderRadius: 999, fontSize: 12, fontWeight: 500,
              background: on ? 'var(--bg-4)' : 'var(--bg-2)',
              border: `1px solid ${on ? 'var(--brand-500)' : 'var(--line-1)'}`,
              color: on ? 'var(--fg-1)' : 'var(--fg-2)', cursor: 'pointer'
            }}>{l} <span style={{ color: 'var(--fg-3)', fontFamily: 'var(--font-mono)' }}>{n}</span></span>
          ))}
        </div>

        {groups.map(g => (
          <div key={g.date}>
            <Label style={{ marginBottom: 8 }}>{g.date}</Label>
            <Card style={{ overflow: 'hidden' }}>
              {g.items.map((n, i) => (
                <div key={i} style={{
                  padding: '14px 18px', display: 'flex', gap: 14, alignItems: 'flex-start',
                  borderBottom: i < g.items.length - 1 ? '1px solid var(--line-1)' : 'none',
                  background: n.unread ? 'rgba(216,138,28,0.04)' : 'transparent'
                }}>
                  <div style={{
                    width: 30, height: 30, borderRadius: 7, flexShrink: 0,
                    background: `var(--${tone(n.kind)}-bg)`,
                    color: tone(n.kind) === 'info' ? 'var(--brand-300)' : `var(--${tone(n.kind)}-500)`,
                    display: 'flex', alignItems: 'center', justifyContent: 'center',
                    fontFamily: 'var(--font-mono)', fontWeight: 700, fontSize: 13
                  }}>{glyph(n.kind)}</div>
                  <div style={{ flex: 1, minWidth: 0 }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', gap: 12 }}>
                      <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
                        <span style={{ fontSize: 13.5, fontWeight: 600 }}>{n.title}</span>
                        {n.sym && <span style={{ fontFamily: 'var(--font-mono)', fontSize: 12, color: 'var(--fg-2)', fontWeight: 600 }}>{n.sym}</span>}
                        {n.unread && <Pill tone="brand">New</Pill>}
                      </div>
                      <span style={{ fontFamily: 'var(--font-mono)', fontSize: 11, color: 'var(--fg-3)', whiteSpace: 'nowrap' }}>{n.time}</span>
                    </div>
                    <div style={{ fontSize: 13, color: 'var(--fg-2)', marginTop: 4, lineHeight: 1.5 }}>{n.body}</div>
                    {n.scan && (
                      <div style={{ marginTop: 8, display: 'flex', alignItems: 'center', gap: 8 }}>
                        <Pill tone="outline">{n.scan}</Pill>
                        <a href="#" style={{ fontSize: 11.5, color: 'var(--brand-300)', textDecoration: 'none' }}>Open chart →</a>
                      </div>
                    )}
                  </div>
                </div>
              ))}
            </Card>
          </div>
        ))}
      </div>
    </Shell>
  );
};

window.NotificationsFeed = NotificationsFeed;

// Backtest results / RME profile optimisation (REQ-PORT, REQ-RME)
const BacktestResults = () => {
  // Synthesised equity curve
  const pts = Array.from({length: 80}, (_, i) => {
    const x = i / 79;
    const v = 1 + x * 0.62 + Math.sin(x * 6) * 0.05 - Math.max(0, (x - 0.4) * (x - 0.55)) * 0.6;
    return v;
  });
  const w = 720, h = 200;
  const path = pts.map((v, i) => `${i === 0 ? 'M' : 'L'} ${(i / (pts.length - 1)) * w} ${h - (v - 0.9) * 280}`).join(' ');

  const variants = [
    { id: 'A', label: 'Fixed % · ATR 2.5x', cagr: '+24.7%', sharpe: '1.42', dd: '−12.3%', trades: 184, picked: true },
    { id: 'B', label: 'Volatility target · Chandelier', cagr: '+22.1%', sharpe: '1.38', dd: '−9.8%', trades: 162 },
    { id: 'C', label: 'Fixed % · % trailing', cagr: '+19.4%', sharpe: '1.21', dd: '−14.6%', trades: 198 },
    { id: 'D', label: 'Equity-curve · ATR 3x', cagr: '+26.8%', sharpe: '1.31', dd: '−16.2%', trades: 144 },
  ];

  return (
    <Shell current="signals">
      <div style={{ padding: 24, maxWidth: 1280, margin: '0 auto', display: 'flex', flexDirection: 'column', gap: 16 }}>
        <div style={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between' }}>
          <div>
            <Label>Backtest run · 28 Apr 14:02 IST</Label>
            <h1 style={{ marginTop: 4 }}>Volume Spike · Large Cap</h1>
            <p style={{ color: 'var(--fg-2)', fontSize: 14, marginTop: 4 }}>
              7-year replay across Nifty 500 with 4 RME profile variants. Backtest Engine and live RME parity verified.
            </p>
          </div>
          <div style={{ display: 'flex', gap: 8 }}>
            <Btn variant="secondary" icon="download">Export CSV</Btn>
            <Btn variant="primary" icon="check">Adopt variant A</Btn>
          </div>
        </div>

        {/* KPI strip */}
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(5, 1fr)', gap: 12 }}>
          {[
            ['CAGR', '+24.7%', 'up'],
            ['Sharpe', '1.42'],
            ['Max DD', '−12.3%', 'down'],
            ['Win rate', '58.2%'],
            ['Avg R', '+0.84R', 'up'],
          ].map(([l, v, tone], i) => (
            <Card key={i} style={{ padding: '14px 18px' }}>
              <Label>{l}</Label>
              <div style={{ marginTop: 6 }}>
                <Num value={v} size="lg" color={tone === 'up' ? 'var(--up-500)' : tone === 'down' ? 'var(--down-500)' : undefined}/>
              </div>
            </Card>
          ))}
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: '1.6fr 1fr', gap: 14 }}>
          {/* Equity curve */}
          <Card>
            <div style={{ padding: '14px 18px', borderBottom: '1px solid var(--line-1)', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <h2 style={{ margin: 0 }}>Equity curve · variant A</h2>
              <Pill tone="outline">vs Nifty 500</Pill>
            </div>
            <div style={{ padding: 18 }}>
              <svg viewBox={`0 0 ${w} ${h + 30}`} style={{ width: '100%', height: 220, display: 'block' }}>
                <defs>
                  <linearGradient id="eq" x1="0" x2="0" y1="0" y2="1">
                    <stop offset="0" stopColor="var(--up-500)" stopOpacity="0.25"/>
                    <stop offset="1" stopColor="var(--up-500)" stopOpacity="0"/>
                  </linearGradient>
                  <pattern id="g2" width={w/8} height={h/4} patternUnits="userSpaceOnUse">
                    <path d={`M ${w/8} 0 L 0 0 0 ${h/4}`} fill="none" stroke="var(--line-1)" strokeWidth="1"/>
                  </pattern>
                </defs>
                <rect width={w} height={h} fill="url(#g2)"/>
                {/* benchmark line */}
                <path d={pts.map((_, i) => `${i === 0 ? 'M' : 'L'} ${(i/(pts.length-1))*w} ${h - (1 + i/pts.length*0.18 - 0.9)*280}`).join(' ')}
                  fill="none" stroke="var(--fg-4)" strokeDasharray="3 3" strokeWidth="1.5"/>
                {/* eq area */}
                <path d={`${path} L ${w} ${h} L 0 ${h} Z`} fill="url(#eq)"/>
                <path d={path} fill="none" stroke="var(--up-500)" strokeWidth="1.8"/>
                <text x={w-100} y={h - (pts[pts.length-1]-0.9)*280 - 6} fill="var(--up-500)" fontSize="11" fontFamily="var(--font-mono)">+24.7% CAGR</text>
                <text x={w-100} y={h - 8} fill="var(--fg-3)" fontSize="11" fontFamily="var(--font-mono)">Nifty 500 +5.1%</text>
              </svg>
            </div>
          </Card>

          {/* Variant comparison */}
          <Card>
            <div style={{ padding: '14px 18px', borderBottom: '1px solid var(--line-1)' }}>
              <h2 style={{ margin: 0 }}>Profile variants</h2>
              <div style={{ fontSize: 12, color: 'var(--fg-3)', marginTop: 2 }}>4 mechanism combinations evaluated</div>
            </div>
            <div>
              {variants.map((v, i) => (
                <div key={v.id} style={{
                  padding: '12px 18px',
                  borderBottom: i < variants.length - 1 ? '1px solid var(--line-1)' : 'none',
                  borderLeft: v.picked ? '2px solid var(--brand-500)' : '2px solid transparent',
                  background: v.picked ? 'var(--bg-3)' : 'transparent'
                }}>
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8 }}>
                    <div>
                      <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
                        <span style={{ fontFamily: 'var(--font-mono)', fontWeight: 700, color: v.picked ? 'var(--brand-300)' : 'var(--fg-2)' }}>{v.id}</span>
                        <span style={{ fontSize: 13, fontWeight: 500 }}>{v.label}</span>
                        {v.picked && <Pill tone="brand">Recommended</Pill>}
                      </div>
                    </div>
                  </div>
                  <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4,1fr)', gap: 8, marginTop: 8 }}>
                    {[['CAGR', v.cagr, 'up'], ['Sharpe', v.sharpe], ['DD', v.dd, 'down'], ['Trades', v.trades]].map(([l, val, t], j) => (
                      <div key={j}>
                        <div style={{ fontSize: 10, color: 'var(--fg-3)', textTransform: 'uppercase', letterSpacing: '0.06em' }}>{l}</div>
                        <Num value={val} size="sm" color={t === 'up' ? 'var(--up-500)' : t === 'down' ? 'var(--down-500)' : undefined}/>
                      </div>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          </Card>
        </div>

        {/* Symbol-level recommendations */}
        <Card>
          <div style={{ padding: '14px 18px', borderBottom: '1px solid var(--line-1)', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <h2 style={{ margin: 0 }}>Symbol-level recommendations</h2>
            <Pill tone="neutral">12 symbols</Pill>
          </div>
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
            <thead><tr>{['Symbol', 'Sector', 'Trades', 'Win', 'Avg R', 'Best variant', 'Confidence'].map((h, i) => (
              <th key={i} style={{ textAlign: i < 2 ? 'left' : 'right', fontSize: 10, fontWeight: 600, letterSpacing: '0.08em', textTransform: 'uppercase', color: 'var(--fg-3)', padding: '10px 16px', borderBottom: '1px solid var(--line-1)' }}>{h}</th>
            ))}</tr></thead>
            <tbody>
              {[
                ['RELIANCE', 'Energy', 18, '67%', '+1.12R', 'A', 92],
                ['TCS', 'IT', 14, '64%', '+0.96R', 'A', 88],
                ['LT', 'Capital Goods', 12, '58%', '+0.84R', 'B', 81],
                ['INFY', 'IT', 16, '63%', '+0.79R', 'A', 86],
                ['HDFCBANK', 'Banks', 22, '55%', '+0.62R', 'A', 74],
                ['ICICIBANK', 'Banks', 19, '53%', '+0.41R', 'C', 62],
              ].map((r, i) => (
                <tr key={i} style={{ borderBottom: '1px solid var(--line-1)' }}>
                  <td style={{ padding: '11px 16px', fontFamily: 'var(--font-mono)', fontWeight: 600 }}>{r[0]}</td>
                  <td style={{ padding: '11px 16px', color: 'var(--fg-2)' }}>{r[1]}</td>
                  <td style={{ padding: '11px 16px', textAlign: 'right' }}><Num value={r[2]} size="sm"/></td>
                  <td style={{ padding: '11px 16px', textAlign: 'right' }}><Num value={r[3]} size="sm"/></td>
                  <td style={{ padding: '11px 16px', textAlign: 'right' }}><Num value={r[4]} size="sm" color="var(--up-500)"/></td>
                  <td style={{ padding: '11px 16px', textAlign: 'right' }}><Pill tone="brand">{r[5]}</Pill></td>
                  <td style={{ padding: '11px 16px', textAlign: 'right' }}>
                    <div style={{ display: 'inline-flex', alignItems: 'center', gap: 8 }}>
                      <div style={{ width: 60, height: 4, background: 'var(--bg-1)', borderRadius: 2, overflow: 'hidden' }}>
                        <div style={{ height: '100%', width: r[6] + '%', background: r[6] >= 80 ? 'var(--up-500)' : r[6] >= 70 ? 'var(--brand-500)' : 'var(--warn-500)' }}/>
                      </div>
                      <Num value={r[6]} size="sm" color="var(--fg-3)"/>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </Card>
      </div>
    </Shell>
  );
};

window.BacktestResults = BacktestResults;

// Telegram notification preview
const TelegramPreview = () => {
  const messages = [
    { time: '15:31', title: 'Entry signal · Volume Spike',
      lines: ['Your scan matched RELIANCE at your configured entry ₹2,840.', 'Stop ₹2,760 · First add ₹2,920 · Size 52', 'Open risk 2.1% → 2.9% · Energy 14% → 18%'],
      cta: 'Open chart →' },
    { time: '14:47', title: 'Stop level breached', tone: 'down',
      lines: ['ICICIBANK breached your stop at ₹1,042.', 'Configured exit condition. Position −0.7R.'],
      cta: 'Review exit →' },
    { time: '12:14', title: 'Add level reached',
      lines: ['LT reached your first add level — +0.5R.', 'Pyramiding allowed up to 2 adds.'],
      cta: 'Open chart →' },
  ];
  return (
    <div style={{ height: '100%', background: '#17212b', display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
      {/* Header */}
      <div style={{ padding: '14px 16px', background: '#1f2c39', borderBottom: '1px solid #0e1620', display: 'flex', alignItems: 'center', gap: 12 }}>
        <div style={{ width: 36, height: 36, borderRadius: '50%', background: 'var(--brand-500)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
          <Logo size={20} color="white"/>
        </div>
        <div style={{ flex: 1 }}>
          <div style={{ color: 'white', fontWeight: 600, fontSize: 14 }}>Signal Stack alerts</div>
          <div style={{ color: '#7a8b9d', fontSize: 11 }}>bot · last seen recently</div>
        </div>
        <div style={{ color: '#5b8db0' }}><Icon name="more" size={18}/></div>
      </div>

      {/* Pattern background */}
      <div style={{ flex: 1, padding: '16px 12px', overflow: 'auto', display: 'flex', flexDirection: 'column', gap: 10,
        backgroundImage: 'radial-gradient(circle at 25% 30%, rgba(255,255,255,0.02) 1px, transparent 1px), radial-gradient(circle at 75% 70%, rgba(255,255,255,0.02) 1px, transparent 1px)',
        backgroundSize: '40px 40px' }}>
        <div style={{ alignSelf: 'center', padding: '4px 10px', background: 'rgba(255,255,255,0.06)', color: '#9aa9ba', fontSize: 11, borderRadius: 10 }}>April 28</div>

        {messages.map((m, i) => (
          <div key={i} style={{
            maxWidth: '88%', alignSelf: 'flex-start',
            background: '#243747', borderRadius: '14px 14px 14px 4px',
            padding: '10px 12px', display: 'flex', flexDirection: 'column', gap: 6,
            boxShadow: '0 1px 1px rgba(0,0,0,0.3)'
          }}>
            <div style={{ color: m.tone === 'down' ? '#ff8a7d' : 'var(--brand-300)', fontWeight: 600, fontSize: 13 }}>
              ▲ {m.title}
            </div>
            {m.lines.map((l, j) => (
              <div key={j} style={{ color: '#e7eef6', fontSize: 13.5, lineHeight: 1.45,
                fontFamily: j === 1 ? 'var(--font-mono)' : 'inherit',
                fontVariantNumeric: 'tabular-nums',
                fontSize: j === 1 ? 12 : 13.5,
                color: j === 1 ? '#9aafc4' : '#e7eef6'
              }}>{l}</div>
            ))}
            <a href="#" style={{ color: '#6ab1ec', fontSize: 13, textDecoration: 'none', marginTop: 2 }}>{m.cta}</a>
            <div style={{ fontSize: 10, color: '#6c8499', marginTop: 4, fontStyle: 'italic', borderTop: '1px solid rgba(255,255,255,0.06)', paddingTop: 5, lineHeight: 1.4 }}>
              Decision support, not advice. You are the sole decision-maker.
            </div>
            <div style={{ alignSelf: 'flex-end', fontSize: 10, color: '#6c8499', marginTop: -2 }}>{m.time}</div>
          </div>
        ))}
      </div>

      {/* Input bar */}
      <div style={{ padding: '10px 12px', background: '#1f2c39', borderTop: '1px solid #0e1620', display: 'flex', gap: 8, alignItems: 'center' }}>
        <div style={{ flex: 1, padding: '8px 12px', background: '#243747', borderRadius: 16, color: '#7a8b9d', fontSize: 13 }}>Message</div>
        <div style={{ color: 'var(--brand-400)' }}><Icon name="send" size={20}/></div>
      </div>
    </div>
  );
};

window.TelegramPreview = TelegramPreview;
