// Auth flow screens: sign-in, onboarding consents, FYERS connect, awaiting approval.

const SignIn = () => (
  <div style={{ height: '100%', display: 'flex', flexDirection: 'column', background: 'var(--bg-0)' }}>
    <div style={{ flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 40 }}>
      <div style={{ width: 420, display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 28 }}>
        <Logo size={48}/>
        <div style={{ textAlign: 'center' }}>
          <div className="t-display-2" style={{ fontFamily: 'var(--font-display)', fontWeight: 700, fontSize: 32, lineHeight: 1.15, letterSpacing: '-0.02em', color: 'var(--fg-1)' }}>
            Sign in to Signal Stack
          </div>
          <div style={{ fontSize: 14, color: 'var(--fg-3)', marginTop: 10, lineHeight: 1.55 }}>
            Decision support for Nifty 500 traders. Invite-only during private evaluation.
          </div>
        </div>

        <Card style={{ padding: 24, width: '100%', display: 'flex', flexDirection: 'column', gap: 12 }}>
          <button style={{
            display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 10,
            background: 'var(--bg-1)', border: '1px solid var(--line-2)', color: 'var(--fg-1)',
            padding: '12px 16px', borderRadius: 8, fontSize: 14, fontWeight: 500, cursor: 'pointer'
          }}>
            <svg width="18" height="18" viewBox="0 0 24 24"><path fill="#fff" d="M21.35 11.1H12v3.2h5.35a4.6 4.6 0 0 1-2 3v2.5h3.2c1.9-1.7 3-4.3 3-7.3 0-.5 0-1-.2-1.4z"/><path fill="#34a853" d="M12 22c2.7 0 5-1 6.55-2.4l-3.2-2.5c-.9.6-2 1-3.35 1a5.85 5.85 0 0 1-5.5-4H3.2v2.5A10 10 0 0 0 12 22z"/><path fill="#fbbc05" d="M6.5 14.1A6 6 0 0 1 6.2 12c0-.7.1-1.4.3-2v-2.5H3.2A10 10 0 0 0 2 12c0 1.6.4 3.1 1.2 4.6l3.3-2.5z"/><path fill="#ea4335" d="M12 5.85c1.5 0 2.85.5 3.9 1.5l2.85-2.85A10 10 0 0 0 12 2 10 10 0 0 0 3.2 7.5l3.3 2.5A5.85 5.85 0 0 1 12 5.85z"/></svg>
            Continue with Google
          </button>
          <button style={{
            display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 10,
            background: 'var(--bg-1)', border: '1px solid var(--line-2)', color: 'var(--fg-1)',
            padding: '12px 16px', borderRadius: 8, fontSize: 14, fontWeight: 500, cursor: 'pointer'
          }}>
            <Icon name="mail" size={16}/>
            Continue with email
          </button>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, margin: '4px 0' }}>
            <div style={{ flex: 1, height: 1, background: 'var(--line-1)' }}/>
            <span style={{ fontSize: 11, color: 'var(--fg-3)', letterSpacing: '0.06em', textTransform: 'uppercase' }}>or</span>
            <div style={{ flex: 1, height: 1, background: 'var(--line-1)' }}/>
          </div>
          <Field label="Invite token">
            <TextInput placeholder="ssk_•••••••••••••••" defaultValue=""/>
          </Field>
          <Btn variant="primary" full size="lg">Redeem invite</Btn>
        </Card>

        <div style={{ fontSize: 11.5, color: 'var(--fg-3)', textAlign: 'center', lineHeight: 1.6, maxWidth: 360 }}>
          By continuing you agree that Signal Stack is decision support, not advice, and that the platform is not registered with SEBI. <a href="#" style={{ color: 'var(--brand-300)' }}>Terms</a> · <a href="#" style={{ color: 'var(--brand-300)' }}>Privacy</a>
        </div>
      </div>
    </div>
    <LegalFooter/>
  </div>
);

window.SignIn = SignIn;

