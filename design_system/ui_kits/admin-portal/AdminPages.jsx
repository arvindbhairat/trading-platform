// UserApprovals.jsx + UniverseManagement.jsx + JobRuns.jsx — combined for brevity.

const UserApprovals = () => {
  const users = [
    { email: 'priya.shah@email.com', name: 'Priya Shah', pan: 'AAAPS1234K', applied: '20 Apr · 2:14 PM', status: 'pending' },
    { email: 'rahul.m@gmail.com', name: 'Rahul Mehta', pan: 'AAKPM9876L', applied: '20 Apr · 11:32 AM', status: 'pending' },
    { email: 'arjun.k@outlook.com', name: 'Arjun Kapoor', pan: 'BCMPK5432N', applied: '19 Apr · 8:10 PM', status: 'pending' },
    { email: 'neha.p@yahoo.in', name: 'Neha Patel', pan: 'CDFPP1122M', applied: '18 Apr · 4:51 PM', status: 'pending' },
    { email: 'dev@signalstack.in', name: 'Dev Menon', pan: 'EGHPM4455P', applied: '15 Apr', status: 'approved' },
  ];
  return (
    <div style={{ padding: 24, maxWidth: 1400, margin: '0 auto', display: 'flex', flexDirection: 'column', gap: 18 }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-end' }}>
        <div><div style={{ fontSize: 10, fontWeight: 600, letterSpacing: '0.08em', textTransform: 'uppercase', color: 'var(--fg-3)' }}>User management</div><h1 style={{ margin: 0 }}>Approval queue</h1></div>
        <div style={{ display: 'flex', gap: 14, alignItems: 'center' }}>
          <div style={{ textAlign: 'right' }}>
            <div style={{ fontSize: 10, fontWeight: 600, letterSpacing: '0.08em', textTransform: 'uppercase', color: 'var(--fg-3)' }}>Tester ceiling</div>
            <Num value="18 / 30" size="lg"/>
          </div>
          <div style={{ width: 120, height: 6, background: 'var(--bg-3)', borderRadius: 3, overflow: 'hidden' }}>
            <div style={{ width: '60%', height: '100%', background: 'var(--brand-500)' }}/>
          </div>
        </div>
      </div>
      <Card>
        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
          <thead><tr>{['Email','Name','PAN','Applied','Status',''].map((h, i) => (
            <th key={i} style={{ textAlign: 'left', fontSize: 10, fontWeight: 600, letterSpacing: '0.08em', textTransform: 'uppercase', color: 'var(--fg-3)', padding: '12px 18px', borderBottom: '1px solid var(--line-1)' }}>{h}</th>
          ))}</tr></thead>
          <tbody>
            {users.map((u, i) => (
              <tr key={u.email} style={{ borderBottom: i < users.length - 1 ? '1px solid var(--line-1)' : 'none' }}>
                <td style={{ padding: '12px 18px', fontFamily: 'var(--font-mono)' }}>{u.email}</td>
                <td style={{ padding: '12px 18px' }}>{u.name}</td>
                <td style={{ padding: '12px 18px', fontFamily: 'var(--font-mono)', color: 'var(--fg-3)' }}>{u.pan}</td>
                <td style={{ padding: '12px 18px', fontFamily: 'var(--font-mono)', fontSize: 12, color: 'var(--fg-3)' }}>{u.applied}</td>
                <td style={{ padding: '12px 18px' }}>{u.status === 'pending' ? <Pill tone="warn" dot>Pending</Pill> : <Pill tone="up" dot>Approved</Pill>}</td>
                <td style={{ padding: '12px 18px', textAlign: 'right' }}>
                  {u.status === 'pending' && <div style={{ display: 'inline-flex', gap: 6 }}><Btn size="sm" variant="danger">Reject</Btn><Btn size="sm" variant="primary">Approve</Btn></div>}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </div>
  );
};

const UniverseManagement = () => {
  const syms = [
    ['RELIANCE','Reliance Industries','Energy','active'],
    ['TCS','Tata Consultancy Services','IT','active'],
    ['HDFCBANK','HDFC Bank','Financial','active'],
    ['INFY','Infosys','IT','active'],
    ['ICICIBANK','ICICI Bank','Financial','scan-excluded'],
    ['ITC','ITC Ltd','FMCG','active'],
    ['LT','Larsen & Toubro','Industrial','active'],
    ['AXISBANK','Axis Bank','Financial','active'],
  ];
  return (
    <div style={{ padding: 24, maxWidth: 1400, margin: '0 auto', display: 'flex', flexDirection: 'column', gap: 18 }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-end' }}>
        <div><div style={{ fontSize: 10, fontWeight: 600, letterSpacing: '0.08em', textTransform: 'uppercase', color: 'var(--fg-3)' }}>Universe</div><h1 style={{ margin: 0 }}>Nifty 500 symbol master</h1></div>
        <Btn variant="primary" icon="plus">Upload CSV</Btn>
      </div>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4,1fr)', gap: 12 }}>
        {[['Active','488','up'],['Scan-excluded','4','warn'],['Archived','8','neutral'],['Last synced','20 Apr 3:35 PM','info']].map(([l,v,t],i) => (
          <Card key={i} style={{ padding: 14 }}>
            <div style={{ fontSize: 10, fontWeight: 600, letterSpacing: '0.08em', textTransform: 'uppercase', color: 'var(--fg-3)', marginBottom: 6 }}>{l}</div>
            <Num value={v} size="lg" color={t === 'up' ? 'var(--up-500)' : t === 'warn' ? 'var(--warn-500)' : 'var(--fg-1)'}/>
          </Card>
        ))}
      </div>
      <Card>
        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
          <thead><tr>{['Symbol','Company','Industry','Status'].map((h,i) => (
            <th key={i} style={{ textAlign: 'left', fontSize: 10, fontWeight: 600, letterSpacing: '0.08em', textTransform: 'uppercase', color: 'var(--fg-3)', padding: '10px 18px', borderBottom: '1px solid var(--line-1)' }}>{h}</th>
          ))}</tr></thead>
          <tbody>
            {syms.map((s, i) => (
              <tr key={s[0]} style={{ borderBottom: i < syms.length - 1 ? '1px solid var(--line-1)' : 'none' }}>
                <td style={{ padding: '10px 18px', fontFamily: 'var(--font-mono)', fontWeight: 600 }}>{s[0]}</td>
                <td style={{ padding: '10px 18px' }}>{s[1]}</td>
                <td style={{ padding: '10px 18px', color: 'var(--fg-3)' }}>{s[2]}</td>
                <td style={{ padding: '10px 18px' }}>{s[3] === 'active' ? <Pill tone="up" dot>Active</Pill> : <Pill tone="warn" dot>Scan-excluded</Pill>}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </div>
  );
};

const JobRuns = () => {
  const jobs = [
    ['DS','DataSync','3:35 PM · 20 Apr','success','2m 14s','499/500 symbols'],
    ['EODSR','EOD Signal Runner','3:55 PM · 20 Apr','success','58s','12 signals'],
    ['LMDS','Live Market Data Scan','Running · since 9:15','running','6h 14m','7 positions'],
    ['LADS','Live Account Data Scan','Running · every 60s','running','—','18 users'],
    ['NDJ','Notification Delivery','3:56 PM · 20 Apr','success','8s','12 Telegram sent'],
    ['HDS','HistoricDataSeed','1:10 AM · 19 Apr','failed','4m 30s · timeout','3 symbols retried'],
  ];
  return (
    <div style={{ padding: 24, maxWidth: 1400, margin: '0 auto', display: 'flex', flexDirection: 'column', gap: 18 }}>
      <div><div style={{ fontSize: 10, fontWeight: 600, letterSpacing: '0.08em', textTransform: 'uppercase', color: 'var(--fg-3)' }}>Operations</div><h1 style={{ margin: 0 }}>Background jobs</h1></div>
      <Card>
        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
          <thead><tr>{['','Job','Last run','Status','Duration','Result',''].map((h,i) => (
            <th key={i} style={{ textAlign: 'left', fontSize: 10, fontWeight: 600, letterSpacing: '0.08em', textTransform: 'uppercase', color: 'var(--fg-3)', padding: '12px 18px', borderBottom: '1px solid var(--line-1)' }}>{h}</th>
          ))}</tr></thead>
          <tbody>
            {jobs.map((j, i) => {
              const tone = j[3] === 'success' ? 'up' : j[3] === 'failed' ? 'down' : 'info';
              return (
                <tr key={j[0]} style={{ borderBottom: i < jobs.length - 1 ? '1px solid var(--line-1)' : 'none' }}>
                  <td style={{ padding: '12px 18px', fontFamily: 'var(--font-mono)', fontSize: 11, color: 'var(--brand-300)', fontWeight: 700 }}>{j[0]}</td>
                  <td style={{ padding: '12px 18px', fontWeight: 500 }}>{j[1]}</td>
                  <td style={{ padding: '12px 18px', fontFamily: 'var(--font-mono)', fontSize: 12, color: 'var(--fg-3)' }}>{j[2]}</td>
                  <td style={{ padding: '12px 18px' }}><Pill tone={tone} dot>{j[3]}</Pill></td>
                  <td style={{ padding: '12px 18px', fontFamily: 'var(--font-mono)', fontSize: 12 }}>{j[4]}</td>
                  <td style={{ padding: '12px 18px', fontSize: 12, color: 'var(--fg-3)' }}>{j[5]}</td>
                  <td style={{ padding: '12px 18px', textAlign: 'right' }}>{j[3] === 'failed' ? <Btn size="sm" variant="secondary">Retry</Btn> : <Btn size="sm" variant="ghost">Logs</Btn>}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </Card>
    </div>
  );
};

Object.assign(window, { UserApprovals, UniverseManagement, JobRuns });
