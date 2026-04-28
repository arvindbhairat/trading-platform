// Shared primitives for Signal Stack mock screens.
// Lifted from ui_kits/user-portal/Primitives.jsx + AppShell.jsx; trimmed for static mocks.

const Icon = ({ name, size = 18, color = 'currentColor', stroke = 1.75 }) => {
  const paths = {
    'bar-chart': <><line x1="12" y1="20" x2="12" y2="10"/><line x1="18" y1="20" x2="18" y2="4"/><line x1="6" y1="20" x2="6" y2="16"/></>,
    'bell': <><path d="M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9"/><path d="M10.3 21a1.94 1.94 0 0 0 3.4 0"/></>,
    'chart-line': <><path d="M3 3v18h18"/><path d="M7 14l4-4 4 4 5-5"/></>,
    'target': <><circle cx="12" cy="12" r="9"/><circle cx="12" cy="12" r="5"/><circle cx="12" cy="12" r="1"/></>,
    'layers': <><polygon points="12 2 2 7 12 12 22 7 12 2"/><polyline points="2 17 12 22 22 17"/><polyline points="2 12 12 17 22 12"/></>,
    'shield': <><path d="M12 2l9 4v6c0 5-4 9-9 10-5-1-9-5-9-10V6z"/></>,
    'settings': <><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 1 1-4 0v-.09a1.65 1.65 0 0 0-1-1.51 1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 1 1 0-4h.09a1.65 1.65 0 0 0 1.51-1 1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06a1.65 1.65 0 0 0 1.82.33h.01a1.65 1.65 0 0 0 1-1.51V3a2 2 0 1 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82v.01a1.65 1.65 0 0 0 1.51 1H21a2 2 0 1 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z"/></>,
    'plus': <><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></>,
    'search': <><circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/></>,
    'menu': <><line x1="3" y1="12" x2="21" y2="12"/><line x1="3" y1="6" x2="21" y2="6"/><line x1="3" y1="18" x2="21" y2="18"/></>,
    'x': <><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></>,
    'check': <><polyline points="20 6 9 17 4 12"/></>,
    'arrow-up-right': <><line x1="7" y1="17" x2="17" y2="7"/><polyline points="7 7 17 7 17 17"/></>,
    'arrow-down': <><polyline points="6 9 12 15 18 9"/></>,
    'arrow-right': <><line x1="5" y1="12" x2="19" y2="12"/><polyline points="12 5 19 12 12 19"/></>,
    'activity': <><polyline points="22 12 18 12 15 21 9 3 6 12 2 12"/></>,
    'home': <><path d="M3 12l9-9 9 9"/><path d="M5 10v10h5v-6h4v6h5V10"/></>,
    'users': <><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/></>,
    'clock': <><circle cx="12" cy="12" r="9"/><polyline points="12 7 12 12 15 14"/></>,
    'alert-triangle': <><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></>,
    'external': <><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"/><polyline points="15 3 21 3 21 9"/><line x1="10" y1="14" x2="21" y2="3"/></>,
    'trending-up': <><polyline points="23 6 13.5 15.5 8.5 10.5 1 18"/><polyline points="17 6 23 6 23 12"/></>,
    'trending-down': <><polyline points="23 18 13.5 8.5 8.5 13.5 1 6"/><polyline points="17 18 23 18 23 12"/></>,
    'upload': <><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="17 8 12 3 7 8"/><line x1="12" y1="3" x2="12" y2="15"/></>,
    'download': <><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="7 10 12 15 17 10"/><line x1="12" y1="15" x2="12" y2="3"/></>,
    'refresh': <><polyline points="23 4 23 10 17 10"/><polyline points="1 20 1 14 7 14"/><path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"/></>,
    'play': <><polygon points="5 3 19 12 5 21 5 3"/></>,
    'pause': <><rect x="6" y="4" width="4" height="16"/><rect x="14" y="4" width="4" height="16"/></>,
    'calendar': <><rect x="3" y="4" width="18" height="18" rx="2"/><line x1="16" y1="2" x2="16" y2="6"/><line x1="8" y1="2" x2="8" y2="6"/><line x1="3" y1="10" x2="21" y2="10"/></>,
    'lock': <><rect x="3" y="11" width="18" height="11" rx="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/></>,
    'mail': <><rect x="2" y="4" width="20" height="16" rx="2"/><polyline points="22 6 12 13 2 6"/></>,
    'send': <><line x1="22" y1="2" x2="11" y2="13"/><polygon points="22 2 15 22 11 13 2 9 22 2"/></>,
    'file-text': <><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="16" y1="13" x2="8" y2="13"/><line x1="16" y1="17" x2="8" y2="17"/></>,
    'circle-check': <><circle cx="12" cy="12" r="10"/><polyline points="9 12 12 15 16 10"/></>,
    'circle-x': <><circle cx="12" cy="12" r="10"/><line x1="15" y1="9" x2="9" y2="15"/><line x1="9" y1="9" x2="15" y2="15"/></>,
    'eye': <><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/></>,
    'filter': <><polygon points="22 3 2 3 10 12.46 10 19 14 21 14 12.46 22 3"/></>,
    'more': <><circle cx="12" cy="12" r="1"/><circle cx="19" cy="12" r="1"/><circle cx="5" cy="12" r="1"/></>,
    'database': <><ellipse cx="12" cy="5" rx="9" ry="3"/><path d="M3 5v14c0 1.66 4 3 9 3s9-1.34 9-3V5"/><path d="M3 12c0 1.66 4 3 9 3s9-1.34 9-3"/></>,
    'telegram': <><path d="M22 2L11 13M22 2l-7 20-4-9-9-4 20-7z"/></>,
  };
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke={color} strokeWidth={stroke} strokeLinecap="round" strokeLinejoin="round">
      {paths[name] || null}
    </svg>
  );
};

