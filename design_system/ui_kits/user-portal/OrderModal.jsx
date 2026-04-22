// OrderModal.jsx — FYERS handoff confirmation.

const OrderModal = ({ onClose }) => (
  <div style={{
    position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.6)',
    display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000
  }} onClick={onClose}>
    <div onClick={e => e.stopPropagation()} style={{
      background: 'var(--bg-2)', border: '1px solid var(--line-2)', borderRadius: 12,
      width: 480, boxShadow: 'var(--shadow-lg)', overflow: 'hidden'
    }}>
      <div style={{ padding: '16px 20px', borderBottom: '1px solid var(--line-1)', display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        <div>
          <div style={{ fontSize: 10, fontWeight: 600, letterSpacing: '0.08em', textTransform: 'uppercase', color: 'var(--fg-3)' }}>Order handoff · FYERS</div>
          <h3 style={{ margin: '2px 0 0 0' }}>Review order on FYERS</h3>
        </div>
        <button onClick={onClose} style={{ background: 'transparent', border: 'none', color: 'var(--fg-3)', cursor: 'pointer' }}><Icon name="x" size={18}/></button>
      </div>

      <div style={{ padding: 20 }}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 10, padding: 14, background: 'var(--bg-1)', border: '1px solid var(--line-1)', borderRadius: 8 }}>
          <div style={{ display: 'flex', justifyContent: 'space-between' }}><span style={{ color: 'var(--fg-3)', fontSize: 12 }}>Symbol</span><span style={{ fontFamily: 'var(--font-mono)', fontWeight: 600 }}>NSE:RELIANCE</span></div>
          <div style={{ display: 'flex', justifyContent: 'space-between' }}><span style={{ color: 'var(--fg-3)', fontSize: 12 }}>Side · product</span><span><Pill tone="up">BUY</Pill> <Pill tone="outline">CNC</Pill></span></div>
          <div style={{ display: 'flex', justifyContent: 'space-between' }}><span style={{ color: 'var(--fg-3)', fontSize: 12 }}>Quantity</span><Num value="52" size="sm"/></div>
          <div style={{ display: 'flex', justifyContent: 'space-between' }}><span style={{ color: 'var(--fg-3)', fontSize: 12 }}>Limit price</span><Num value="₹2,840.00" size="sm"/></div>
          <div style={{ borderTop: '1px solid var(--line-1)', paddingTop: 10, display: 'flex', justifyContent: 'space-between' }}><span style={{ fontWeight: 600 }}>Total</span><Num value="₹1,47,680" size="md"/></div>
        </div>

        <div style={{ marginTop: 14, padding: 12, background: 'rgba(245,165,36,0.08)', border: '1px solid rgba(245,165,36,0.25)', borderRadius: 6, fontSize: 12, color: 'var(--warn-500)' }}>
          Signal Stack does not place this order. Parameters will pre-populate the FYERS window; final submission, pricing, and execution are handled by FYERS. You are the sole decision-maker.
        </div>
      </div>

      <div style={{ padding: 16, borderTop: '1px solid var(--line-1)', display: 'flex', gap: 10, justifyContent: 'flex-end' }}>
        <Btn variant="ghost" onClick={onClose}>Cancel</Btn>
        <Btn variant="primary" icon="external">Continue to FYERS</Btn>
      </div>
    </div>
  </div>
);

window.OrderModal = OrderModal;