// Onboarding — three distinct consents (REQ-LEGAL-008 + REQ-PRIVACY-003 + REQ-LEGAL-004)
const Onboarding = () => {
  const steps = ['Account', 'Acknowledgements', 'FYERS', 'Risk profile'];
  return (
    <div style={{ height: '100%', display: 'flex', flexDirection: 'column' }}>
      <TopBar search={false}/>
      <div style={{ flex: 1, overflow: 'auto', padding: '32px 24px', background: 'var(--bg-0)' }}>
        <div style={{ maxWidth: 760, margin: '0 auto', display: 'flex', flexDirection: 'column', gap: 24 }}>
          {/* Stepper */}
          <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
            {steps.map((s, i) => {
              const active = i === 1, done = i < 1;
              return (
                <React.Fragment key={s}>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                    <span style={{
                      width: 22, height: 22, borderRadius: '50%',
                      background: done ? 'var(--up-500)' : active ? 'var(--brand-500)' : 'var(--bg-3)',
                      color: done || active ? 'white' : 'var(--fg-3)',
                      fontSize: 11, fontWeight: 700,
                      display: 'flex', alignItems: 'center', justifyContent: 'center'
                    }}>{done ? <Icon name="check" size={12} stroke={3}/> : i + 1}</span>
                    <span style={{ fontSize: 12.5, color: active ? 'var(--fg-1)' : 'var(--fg-3)', fontWeight: active ? 600 : 500 }}>{s}</span>
                  </div>
                  {i < steps.length - 1 && <div style={{ flex: 1, height: 1, background: 'var(--line-1)' }}/>}
                </React.Fragment>
              );
            })}
          </div>

          <div>
            <Label>Step 2 of 4</Label>
            <h1 style={{ marginTop: 4 }}>Acknowledgements</h1>
            <p style={{ color: 'var(--fg-2)', fontSize: 14, marginTop: 6, maxWidth: 540 }}>
              Three separate acceptances are required before you can access the portal. Each is recorded with the document version, your identity, and a timestamp.
            </p>
          </div>

          <Card style={{ padding: 24, display: 'flex', flexDirection: 'column', gap: 18 }}>
            <Checkbox checked={true} label="Terms of Service · v1.0"
              sub="Defines platform usage, the long-only Nifty 500 scope, and the operator's responsibilities. Updated 12 Apr 2026."/>
            <div style={{ height: 1, background: 'var(--line-1)' }}/>
            <Checkbox checked={true} label="Privacy notice · v1.0"
              sub="Personal data collected: identity, FYERS-linked trading data, Telegram chat ID. Stored in Azure India regions only. Grievance officer: arvind@signalstack.in."/>
            <div style={{ height: 1, background: 'var(--line-1)' }}/>
            <Checkbox checked={false} label="Tester acknowledgement · Phase A v1.2"
              sub="The platform is in private evaluation and is not registered with SEBI. All outputs are tools-generated from your own configured scans and risk profile. You are the sole decision-maker. The operator accepts no responsibility for trading outcomes."/>
            <div style={{ height: 1, background: 'var(--line-1)' }}/>
            <Checkbox checked={true} label="I confirm I am 18 or older"
              sub="Required by the DPDP Act, 2023. Signal Stack does not accept signups from minors."/>
          </Card>

          <div style={{ padding: 14, background: 'var(--warn-bg)', border: '1px solid rgba(228,160,48,0.25)', borderRadius: 8, display: 'flex', gap: 12, alignItems: 'flex-start' }}>
            <Icon name="alert-triangle" size={18} color="var(--warn-500)"/>
            <div style={{ fontSize: 12.5, color: 'var(--fg-2)', lineHeight: 1.55 }}>
              You must accept all three documents to continue. If any document version changes later, you'll be asked to re-accept on your next sign-in.
            </div>
          </div>

          <div style={{ display: 'flex', gap: 10, justifyContent: 'flex-end' }}>
            <Btn variant="ghost">Back</Btn>
            <Btn variant="primary" iconRight="arrow-right" disabled>Continue to FYERS</Btn>
          </div>
        </div>
      </div>
      <LegalFooter/>
    </div>
  );
};

window.Onboarding = Onboarding;