const Logo = ({ size = 28, color = 'var(--brand-400)' }) => (
  <svg width={size} height={size} viewBox="0 0 40 40" fill="none" style={{ color }}>
    <rect x="6"  y="24" width="6" height="10" rx="1.5" fill="currentColor" opacity="0.45"/>
    <rect x="14" y="18" width="6" height="16" rx="1.5" fill="currentColor" opacity="0.65"/>
    <rect x="22" y="12" width="6" height="22" rx="1.5" fill="currentColor" opacity="0.85"/>
    <rect x="30" y="6"  width="6" height="28" rx="1.5" fill="currentColor"/>
    <circle cx="33" cy="6" r="2.6" fill="currentColor"/>
    <circle cx="33" cy="6" r="1.2" fill="var(--bg-1)"/>
  </svg>
);

const Pill = ({ tone = 'neutral', children, dot }) => {
  const tones = {
    up: { bg: 'var(--up-bg)', fg: 'var(--up-500)' },
    down: { bg: 'var(--down-bg)', fg: 'var(--down-500)' },
    warn: { bg: 'var(--warn-bg)', fg: 'var(--warn-500)' },
    info: { bg: 'var(--info-bg)', fg: 'var(--brand-300)' },
    neutral: { bg: 'var(--neutral-bg)', fg: 'var(--fg-2)' },
    outline: { bg: 'transparent', fg: 'var(--fg-2)', border: '1px solid var(--line-2)' },
    brand: { bg: 'rgba(216,138,28,0.12)', fg: 'var(--brand-300)' },
  };
  const t = tones[tone];
  return (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 5, padding: '3px 9px', borderRadius: 999, fontSize: 11, fontWeight: 600, background: t.bg, color: t.fg, border: t.border || '1px solid transparent', letterSpacing: '0.02em', whiteSpace: 'nowrap' }}>
      {dot && <span style={{ width: 6, height: 6, borderRadius: '50%', background: 'currentColor' }}/>}
      {children}
    </span>
  );
};

const Btn = ({ variant = 'primary', size = 'md', children, onClick, icon, iconRight, disabled, full }) => {
  const variants = {
    primary:   { bg: 'var(--brand-500)', fg: 'white', hover: 'var(--brand-400)', border: 'transparent' },
    secondary: { bg: 'var(--bg-3)', fg: 'var(--fg-1)', hover: 'var(--bg-4)', border: 'var(--line-2)' },
    ghost:     { bg: 'transparent', fg: 'var(--fg-2)', hover: 'var(--bg-3)', border: 'transparent' },
    danger:    { bg: 'var(--down-bg)', fg: 'var(--down-500)', hover: 'rgba(239,74,54,0.2)', border: 'rgba(239,74,54,0.3)' },
    success:   { bg: 'var(--up-bg)', fg: 'var(--up-500)', hover: 'rgba(43,168,76,0.2)', border: 'rgba(43,168,76,0.3)' },
  };
  const v = variants[variant];
  const sizes = { sm: { p: '5px 10px', fs: 12 }, md: { p: '8px 14px', fs: 13 }, lg: { p: '11px 18px', fs: 14 } };
  const s = sizes[size];
  const [hover, setHover] = React.useState(false);
  return (
    <button onClick={onClick} disabled={disabled}
      onMouseEnter={() => setHover(true)} onMouseLeave={() => setHover(false)}
      style={{
        display: 'inline-flex', alignItems: 'center', justifyContent: 'center', gap: 6,
        background: hover && !disabled ? v.hover : v.bg, color: v.fg,
        border: `1px solid ${v.border}`, padding: s.p, fontSize: s.fs, fontWeight: 500,
        borderRadius: 6, cursor: disabled ? 'not-allowed' : 'pointer',
        fontFamily: 'var(--font-sans)', opacity: disabled ? 0.4 : 1,
        width: full ? '100%' : 'auto',
        transition: 'background var(--dur-fast) var(--ease-out)'
      }}>
      {icon && <Icon name={icon} size={14}/>}
      {children}
      {iconRight && <Icon name={iconRight} size={14}/>}
    </button>
  );
};

