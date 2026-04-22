// SignalBuilder.jsx — create/edit a Signal Subscription.

const SignalBuilder = () => {
  const [name, setName] = React.useState('Volume Spike · Large Cap');
  const [tf, setTf] = React.useState('Daily');
  const [risk, setRisk] = React.useState('0.75');

  const field = (lbl, ...kids) => (
    <label style={{ display: 'flex', flexDirection: 'column', gap: 6, fontSize: 12, color: 'var(--fg-3)', fontWeight: 500 }}>
      <span style={{ letterSpacing: '0.04em' }}>{lbl}</span>
      {kids}
    </label>
  );

  const input = (v, set) => (
    <input value={v} onChange={e => set(e.target.value)} style={{
      background: 'var(--bg-1)', border: '1px solid var(--line-2)', color: 'var(--fg-1)',
      padding: '9px 12px', borderRadius: 6, fontFamily: 'var(--font-sans)', fontSize: 13
    }}/>
  );

  return (
    <div style={{ padding: 24, display: 'flex', flexDirection: 'column', gap: 18, maxWidth: 1000, margin: '0 auto' }}>
      <div>
        <div style={{ fontSize: 10, fontWeight: 600, letterSpacing: '0.08em', textTransform: 'uppercase', color: 'var(--fg-3)', marginBottom: 4 }}>Signal Builder</div>
        <h1 style={{ margin: 0 }}>Edit subscription</h1>
        <p style={{ color: 'var(--fg-2)', fontSize: 14, marginTop: 6, maxWidth: 540 }}>
          You select the scan parameters; the EOD Signal Runner evaluates them after market close and emits matches to your alerts.
        </p>
      </div>

      <Card style={{ padding: 24, display: 'flex', flexDirection: 'column', gap: 16 }}>
        <h2 style={{ margin: 0 }}>1 · Scan type & parameters</h2>
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 14 }}>
          {field('Subscription name', input(name, setName))}
          {field('Signal type',
            <select style={{
              background: 'var(--bg-1)', border: '1px solid var(--line-2)', color: 'var(--fg-1)',
              padding: '9px 12px', borderRadius: 6, fontFamily: 'var(--font-sans)', fontSize: 13
            }}>
              <option>Volume Spike Signal</option>
              <option>Price Volatility Signal</option>
              <option>Breakout Signal</option>
            </select>
          )}
          {field('Timeframe',
            <div style={{ display: 'flex', gap: 6 }}>
              {['Daily','Weekly','Monthly'].map(t => (
                <button key={t} onClick={() => setTf(t)} style={{
                  flex:1, padding: '8px 0', background: tf === t ? 'var(--brand-500)' : 'var(--bg-1)',
                  color: tf === t ? 'white' : 'var(--fg-2)', border: `1px solid ${tf === t ? 'var(--brand-500)' : 'var(--line-2)'}`,
                  borderRadius: 6, fontSize: 13, cursor: 'pointer', fontWeight: 500
                }}>{t}</button>
              ))}
            </div>
          )}
          {field('Volume multiplier', input('2.5', () => {}))}
          {field('Lookback (days)', input('20', () => {}))}
          {field('Min market cap (Cr)', input('10,000', () => {}))}
        </div>
      </Card>

      <Card style={{ padding: 24, display: 'flex', flexDirection: 'column', gap: 16 }}>
        <h2 style={{ margin: 0 }}>2 · RME profile</h2>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 14 }}>
          {field('Position sizing',
            <select style={{
              background: 'var(--bg-1)', border: '1px solid var(--line-2)', color: 'var(--fg-1)',
              padding: '9px 12px', borderRadius: 6, fontSize: 13
            }}><option>Fixed fractional · ATR</option><option>Volatility target</option></select>
          )}
          {field('Risk per trade (%)', input(risk, setRisk))}
          {field('Max concurrent positions', input('10', () => {}))}
          {field('Stop type',
            <select style={{
              background: 'var(--bg-1)', border: '1px solid var(--line-2)', color: 'var(--fg-1)',
              padding: '9px 12px', borderRadius: 6, fontSize: 13
            }}><option>ATR trailing · 2.5x</option><option>Percent trailing</option><option>Chandelier exit</option></select>
          )}
          {field('Pyramiding', input('2 adds', () => {}))}
          {field('Max portfolio heat (%)', input('6', () => {}))}
        </div>
      </Card>

      <Card style={{ padding: 24 }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <div>
            <h2 style={{ margin: 0 }}>3 · Backtest</h2>
            <p style={{ color: 'var(--fg-2)', fontSize: 13, margin: '4px 0 0 0' }}>Last run: 12 Apr · 7y, Nifty 500</p>
          </div>
          <Btn variant="secondary" icon="activity">Run optimisation</Btn>
        </div>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4,1fr)', gap: 14, marginTop: 18 }}>
          {[['CAGR', '+24.7%', 'up'], ['Sharpe', '1.42', null], ['Max DD', '−12.3%', 'down'], ['Trades', '184', null]].map(([l, v, tone], i) => (
            <div key={i} style={{ padding: '12px 14px', background: 'var(--bg-1)', border: '1px solid var(--line-1)', borderRadius: 8 }}>
              <div style={{ fontSize: 10, fontWeight: 600, letterSpacing: '0.08em', textTransform: 'uppercase', color: 'var(--fg-3)', marginBottom: 6 }}>{l}</div>
              <Num value={v} size="lg" color={tone === 'up' ? 'var(--up-500)' : tone === 'down' ? 'var(--down-500)' : 'var(--fg-1)'}/>
            </div>
          ))}
        </div>
      </Card>

      <div style={{ display: 'flex', gap: 10, justifyContent: 'flex-end' }}>
        <Btn variant="ghost">Cancel</Btn>
        <Btn variant="secondary">Save as draft</Btn>
        <Btn variant="primary" icon="check">Activate subscription</Btn>
      </div>
    </div>
  );
};

window.SignalBuilder = SignalBuilder;
