// AdminShell.jsx — admin-flavored shell.

const AdminShell = ({ current, setCurrent, children }) => {
  const nav = [
    { id: 'home', label: 'Admin home', icon: 'shield' },
    { id: 'approvals', label: 'User approvals', icon: 'users', badge: 4 },
    { id: 'universe', label: 'Universe', icon: 'layers' },
    { id: 'jobs', label: 'Job runs', icon: 'activity' },
  ];
  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: '100vh', background: 'var(--bg-0)' }}>
      <header style={{
        height: 56, flexShrink: 0, display: 'flex', alignItems: 'center',
        padding: '0 20px', borderBottom: '1px solid var(--line-1)', background: 'var(--bg-1)', gap: 16
      }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <Logo size={26}/>
          <span style={{ fontFamily: 'var(--font-display)', fontWeight: 700, fontSize: 16, letterSpacing: '-0.01em' }}>Signal Stack</span>
          <span style={{ padding: '2px 8px', borderRadius: 4, background: 'var(--brand-700)', color: 'white', fontSize: 10, fontWeight: 700, letterSpacing: '0.08em' }}>ADMIN</span>
        </div>
        <div style={{ flex: 1 }}/>
        <Pill tone="warn" dot>Phase A</Pill>
        <Pill tone="up" dot>All systems normal</Pill>
        <div style={{ width: 30, height: 30, borderRadius: '50%', background: 'var(--brand-700)', color: 'white', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 12, fontWeight: 600 }}>AR</div>
      </header>
      <div style={{ flex: 1, display: 'flex', minHeight: 0 }}>
        <nav style={{ width: 220, flexShrink: 0, background: 'var(--bg-1)', borderRight: '1px solid var(--line-1)', padding: '14px 10px', display: 'flex', flexDirection: 'column', gap: 2 }}>
          {nav.map(item => (
            <button key={item.id} onClick={() => setCurrent(item.id)} style={{
              display: 'flex', alignItems: 'center', gap: 10, padding: '9px 12px',
              background: current === item.id ? 'var(--bg-4)' : 'transparent',
              borderLeft: current === item.id ? '2px solid var(--brand-500)' : '2px solid transparent',
              border: 'none', color: current === item.id ? 'var(--fg-1)' : 'var(--fg-2)',
              borderRadius: 0, cursor: 'pointer', fontSize: 13, fontWeight: 500, textAlign: 'left',
              paddingLeft: current === item.id ? 10 : 12
            }}>
              <Icon name={item.icon} size={16}/>
              <span style={{ flex: 1 }}>{item.label}</span>
              {item.badge && <Pill tone="warn">{item.badge}</Pill>}
            </button>
          ))}
        </nav>
        <main style={{ flex: 1, overflow: 'auto', background: 'var(--bg-0)' }}>{children}</main>
      </div>
      <LegalFooter/>
    </div>
  );
};

window.AdminShell = AdminShell;