const Card = ({ children, accent, style }) => (
  <div style={{
    background: 'var(--bg-2)',
    border: '1px solid var(--line-1)',
    borderLeft: accent ? `2px solid var(--${accent}-500)` : '1px solid var(--line-1)',
    borderRadius: 12,
    ...style
  }}>{children}</div>
);

const Num = ({ value, size = 'md', color }) => {
  const sizes = { sm: 13, md: 16, lg: 22, xl: 32 };
  return <span style={{
    fontFamily: 'var(--font-mono)', fontVariantNumeric: 'tabular-nums',
    fontWeight: 500, fontSize: sizes[size], color: color || 'var(--fg-1)', letterSpacing: '-0.01em'
  }}>{value}</span>;
};

const Label = ({ children, style }) => (
  <div style={{ fontSize: 10, fontWeight: 600, letterSpacing: '0.08em', textTransform: 'uppercase', color: 'var(--fg-3)', ...style }}>{children}</div>
);

const LegalFooter = () => (
  <div style={{
    padding: '10px 20px', fontSize: 11, color: 'var(--fg-3)',
    borderTop: '1px solid var(--line-1)', background: 'var(--bg-1)',
    display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 12, flexShrink: 0
  }}>
    <span>
      Signal Stack is in private evaluation and is not registered with SEBI.
      All outputs are tools-generated from your own configured scans.
      You are the sole decision-maker.
    </span>
    <a href="#" style={{ color: 'var(--brand-300)', textDecoration: 'none', whiteSpace: 'nowrap' }}>
      Full disclaimer →
    </a>
  </div>
);

// App shells -------------------------------------------------------------

const TopBar = ({ admin = false, search = true }) => (
  <header style={{
    height: 56, flexShrink: 0, display: 'flex', alignItems: 'center',
    padding: '0 20px', borderBottom: '1px solid var(--line-1)', background: 'var(--bg-1)', gap: 16
  }}>
    <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
      <Logo size={26}/>
      <span style={{ fontFamily: 'var(--font-display)', fontWeight: 700, fontSize: 16, letterSpacing: '-0.01em' }}>Signal Stack</span>
      {admin && <span style={{ padding: '2px 8px', borderRadius: 4, background: 'var(--brand-700)', color: 'white', fontSize: 10, fontWeight: 700, letterSpacing: '0.08em' }}>ADMIN</span>}
    </div>
    {search ? (
      <div style={{ flex: 1, maxWidth: 420, margin: '0 auto', position: 'relative' }}>
        <span style={{ position: 'absolute', left: 10, top: '50%', transform: 'translateY(-50%)', color: 'var(--fg-3)' }}>
          <Icon name="search" size={14}/>
        </span>
        <input placeholder="Search symbol · NSE:RELIANCE" style={{
          width: '100%', background: 'var(--bg-2)', border: '1px solid var(--line-1)',
          color: 'var(--fg-1)', padding: '7px 12px 7px 32px', borderRadius: 6, fontSize: 13
        }}/>
      </div>
    ) : <div style={{ flex: 1 }}/>}
    {admin
      ? <><Pill tone="warn" dot>Phase A</Pill><Pill tone="up" dot>All systems normal</Pill></>
      : <><Pill tone="outline">Phase A · Tester</Pill><Pill tone="up" dot>FYERS connected</Pill></>}
    <div style={{ width: 30, height: 30, borderRadius: '50%', background: 'var(--brand-700)', color: 'white', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 12, fontWeight: 600 }}>AR</div>
  </header>
);

const SideNav = ({ items, current }) => (
  <nav style={{ width: 220, flexShrink: 0, background: 'var(--bg-1)', borderRight: '1px solid var(--line-1)', padding: '14px 10px', display: 'flex', flexDirection: 'column', gap: 2 }}>
    {items.map(item => (
      <div key={item.id} style={{
        display: 'flex', alignItems: 'center', gap: 10,
        padding: current === item.id ? '9px 12px 9px 10px' : '9px 12px',
        background: current === item.id ? 'var(--bg-4)' : 'transparent',
        borderLeft: current === item.id ? '2px solid var(--brand-500)' : '2px solid transparent',
        color: current === item.id ? 'var(--fg-1)' : 'var(--fg-2)',
        fontSize: 13, fontWeight: 500, cursor: 'default'
      }}>
        <Icon name={item.icon} size={16}/>
        <span style={{ flex: 1 }}>{item.label}</span>
        {item.badge && <Pill tone={item.badgeTone || 'info'}>{item.badge}</Pill>}
      </div>
    ))}
    <div style={{ marginTop: 'auto', padding: '8px 12px', fontSize: 10, color: 'var(--fg-4)', letterSpacing: '0.06em', textTransform: 'uppercase' }}>System</div>
    <div style={{ display: 'flex', alignItems: 'center', gap: 10, padding: '9px 12px', color: 'var(--fg-2)', fontSize: 13, fontWeight: 500 }}>
      <Icon name="settings" size={16}/>Settings
    </div>
  </nav>
);

