// ChartPage.jsx — Chart pane with RME advisory panel.

const ChartPage = ({ setShowOrderModal }) => {
  // Stubbed chart — real app uses TradingView lightweight-charts
  const candles = Array.from({length: 60}, (_, i) => {
    const base = 2750 + Math.sin(i * 0.3) * 40 + i * 2.2;
    const o = base + (Math.random() - 0.5) * 10;
    const c = base + (Math.random() - 0.5) * 10;
    const h = Math.max(o, c) + Math.random() * 8;
    const l = Math.min(o, c) - Math.random() * 8;
    return { o, h, l, c };
  });
  const min = Math.min(...candles.map(k => k.l)), max = Math.max(...candles.map(k => k.h));
  const range = max - min;
  const w = 900, h = 340, cw = w / candles.length;
  const y = v => h - ((v - min) / range) * h;

  return (
    <div style={{ padding: 24, display: 'flex', flexDirection: 'column', gap: 14, maxWidth: 1400, margin: '0 auto' }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
        <span style={{ fontFamily: 'var(--font-mono)', fontSize: 20, fontWeight: 700 }}>NSE:RELIANCE</span>
        <span style={{ color: 'var(--fg-3)', fontSize: 14 }}>Reliance Industries Ltd</span>
        <Num value="₹2,894.20" size="lg" color="var(--up-500)"/>
        <span style={{ fontFamily: 'var(--font-mono)', fontSize: 13, color: 'var(--up-500)' }}>+54.20 · +1.91%</span>
        <div style={{ marginLeft: 'auto', display: 'flex', gap: 6 }}>
          {['1D', '1W', '1M', '3M', '1Y', 'All'].map(tf => (
            <button key={tf} style={{ padding: '5px 12px', background: tf === '3M' ? 'var(--bg-4)' : 'transparent', border: '1px solid var(--line-1)', color: 'var(--fg-2)', fontSize: 12, borderRadius: 4, cursor: 'pointer', fontFamily: 'var(--font-sans)' }}>{tf}</button>
          ))}
        </div>
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: '1fr 340px', gap: 14 }}>
        {/* Chart */}
        <Card style={{ padding: 16 }}>
          <svg viewBox={`0 0 ${w} ${h}`} style={{ width: '100%', height: 340, display: 'block' }}>
            <defs>
              <pattern id="grid" width={w/6} height={h/5} patternUnits="userSpaceOnUse">
                <path d={`M ${w/6} 0 L 0 0 0 ${h/5}`} fill="none" stroke="var(--line-1)" strokeWidth="1"/>
              </pattern>
            </defs>
            <rect width={w} height={h} fill="url(#grid)"/>
            {candles.map((k, i) => {
              const up = k.c >= k.o;
              const color = up ? 'var(--up-500)' : 'var(--down-500)';
              const x = i * cw + cw/2;
              return (
                <g key={i}>
                  <line x1={x} y1={y(k.h)} x2={x} y2={y(k.l)} stroke={color} strokeWidth="1"/>
                  <rect x={x - cw * 0.35} y={y(Math.max(k.o, k.c))} width={cw * 0.7} height={Math.max(1, Math.abs(y(k.o) - y(k.c)))} fill={color}/>
                </g>
              );
            })}
            {/* Entry / stop / add lines */}
            <line x1="0" x2={w} y1={y(2840)} y2={y(2840)} stroke="var(--brand-400)" strokeWidth="1" strokeDasharray="4 4"/>
            <text x={w-60} y={y(2840)-4} fill="var(--brand-400)" fontSize="10" fontFamily="var(--font-mono)">Entry 2840</text>
            <line x1="0" x2={w} y1={y(2760)} y2={y(2760)} stroke="var(--down-500)" strokeWidth="1" strokeDasharray="4 4"/>
            <text x={w-60} y={y(2760)-4} fill="var(--down-500)" fontSize="10" fontFamily="var(--font-mono)">Stop 2760</text>
            <line x1="0" x2={w} y1={y(2920)} y2={y(2920)} stroke="var(--up-500)" strokeWidth="1" strokeDasharray="4 4"/>
            <text x={w-60} y={y(2920)-4} fill="var(--up-500)" fontSize="10" fontFamily="var(--font-mono)">Add 2920</text>
          </svg>
        </Card>

        {/* RME advisory panel */}
        <Card>
          <div style={{ padding: '14px 18px', borderBottom: '1px solid var(--line-1)' }}>
            <div style={{ fontSize: 10, fontWeight: 600, letterSpacing: '0.08em', textTransform: 'uppercase', color: 'var(--fg-3)' }}>RME advisory · your profile</div>
            <h3 style={{ margin: '4px 0 0 0' }}>Volume Spike match</h3>
          </div>
          <div style={{ padding: 18, display: 'flex', flexDirection: 'column', gap: 14 }}>
            <div>
              <div style={{ fontSize: 11, color: 'var(--fg-3)', marginBottom: 4 }}>Your configured entry</div>
              <Num value="₹2,840.00" size="lg" color="var(--brand-300)"/>
            </div>
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
              <div>
                <div style={{ fontSize: 11, color: 'var(--fg-3)', marginBottom: 4 }}>Stop (per risk profile)</div>
                <Num value="₹2,760" size="md" color="var(--down-500)"/>
              </div>
              <div>
                <div style={{ fontSize: 11, color: 'var(--fg-3)', marginBottom: 4 }}>First add level</div>
                <Num value="₹2,920" size="md" color="var(--up-500)"/>
              </div>
              <div>
                <div style={{ fontSize: 11, color: 'var(--fg-3)', marginBottom: 4 }}>Position size</div>
                <Num value="52 shrs" size="md"/>
              </div>
              <div>
                <div style={{ fontSize: 11, color: 'var(--fg-3)', marginBottom: 4 }}>Risk amount</div>
                <Num value="₹4,160" size="md"/>
              </div>
            </div>
            <div style={{ padding: 10, background: 'var(--bg-1)', border: '1px solid var(--line-1)', borderRadius: 6, fontSize: 12, color: 'var(--fg-2)' }}>
              <div style={{ fontSize: 10, color: 'var(--fg-3)', letterSpacing: '0.06em', textTransform: 'uppercase', marginBottom: 4 }}>Portfolio impact</div>
              Open risk will move from <b style={{ color: 'var(--fg-1)' }}>2.1%</b> to <b style={{ color: 'var(--fg-1)' }}>2.9%</b> of equity. Sector Energy exposure <b style={{ color: 'var(--fg-1)' }}>14% → 18%</b>.
            </div>
            <div style={{ padding: 8, background: 'rgba(245,165,36,0.08)', border: '1px solid rgba(245,165,36,0.2)', borderRadius: 6, fontSize: 11, color: 'var(--warn-500)' }}>
              You are the sole decision-maker. This is decision support based on your own configuration.
            </div>
            <Btn variant="primary" size="lg" icon="external" onClick={() => setShowOrderModal(true)}>Review on FYERS</Btn>
          </div>
        </Card>
      </div>
    </div>
  );
};

window.ChartPage = ChartPage;