// FYERS connect step
const FyersConnect = () => (
  <div style={{ height: '100%', display: 'flex', flexDirection: 'column' }}>
    <TopBar search={false}/>
    <div style={{ flex: 1, overflow: 'auto', padding: '32px 24px', background: 'var(--bg-0)' }}>
      <div style={{ maxWidth: 600, margin: '0 auto', display: 'flex', flexDirection: 'column', gap: 24 }}>
        <div>
          <Label>Step 3 of 4</Label>
          <h1 style={{ marginTop: 4 }}>Connect your FYERS account</h1>
          <p style={{ color: 'var(--fg-2)', fontSize: 14, marginTop: 6 }}>
            Signal Stack uses your FYERS account to read your holdings, positions, and trades. Order placement is handed back to FYERS — Signal Stack never submits orders on your behalf.
          </p>
        </div>

        <Card style={{ padding: 24, display: 'flex', flexDirection: 'column', gap: 18 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 14, padding: 14, background: 'var(--bg-1)', borderRadius: 8 }}>
            <div style={{ width: 40, height: 40, borderRadius: 8, background: 'rgba(43,168,76,0.15)', display: 'flex', alignItems: 'center', justifyContent: 'center', color: 'var(--up-500)' }}>
              <Icon name="lock" size={20}/>
            </div>
            <div style={{ flex: 1 }}>
              <div style={{ fontSize: 13, fontWeight: 600 }}>Read-only by design</div>
              <div style={{ fontSize: 12, color: 'var(--fg-3)' }}>We request `read` scopes only. We cannot place, modify, or cancel orders.</div>
            </div>
          </div>

          {[
            ['Holdings', 'Long-term equity holdings — read-only'],
            ['Positions & orders', 'Live intraday + delivery positions and order history'],
            ['Profile', 'Client ID and account metadata for FIFO accounting'],
          ].map(([t, s]) => (
            <div key={t} style={{ display: 'flex', alignItems: 'flex-start', gap: 12 }}>
              <Icon name="check" size={16} color="var(--up-500)"/>
              <div>
                <div style={{ fontSize: 13, fontWeight: 500 }}>{t}</div>
                <div style={{ fontSize: 12, color: 'var(--fg-3)' }}>{s}</div>
              </div>
            </div>
          ))}

          <div style={{ display: 'flex', gap: 10, marginTop: 6 }}>
            <Btn variant="primary" size="lg" icon="external" full>Continue to FYERS</Btn>
            <Btn variant="ghost" size="lg">Skip for now</Btn>
          </div>
          <div style={{ fontSize: 11.5, color: 'var(--fg-3)', textAlign: 'center' }}>
            You'll return here automatically after authorisation.
          </div>
        </Card>
      </div>
    </div>
    <LegalFooter/>
  </div>
);

window.FyersConnect = FyersConnect;

// Awaiting approval state (REQ-ROLE-004)
const AwaitingApproval = () => (
  <div style={{ height: '100%', display: 'flex', flexDirection: 'column' }}>
    <TopBar search={false}/>
    <div style={{ flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 40, background: 'var(--bg-0)' }}>
      <div style={{ width: 480, textAlign: 'center', display: 'flex', flexDirection: 'column', gap: 20, alignItems: 'center' }}>
        <div style={{ width: 64, height: 64, borderRadius: 16, background: 'rgba(228,160,48,0.12)', border: '1px solid rgba(228,160,48,0.3)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
          <Icon name="clock" size={28} color="var(--warn-500)"/>
        </div>
        <div>
          <h1 style={{ margin: 0 }}>You're on the waitlist</h1>
          <p style={{ color: 'var(--fg-2)', fontSize: 14, marginTop: 8, lineHeight: 1.55 }}>
            Signal Stack is in private evaluation with a hard cap of 30 active testers. We've notified the admin of your request — you'll receive an email when your account is approved.
          </p>
        </div>

        <Card style={{ padding: 18, width: '100%', display: 'flex', flexDirection: 'column', gap: 12 }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <span style={{ fontSize: 12, color: 'var(--fg-3)' }}>Your position in queue</span>
            <Num value="#7" size="md" color="var(--brand-300)"/>
          </div>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <span style={{ fontSize: 12, color: 'var(--fg-3)' }}>Active testers</span>
            <Num value="18 / 30" size="md"/>
          </div>
          <div style={{ height: 4, background: 'var(--bg-1)', borderRadius: 2, overflow: 'hidden' }}>
            <div style={{ height: '100%', width: '60%', background: 'var(--brand-500)' }}/>
          </div>
          <div style={{ fontSize: 11, color: 'var(--fg-3)' }}>Requested · 28 Apr 2026 at 14:22 IST</div>
        </Card>

        <div style={{ display: 'flex', gap: 8 }}>
          <Btn variant="ghost" icon="mail">Update email</Btn>
          <Btn variant="ghost">Sign out</Btn>
        </div>
      </div>
    </div>
    <LegalFooter/>
  </div>
);

window.AwaitingApproval = AwaitingApproval;