const userNav = [
  { id: 'dashboard',     label: 'Dashboard',      icon: 'home' },
  { id: 'chart',         label: 'Chart',          icon: 'chart-line' },
  { id: 'signals',       label: 'Signal Builder', icon: 'layers' },
  { id: 'notifications', label: 'Notifications',  icon: 'bell',    badge: 3, badgeTone: 'info' },
];

const adminNav = [
  { id: 'home',      label: 'Admin home',     icon: 'shield' },
  { id: 'approvals', label: 'User approvals', icon: 'users',    badge: 4, badgeTone: 'warn' },
  { id: 'universe',  label: 'Universe',       icon: 'layers' },
  { id: 'jobs',      label: 'Job runs',       icon: 'activity' },
  { id: 'calendar',  label: 'Trading calendar', icon: 'calendar' },
];

// Wraps a screen body in the app chrome (top bar + side nav + footer).
const Shell = ({ children, current, admin = false, search = true }) => (
  <div style={{ display: 'flex', flexDirection: 'column', height: '100%', background: 'var(--bg-0)' }}>
    <TopBar admin={admin} search={search}/>
    <div style={{ flex: 1, display: 'flex', minHeight: 0 }}>
      <SideNav items={admin ? adminNav : userNav} current={current}/>
      <main style={{ flex: 1, overflow: 'auto', background: 'var(--bg-0)' }}>{children}</main>
    </div>
    <LegalFooter/>
  </div>
);

// Form-ish helpers --------------------------------------------------------

const Field = ({ label, hint, children, full }) => (
  <label style={{ display: 'flex', flexDirection: 'column', gap: 6, gridColumn: full ? '1 / -1' : 'auto' }}>
    <span style={{ fontSize: 11, fontWeight: 600, letterSpacing: '0.04em', textTransform: 'uppercase', color: 'var(--fg-3)' }}>{label}</span>
    {children}
    {hint && <span style={{ fontSize: 11.5, color: 'var(--fg-3)' }}>{hint}</span>}
  </label>
);

const TextInput = (props) => (
  <input {...props} style={{
    background: 'var(--bg-1)', border: '1px solid var(--line-2)', color: 'var(--fg-1)',
    padding: '9px 12px', borderRadius: 6, fontFamily: 'var(--font-sans)', fontSize: 13, ...props.style
  }}/>
);

const Select = ({ children, ...rest }) => (
  <select {...rest} style={{
    background: 'var(--bg-1)', border: '1px solid var(--line-2)', color: 'var(--fg-1)',
    padding: '9px 12px', borderRadius: 6, fontFamily: 'var(--font-sans)', fontSize: 13, ...(rest.style || {})
  }}>{children}</select>
);

const Checkbox = ({ checked, label, sub }) => (
  <label style={{ display: 'flex', gap: 12, cursor: 'pointer', alignItems: 'flex-start' }}>
    <span style={{
      width: 18, height: 18, marginTop: 1, borderRadius: 4, flexShrink: 0,
      border: `1.5px solid ${checked ? 'var(--brand-500)' : 'var(--line-3)'}`,
      background: checked ? 'var(--brand-500)' : 'transparent',
      display: 'flex', alignItems: 'center', justifyContent: 'center'
    }}>
      {checked && <Icon name="check" size={12} color="white" stroke={3}/>}
    </span>
    <span style={{ flex: 1 }}>
      <div style={{ fontSize: 13, color: 'var(--fg-1)', fontWeight: 500 }}>{label}</div>
      {sub && <div style={{ fontSize: 12, color: 'var(--fg-3)', marginTop: 2, lineHeight: 1.5 }}>{sub}</div>}
    </span>
  </label>
);

const StatusDot = ({ tone = 'up' }) => (
  <span style={{ width: 8, height: 8, borderRadius: '50%', background: `var(--${tone}-500)`, display: 'inline-block' }}/>
);

// ─────────────────────────────────────────────────────────────────────────

Object.assign(window, {
  Icon, Logo, Pill, Btn, Card, Num, Label, LegalFooter,
  TopBar, SideNav, Shell, Field, TextInput, Select, Checkbox, StatusDot,
  userNav, adminNav,
});
