// Shared primitive UI components for the User Portal
// Exports to window for cross-file sharing.

const Icon = ({ name, size = 18, color = 'currentColor', stroke = 1.75 }) => {
  // Minimal inline icon set (Lucide-style). Not exhaustive.
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
    'activity': <><polyline points="22 12 18 12 15 21 9 3 6 12 2 12"/></>,
    'home': <><path d="M3 12l9-9 9 9"/><path d="M5 10v10h5v-6h4v6h5V10"/></>,
    'users': <><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/></>,
    'clock': <><circle cx="12" cy="12" r="9"/><polyline points="12 7 12 12 15 14"/></>,
    'alert-triangle': <><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></>,
    'external': <><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"/><polyline points="15 3 21 3 21 9"/><line x1="10" y1="14" x2="21" y2="3"/></>,
    'trending-up': <><polyline points="23 6 13.5 15.5 8.5 10.5 1 18"/><polyline points="17 6 23 6 23 12"/></>,
    'trending-down': <><polyline points="23 18 13.5 8.5 8.5 13.5 1 6"/><polyline points="17 18 23 18 23 12"/></>,
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
  };
  const t = tones[tone];
  return (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 5, padding: '3px 9px', borderRadius: 999, fontSize: 11, fontWeight: 600, background: t.bg, color: t.fg, border: t.border || '1px solid transparent', letterSpacing: '0.02em' }}>
      {dot && <span style={{ width: 6, height: 6, borderRadius: '50%', background: 'currentColor' }}/>}
      {children}
    </span>
  );
};

const Btn = ({ variant = 'primary', size = 'md', children, onClick, icon, disabled }) => {
  const variants = {
    primary: { bg: 'var(--brand-500)', fg: 'white', hover: 'var(--brand-400)', border: 'transparent' },
    secondary: { bg: 'var(--bg-3)', fg: 'var(--fg-1)', hover: 'var(--bg-4)', border: 'var(--line-2)' },
    ghost: { bg: 'transparent', fg: 'var(--fg-2)', hover: 'var(--bg-3)', border: 'transparent' },
    danger: { bg: 'var(--down-bg)', fg: 'var(--down-500)', hover: 'rgba(239,59,59,0.2)', border: 'rgba(239,59,59,0.3)' },
  };
  const v = variants[variant];
  const sizes = { sm: { p: '5px 10px', fs: 12 }, md: { p: '8px 14px', fs: 13 }, lg: { p: '11px 18px', fs: 14 } };
  const s = sizes[size];
  const [hover, setHover] = React.useState(false);
  return (
    <button
      onClick={onClick}
      disabled={disabled}
      onMouseEnter={() => setHover(true)}
      onMouseLeave={() => setHover(false)}
      style={{
        display: 'inline-flex', alignItems: 'center', gap: 6,
        background: hover && !disabled ? v.hover : v.bg,
        color: v.fg, border: `1px solid ${v.border}`, padding: s.p,
        fontSize: s.fs, fontWeight: 500, borderRadius: 6, cursor: disabled ? 'not-allowed' : 'pointer',
        fontFamily: 'var(--font-sans)', opacity: disabled ? 0.4 : 1,
        transition: 'background var(--dur-fast) var(--ease-out)'
      }}>
      {icon && <Icon name={icon} size={14}/>}
      {children}
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

const Num = ({ value, delta, size = 'md', color }) => {
  const sizes = { sm: 13, md: 16, lg: 22, xl: 32 };
  return <span style={{
    fontFamily: 'var(--font-mono)', fontVariantNumeric: 'tabular-nums',
    fontWeight: 500, fontSize: sizes[size], color: color || 'var(--fg-1)', letterSpacing: '-0.01em'
  }}>{value}</span>;
};

const LegalFooter = () => (
  <div style={{
    padding: '10px 20px', fontSize: 11, color: 'var(--fg-3)',
    borderTop: '1px solid var(--line-1)', background: 'var(--bg-1)',
    display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 12
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

Object.assign(window, { Icon, Logo, Pill, Btn, Card, Num, LegalFooter });
