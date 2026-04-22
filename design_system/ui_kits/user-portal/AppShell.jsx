// AppShell.jsx — 3-zone layout for the user portal.

const AppShell = ({ current, setCurrent, children }) => {
  const nav = [
    { id: 'dashboard', label: 'Dashboard', icon: 'home' },
    { id: 'chart', label: 'Chart', icon: 'chart-line' },
    { id: 'signals', label: 'Signal Builder', icon: 'layers' },
    { id: 'notifications', label: 'Notifications', icon: 'bell', badge: 3 },
  ];
  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: '100vh', background: 'var(--bg-0)' }}>
      {/* Top bar */}
      <header style={{
        height: 56, flexShrink: 0, display: 'flex', alignItems: 'center',
        padding: '0 20px', borderBottom: '1px solid var(--line-1)', background: 'var(--bg-1)', gap: 16
      }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <Logo size={26}/>
          <span style={{ fontFamily: 'var(--font-display)', fontWeight: 700, fontSize: 16, letterSpacing: '-0.01em' }}>Signal Stack</span>
        </div>
        <div style={{
          flex: 1, maxWidth: 420, margin: '0 auto', position: 'relative'
        }}>
          <span style={{ position: 'absolute', left: 10, top: '50%', transform: 'translateY(-50%)', color: 'var(--fg-3)' }}>
            <Icon name="search" size={14}/>
          </span>
          <input placeholder="Search symbol · NSE:RELIANCE" style={{
            width: '100%', background: 'var(--bg-2)', border: '1px solid var(--line-1)',
            color: 'var(--fg-1)', padding: '7px 12px 7px 32px', borderRadius: 6, fontFamily: 'var(--font-sans)', fontSize: 13
          }}/>
        </div>
        <Pill tone="outline">Phase A · Tester</Pill>
        <Pill tone="up" dot>FYERS connected</Pill>
        <div style={{
          width: 30, height: 30, borderRadius: '50%', background: 'var(--brand-700)',
          color: 'white', display: 'flex', alignItems: 'center', justifyContent: 'center',
          fontSize: 12, fontWeight: 600
        }}>AR</div>
      </header>

      <div style={{ flex: 1, display: 'flex', minHeight: 0 }}>
        {/* Sidebar */}
        <nav style={{
          width: 220, flexShrink: 0, background: 'var(--bg-1)', borderRight: '1px solid var(--line-1)',
          padding: '14px 10px', display: 'flex', flexDirection: 'column', gap: 2
        }}>
          {nav.map(item => (
            <button key={item.id} onClick={() => setCurrent(item.id)} style={{
              display: 'flex', alignItems: 'center', gap: 10, padding: '9px 12px',
              background: current === item.id ? 'var(--bg-4)' : 'transparent',
              borderLeft: current === item.id ? '2px solid var(--brand-500)' : '2px solid transparent',
              border: 'none', color: current === item.id ? 'var(--fg-1)' : 'var(--fg-2)',
              borderRadius: 0, cursor: 'pointer', fontFamily: 'var(--font-sans)',
              fontSize: 13, fontWeight: 500, textAlign: 'left', justifyContent: 'flex-start',
              marginLeft: 0, paddingLeft: current === item.id ? 10 : 12
            }}>
              <Icon name={item.icon} size={16}/>
              <span style={{ flex: 1 }}>{item.label}</span>
              {item.badge && <Pill tone="info">{item.badge}</Pill>}
            </button>
          ))}
          <div style={{ marginTop: 'auto', padding: '8px 12px', fontSize: 10, color: 'var(--fg-4)', letterSpacing: '0.06em', textTransform: 'uppercase' }}>System</div>
          <button style={{
            display: 'flex', alignItems: 'center', gap: 10, padding: '9px 12px',
            background: 'transparent', border: 'none', color: 'var(--fg-2)', cursor: 'pointer',
            fontFamily: 'var(--font-sans)', fontSize: 13, fontWeight: 500, textAlign: 'left'
          }}>
            <Icon name="settings" size={16}/>Settings
          </button>
        </nav>

        {/* Content */}
        <main style={{ flex: 1, overflow: 'auto', background: 'var(--bg-0)' }}>
          {children}
        </main>
      </div>

      <LegalFooter/>
    </div>
  );
};

window.AppShell = AppShell;
