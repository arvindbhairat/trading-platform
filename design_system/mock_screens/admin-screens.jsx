// Admin-side screens: approvals queue, universe management, job runs, trading calendar.

const AdminApprovals = () => {
  const queue = [
    { name: 'Priya Subramanian', email: 'priya.s@example.in', when: '28 Apr · 14:22', invite: 'ssk_q4f7…2c', source: 'Direct' },
    { name: 'Rahul Mehta',       email: 'rmehta@example.in',  when: '28 Apr · 11:08', invite: 'ssk_8m2k…1a', source: 'Referral · AR' },
    { name: 'Anjali Iyer',       email: 'anjali@example.in',  when: '27 Apr · 18:51', invite: 'ssk_x9j3…7d', source: 'Direct' },
    { name: 'Karan Bhatia',      email: 'kb@example.in',      when: '27 Apr · 09:14', invite: 'ssk_v2l8…3f', source: 'Referral · NK' },
  ];
  const active = [
    { name: 'Arvind Rao',  email: 'arvind@signalstack.in', joined: '02 Mar', last: '15:31', role: 'Admin' },
    { name: 'Nilesh Kale', email: 'nk@example.in',         joined: '14 Mar', last: '14:55', role: 'Tester' },
    { name: 'Meera Shah',  email: 'meera@example.in',      joined: '21 Mar', last: 'Yesterday', role: 'Tester' },
  ];

  return (
    <Shell admin current="approvals">
      <div style={{ padding: 24, maxWidth: 1280, margin: '0 auto', display: 'flex', flexDirection: 'column', gap: 18 }}>
        <div>
          <Label>Admin · Phase A</Label>
          <h1 style={{ marginTop: 4 }}>User approvals</h1>
          <p style={{ color: 'var(--fg-2)', fontSize: 14, marginTop: 4 }}>
            Hard cap of 30 active testers during private evaluation. Approvals capture document versions and append to the audit log.
          </p>
        </div>

        {/* Capacity strip */}
        <Card style={{ padding: '14px 18px', display: 'flex', gap: 24, alignItems: 'center' }}>
          <div>
            <Label>Active</Label>
            <div style={{ marginTop: 4, display: 'flex', alignItems: 'baseline', gap: 6 }}>
              <Num value="18" size="xl"/>
              <span style={{ color: 'var(--fg-3)', fontFamily: 'var(--font-mono)' }}>/ 30</span>
            </div>
          </div>
          <div style={{ flex: 1, height: 6, background: 'var(--bg-1)', borderRadius: 3, overflow: 'hidden', display: 'flex' }}>
            <div style={{ width: '60%', background: 'var(--up-500)' }}/>
            <div style={{ width: '14%', background: 'var(--warn-500)' }}/>
          </div>
          <div style={{ display: 'flex', gap: 18 }}>
            <div><Label>Pending</Label><div style={{ marginTop: 4 }}><Num value="4" size="lg" color="var(--warn-500)"/></div></div>
            <div><Label>Capacity</Label><div style={{ marginTop: 4 }}><Num value="12" size="lg" color="var(--up-500)"/></div></div>
            <div><Label>Waitlist</Label><div style={{ marginTop: 4 }}><Num value="11" size="lg"/></div></div>
          </div>
        </Card>

        {/* Pending */}
        <Card>
          <div style={{ padding: '14px 18px', borderBottom: '1px solid var(--line-1)', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <h2 style={{ margin: 0 }}>Pending requests <span style={{ color: 'var(--fg-3)', fontWeight: 400 }}>· {queue.length}</span></h2>
            <Btn variant="ghost" size="sm" icon="filter">Filter</Btn>
          </div>
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
            <thead><tr>{['Tester', 'Requested', 'Invite token', 'Source', ''].map((h, i) => (
              <th key={i} style={{ textAlign: i === 4 ? 'right' : 'left', fontSize: 10, fontWeight: 600, letterSpacing: '0.08em', textTransform: 'uppercase', color: 'var(--fg-3)', padding: '10px 18px', borderBottom: '1px solid var(--line-1)' }}>{h}</th>
            ))}</tr></thead>
            <tbody>
              {queue.map((u, i) => (
                <tr key={i} style={{ borderBottom: '1px solid var(--line-1)' }}>
                  <td style={{ padding: '12px 18px' }}>
                    <div style={{ display: 'flex', gap: 12, alignItems: 'center' }}>
                      <div style={{ width: 32, height: 32, borderRadius: '50%', background: 'var(--bg-3)', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 12, fontWeight: 600 }}>{u.name.split(' ').map(s => s[0]).join('')}</div>
                      <div>
                        <div style={{ fontWeight: 500 }}>{u.name}</div>
                        <div style={{ fontSize: 12, color: 'var(--fg-3)' }}>{u.email}</div>
                      </div>
                    </div>
                  </td>
                  <td style={{ padding: '12px 18px', color: 'var(--fg-2)', fontFamily: 'var(--font-mono)', fontSize: 12 }}>{u.when}</td>
                  <td style={{ padding: '12px 18px', fontFamily: 'var(--font-mono)', fontSize: 12, color: 'var(--fg-2)' }}>{u.invite}</td>
                  <td style={{ padding: '12px 18px' }}><Pill tone="outline">{u.source}</Pill></td>
                  <td style={{ padding: '12px 18px', textAlign: 'right' }}>
                    <div style={{ display: 'inline-flex', gap: 6 }}>
                      <Btn variant="ghost" size="sm">Reject</Btn>
                      <Btn variant="success" size="sm" icon="check">Approve</Btn>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </Card>

        {/* Active */}
        <Card>
          <div style={{ padding: '14px 18px', borderBottom: '1px solid var(--line-1)' }}>
            <h2 style={{ margin: 0 }}>Active testers <span style={{ color: 'var(--fg-3)', fontWeight: 400 }}>· 18</span></h2>
          </div>
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
            <thead><tr>{['Tester', 'Joined', 'Last active', 'Role', 'Status', ''].map((h, i) => (
              <th key={i} style={{ textAlign: i === 5 ? 'right' : 'left', fontSize: 10, fontWeight: 600, letterSpacing: '0.08em', textTransform: 'uppercase', color: 'var(--fg-3)', padding: '10px 18px', borderBottom: '1px solid var(--line-1)' }}>{h}</th>
            ))}</tr></thead>
            <tbody>
              {active.map((u, i) => (
                <tr key={i} style={{ borderBottom: i < active.length - 1 ? '1px solid var(--line-1)' : 'none' }}>
                  <td style={{ padding: '12px 18px' }}>
                    <div style={{ fontWeight: 500 }}>{u.name}</div>
                    <div style={{ fontSize: 12, color: 'var(--fg-3)' }}>{u.email}</div>
                  </td>
                  <td style={{ padding: '12px 18px', color: 'var(--fg-2)', fontFamily: 'var(--font-mono)', fontSize: 12 }}>{u.joined}</td>
                  <td style={{ padding: '12px 18px', color: 'var(--fg-2)', fontFamily: 'var(--font-mono)', fontSize: 12 }}>{u.last}</td>
                  <td style={{ padding: '12px 18px' }}><Pill tone={u.role === 'Admin' ? 'brand' : 'neutral'}>{u.role}</Pill></td>
                  <td style={{ padding: '12px 18px' }}><span style={{ display: 'inline-flex', alignItems: 'center', gap: 6, fontSize: 12 }}><StatusDot tone="up"/>FYERS connected</span></td>
                  <td style={{ padding: '12px 18px', textAlign: 'right' }}>
                    <Btn variant="ghost" size="sm">Manage</Btn>
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

window.AdminApprovals = AdminApprovals;

// Universe management — Nifty 500 sync state
const AdminUniverse = () => {
  const recent = [
    ['28 Apr 09:08 IST', 'Universe sync', 500, 499, 1, 'Completed', 'up'],
    ['27 Apr 09:08 IST', 'Universe sync', 500, 500, 0, 'Completed', 'up'],
    ['26 Apr 09:08 IST', 'Universe sync', 500, 498, 2, 'Completed · with skips', 'warn'],
    ['25 Apr 09:08 IST', 'Universe sync', 500, 500, 0, 'Completed', 'up'],
  ];
  const skips = [
    ['HFCL', 'Pending corp action interim record', '26 Apr', 'Suppressed'],
    ['DELHIVERY', 'F&O ban list — informational only', '28 Apr', 'Flagged'],
  ];
  return (
    <Shell admin current="universe">
      <div style={{ padding: 24, maxWidth: 1280, margin: '0 auto', display: 'flex', flexDirection: 'column', gap: 18 }}>
        <div style={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between' }}>
          <div>
            <Label>Admin</Label>
            <h1 style={{ marginTop: 4 }}>Universe & corporate actions</h1>
            <p style={{ color: 'var(--fg-2)', fontSize: 14, marginTop: 4 }}>
              Nifty 500 reconciliation. Symbols on the F&O ban list are flagged but never auto-blocked — testers stay in control.
            </p>
          </div>
          <Btn variant="primary" icon="refresh">Run universe sync now</Btn>
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 12 }}>
          {[
            ['Index members', '500', null],
            ['Synced today', '499', 'up'],
            ['Skipped', '1', 'warn'],
            ['F&O ban (info)', '7', null],
          ].map(([l, v, t], i) => (
            <Card key={i} style={{ padding: '14px 18px' }} accent={t}>
              <Label>{l}</Label>
              <div style={{ marginTop: 6 }}><Num value={v} size="xl" color={t === 'up' ? 'var(--up-500)' : t === 'warn' ? 'var(--warn-500)' : undefined}/></div>
            </Card>
          ))}
        </div>

        <Card>
          <div style={{ padding: '14px 18px', borderBottom: '1px solid var(--line-1)' }}>
            <h2 style={{ margin: 0 }}>Recent syncs</h2>
          </div>
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
            <thead><tr>{['When', 'Job', 'Members', 'OK', 'Skipped', 'Outcome', ''].map((h, i) => (
              <th key={i} style={{ textAlign: i === 6 ? 'right' : i >= 2 && i <= 4 ? 'right' : 'left', fontSize: 10, fontWeight: 600, letterSpacing: '0.08em', textTransform: 'uppercase', color: 'var(--fg-3)', padding: '10px 18px', borderBottom: '1px solid var(--line-1)' }}>{h}</th>
            ))}</tr></thead>
            <tbody>
              {recent.map((r, i) => (
                <tr key={i} style={{ borderBottom: i < recent.length - 1 ? '1px solid var(--line-1)' : 'none' }}>
                  <td style={{ padding: '11px 18px', fontFamily: 'var(--font-mono)', fontSize: 12, color: 'var(--fg-2)' }}>{r[0]}</td>
                  <td style={{ padding: '11px 18px' }}>{r[1]}</td>
                  <td style={{ padding: '11px 18px', textAlign: 'right' }}><Num value={r[2]} size="sm"/></td>
                  <td style={{ padding: '11px 18px', textAlign: 'right' }}><Num value={r[3]} size="sm" color="var(--up-500)"/></td>
                  <td style={{ padding: '11px 18px', textAlign: 'right' }}><Num value={r[4]} size="sm" color={r[4] > 0 ? 'var(--warn-500)' : 'var(--fg-3)'}/></td>
                  <td style={{ padding: '11px 18px' }}><Pill tone={r[6]}>{r[5]}</Pill></td>
                  <td style={{ padding: '11px 18px', textAlign: 'right' }}><Btn variant="ghost" size="sm" iconRight="external">Logs</Btn></td>
                </tr>
              ))}
            </tbody>
          </table>
        </Card>

        <Card>
          <div style={{ padding: '14px 18px', borderBottom: '1px solid var(--line-1)', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <h2 style={{ margin: 0 }}>Symbols requiring attention</h2>
            <Pill tone="warn">{skips.length} flagged</Pill>
          </div>
          {skips.map((s, i) => (
            <div key={i} style={{ padding: '14px 18px', display: 'flex', alignItems: 'center', gap: 14, borderBottom: i < skips.length - 1 ? '1px solid var(--line-1)' : 'none' }}>
              <div style={{ width: 32, height: 32, borderRadius: 8, background: 'var(--warn-bg)', display: 'flex', alignItems: 'center', justifyContent: 'center', color: 'var(--warn-500)' }}>
                <Icon name="alert-triangle" size={16}/>
              </div>
              <div style={{ flex: 1 }}>
                <div style={{ fontFamily: 'var(--font-mono)', fontWeight: 600, fontSize: 13 }}>{s[0]}</div>
                <div style={{ fontSize: 12.5, color: 'var(--fg-2)', marginTop: 2 }}>{s[1]}</div>
              </div>
              <span style={{ fontFamily: 'var(--font-mono)', fontSize: 12, color: 'var(--fg-3)' }}>{s[2]}</span>
              <Pill tone="outline">{s[3]}</Pill>
              <Btn variant="ghost" size="sm">Resolve</Btn>
            </div>
          ))}
        </Card>
      </div>
    </Shell>
  );
};

window.AdminUniverse = AdminUniverse;

// Job runs admin (REQ-OBS-001 etc)
const AdminJobs = () => {
  const jobs = [
    { name: 'DataSync',         schedule: '09:08 IST · daily',  last: '28 Apr 09:09', dur: '1m 12s', state: 'OK',     tone: 'up' },
    { name: 'Universe sync',    schedule: '09:08 IST · daily',  last: '28 Apr 09:09', dur: '0m 41s', state: 'OK',     tone: 'up' },
    { name: 'Signal Runner EOD',schedule: '15:35 IST · daily',  last: '28 Apr 15:38', dur: '3m 02s', state: 'Running',tone: 'info' },
    { name: 'Backtest Engine',  schedule: 'On demand',          last: '28 Apr 14:02', dur: '4m 28s', state: 'OK',     tone: 'up' },
    { name: 'Telegram dispatch',schedule: 'Continuous',         last: '28 Apr 15:31', dur: '0m 03s', state: 'OK',     tone: 'up' },
    { name: 'Audit log archive',schedule: '23:30 IST · daily',  last: '27 Apr 23:30', dur: '0m 18s', state: 'OK',     tone: 'up' },
  ];
  const log = [
    ['15:38:14', 'INFO',  'EOD Signal Runner completed in 3m 02s'],
    ['15:38:11', 'INFO',  'Notifications dispatched · 4 entries · 2 stops · 1 reduce'],
    ['15:35:09', 'INFO',  'EOD Signal Runner started · scope: 18 testers · 47 active scans'],
    ['15:32:47', 'WARN',  'Symbol HFCL skipped — pending corp action record'],
    ['15:31:02', 'INFO',  'Telegram dispatch · payload 7 messages · all delivered'],
    ['09:09:31', 'INFO',  'DataSync completed · 499/500 OK · 1 skip'],
    ['09:08:00', 'INFO',  'DataSync started · scope: Nifty 500'],
  ];
  return (
    <Shell admin current="jobs">
      <div style={{ padding: 24, maxWidth: 1280, margin: '0 auto', display: 'flex', flexDirection: 'column', gap: 18 }}>
        <div style={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between' }}>
          <div>
            <Label>Admin · Observability</Label>
            <h1 style={{ marginTop: 4 }}>Job runs</h1>
            <p style={{ color: 'var(--fg-2)', fontSize: 14, marginTop: 4 }}>
              Eight scheduled jobs run inside India business hours. Failures go straight to the on-call admin.
            </p>
          </div>
          <div style={{ display: 'flex', gap: 8 }}>
            <Btn variant="secondary" icon="download">Export logs</Btn>
            <Btn variant="primary" icon="play">Trigger job</Btn>
          </div>
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: '1.5fr 1fr', gap: 14 }}>
          <Card>
            <div style={{ padding: '14px 18px', borderBottom: '1px solid var(--line-1)' }}>
              <h2 style={{ margin: 0 }}>Scheduled jobs</h2>
            </div>
            <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
              <thead><tr>{['Job', 'Schedule', 'Last run', 'Duration', 'State', ''].map((h, i) => (
                <th key={i} style={{ textAlign: i === 5 ? 'right' : 'left', fontSize: 10, fontWeight: 600, letterSpacing: '0.08em', textTransform: 'uppercase', color: 'var(--fg-3)', padding: '10px 16px', borderBottom: '1px solid var(--line-1)' }}>{h}</th>
              ))}</tr></thead>
              <tbody>
                {jobs.map((j, i) => (
                  <tr key={i} style={{ borderBottom: i < jobs.length - 1 ? '1px solid var(--line-1)' : 'none' }}>
                    <td style={{ padding: '11px 16px', fontWeight: 500 }}>{j.name}</td>
                    <td style={{ padding: '11px 16px', color: 'var(--fg-2)', fontSize: 12 }}>{j.schedule}</td>
                    <td style={{ padding: '11px 16px', fontFamily: 'var(--font-mono)', fontSize: 12, color: 'var(--fg-2)' }}>{j.last}</td>
                    <td style={{ padding: '11px 16px', fontFamily: 'var(--font-mono)', fontSize: 12, color: 'var(--fg-2)' }}>{j.dur}</td>
                    <td style={{ padding: '11px 16px' }}>
                      <Pill tone={j.tone} dot={j.tone === 'info'}>{j.state}</Pill>
                    </td>
                    <td style={{ padding: '11px 16px', textAlign: 'right' }}>
                      <Btn variant="ghost" size="sm" iconRight="external">Open</Btn>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </Card>

          <Card>
            <div style={{ padding: '14px 18px', borderBottom: '1px solid var(--line-1)', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <h2 style={{ margin: 0 }}>Live log</h2>
              <Pill tone="up" dot>Streaming</Pill>
            </div>
            <div style={{ padding: 14, fontFamily: 'var(--font-mono)', fontSize: 12, lineHeight: 1.6 }}>
              {log.map((l, i) => (
                <div key={i} style={{ display: 'flex', gap: 10, padding: '4px 0' }}>
                  <span style={{ color: 'var(--fg-4)', minWidth: 64 }}>{l[0]}</span>
                  <span style={{
                    minWidth: 48, fontWeight: 600,
                    color: l[1] === 'WARN' ? 'var(--warn-500)' : l[1] === 'ERROR' ? 'var(--down-500)' : 'var(--up-500)'
                  }}>{l[1]}</span>
                  <span style={{ color: 'var(--fg-2)', flex: 1 }}>{l[2]}</span>
                </div>
              ))}
              <div style={{ display: 'flex', gap: 10, padding: '4px 0', color: 'var(--fg-3)' }}>
                <span style={{ minWidth: 64 }}>15:38:18</span><span style={{ minWidth: 48 }}>···</span><span>waiting</span>
                <span style={{ width: 8, height: 14, background: 'var(--brand-400)', animation: 'blink 1s steps(2) infinite' }}/>
              </div>
            </div>
          </Card>
        </div>
      </div>
      <style>{`@keyframes blink { 50% { opacity: 0; } }`}</style>
    </Shell>
  );
};

window.AdminJobs = AdminJobs;

// Trading calendar admin
const AdminCalendar = () => {
  // April 2026 grid
  const days = Array.from({length: 35}, (_, i) => {
    const d = i - 2; // April starts on Wed = idx 3 in 0-Sun grid
    const inMonth = d >= 1 && d <= 30;
    const date = inMonth ? d : null;
    const dow = i % 7;
    const weekend = dow === 0 || dow === 6;
    const holidays = { 1: 'Annual close', 14: 'Ambedkar Jayanti', 21: 'Mahavir Jayanti' };
    return { date, inMonth, weekend, holiday: holidays[date], today: date === 28 };
  });
  return (
    <Shell admin current="calendar">
      <div style={{ padding: 24, maxWidth: 1280, margin: '0 auto', display: 'flex', flexDirection: 'column', gap: 18 }}>
        <div style={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between' }}>
          <div>
            <Label>Admin</Label>
            <h1 style={{ marginTop: 4 }}>Trading calendar</h1>
            <p style={{ color: 'var(--fg-2)', fontSize: 14, marginTop: 4 }}>
              NSE holiday list and special sessions. Job runs and alerts skip non-trading days automatically.
            </p>
          </div>
          <div style={{ display: 'flex', gap: 8 }}>
            <Btn variant="secondary" icon="upload">Import NSE</Btn>
            <Btn variant="primary" icon="plus">Add holiday</Btn>
          </div>
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: '1.5fr 1fr', gap: 14 }}>
          <Card>
            <div style={{ padding: '14px 18px', borderBottom: '1px solid var(--line-1)', display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
              <h2 style={{ margin: 0 }}>April 2026</h2>
              <div style={{ display: 'flex', gap: 4 }}>
                <Btn variant="ghost" size="sm">←</Btn>
                <Btn variant="ghost" size="sm">Today</Btn>
                <Btn variant="ghost" size="sm">→</Btn>
              </div>
            </div>
            <div style={{ padding: 12 }}>
              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(7, 1fr)', gap: 4, marginBottom: 6 }}>
                {['Sun','Mon','Tue','Wed','Thu','Fri','Sat'].map(d => (
                  <div key={d} style={{ fontSize: 10, fontWeight: 600, letterSpacing: '0.08em', textTransform: 'uppercase', color: 'var(--fg-3)', textAlign: 'center', padding: '4px 0' }}>{d}</div>
                ))}
              </div>
              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(7, 1fr)', gap: 4 }}>
                {days.map((d, i) => {
                  let bg = 'var(--bg-1)', fg = 'var(--fg-1)', tone = null;
                  if (!d.inMonth) { bg = 'transparent'; fg = 'var(--fg-4)'; }
                  else if (d.holiday) { bg = 'var(--down-bg)'; fg = 'var(--down-500)'; tone = 'holiday'; }
                  else if (d.weekend) { bg = 'var(--bg-2)'; fg = 'var(--fg-3)'; }
                  return (
                    <div key={i} style={{
                      aspectRatio: '1.1', background: bg, border: d.today ? '1.5px solid var(--brand-500)' : '1px solid var(--line-1)',
                      borderRadius: 6, padding: 8, display: 'flex', flexDirection: 'column', justifyContent: 'space-between', overflow: 'hidden'
                    }}>
                      <div style={{ fontFamily: 'var(--font-mono)', fontSize: 13, fontWeight: d.today ? 700 : 500, color: fg }}>{d.date || ''}</div>
                      {d.holiday && <div style={{ fontSize: 9.5, color: 'var(--down-500)', fontWeight: 500, lineHeight: 1.2 }}>{d.holiday}</div>}
                      {d.today && <div style={{ fontSize: 9.5, color: 'var(--brand-300)', fontWeight: 600 }}>Today</div>}
                    </div>
                  );
                })}
              </div>
              <div style={{ display: 'flex', gap: 16, marginTop: 14, fontSize: 11, color: 'var(--fg-3)' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}><div style={{ width: 10, height: 10, background: 'var(--down-bg)', border: '1px solid var(--line-1)', borderRadius: 2 }}/>Holiday</div>
                <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}><div style={{ width: 10, height: 10, background: 'var(--bg-2)', border: '1px solid var(--line-1)', borderRadius: 2 }}/>Weekend</div>
                <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}><div style={{ width: 10, height: 10, background: 'var(--bg-1)', border: '1.5px solid var(--brand-500)', borderRadius: 2 }}/>Today</div>
              </div>
            </div>
          </Card>

          <Card>
            <div style={{ padding: '14px 18px', borderBottom: '1px solid var(--line-1)' }}>
              <h2 style={{ margin: 0 }}>Upcoming non-trading days</h2>
            </div>
            <div>
              {[
                ['Tue · 14 Apr 2026', 'Ambedkar Jayanti', 'Holiday', 'down'],
                ['Tue · 21 Apr 2026', 'Mahavir Jayanti', 'Holiday', 'down'],
                ['Fri · 01 May 2026', 'Maharashtra Day', 'Holiday', 'down'],
                ['Mon · 11 May 2026', 'Muhurat session 17:00–18:00', 'Special', 'warn'],
                ['Tue · 26 May 2026', 'Eid-ul-Fitr', 'Holiday', 'down'],
              ].map((h, i, arr) => (
                <div key={i} style={{ padding: '13px 18px', display: 'flex', alignItems: 'center', gap: 14, borderBottom: i < arr.length - 1 ? '1px solid var(--line-1)' : 'none' }}>
                  <div style={{ flex: 1 }}>
                    <div style={{ fontSize: 13, fontWeight: 500 }}>{h[1]}</div>
                    <div style={{ fontSize: 12, color: 'var(--fg-3)', fontFamily: 'var(--font-mono)' }}>{h[0]}</div>
                  </div>
                  <Pill tone={h[3]}>{h[2]}</Pill>
                </div>
              ))}
            </div>
            <div style={{ padding: '14px 18px', borderTop: '1px solid var(--line-1)', background: 'var(--bg-1)' }}>
              <div style={{ display: 'flex', alignItems: 'flex-start', gap: 10 }}>
                <Icon name="alert-triangle" size={14} color="var(--warn-500)"/>
                <span style={{ fontSize: 12, color: 'var(--fg-2)', lineHeight: 1.5 }}>
                  Calendar drift would silently send signals on non-trading days. Last NSE sync: <span style={{ fontFamily: 'var(--font-mono)', color: 'var(--fg-1)' }}>27 Apr · 18:00 IST</span>.
                </span>
              </div>
            </div>
          </Card>
        </div>
      </div>
    </Shell>
  );
};

window.AdminCalendar = AdminCalendar;
