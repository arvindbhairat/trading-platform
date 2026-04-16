# Terms of Service

**Version:** v1
**Phase:** A (private validation)
**Last updated:** *(set at deployment)*
**Status:** In-house draft by the platform operator. Pending review by a registered Indian fintech lawyer before Phase B transition per `REQ-LEGAL-005`. These terms will be materially revised before the platform moves to commercial operation.

---

Please read these Terms of Service carefully before using the SignalStack platform. By accepting these Terms at signup — or by continuing to use the platform after a new version is published — you agree to be bound by them. If you do not agree, do not use the platform.

These Terms of Service are accepted as a separate affirmative action at signup, distinct from the Privacy Policy and the Phase A Tester Acknowledgement per REQ-LEGAL-008.

## 1. Who you are contracting with

The SignalStack platform ("SignalStack" or "the Platform") is operated personally by **Arvind Bhairat** ("we", "our", "us", or "the Operator"). No company or other legal entity operates the Platform at this time. The Operator is the data fiduciary under the Digital Personal Data Protection Act, 2023 (DPDP) for all personal data processed by the Platform.

Contact email for disputes, grievances, notices, and formal communication: `[GRIEVANCE_EMAIL]`.

## 2. Definitions

- **Platform** — the SignalStack portal (web), its associated backend services, and any integrations described in the Privacy Policy.
- **User** — any person who has been approved by the Operator to access the Platform during Phase A.
- **Account** — a User's registered identity on the Platform, linked to an OAuth provider.
- **FYERS** — the brokerage whose APIs the Platform integrates with for market data and order placement assistance.
- **Phase A** — the current private evaluation phase of the Platform, as described in Section 4.
- **Tester Acknowledgement** — the separate Phase A acknowledgement document that all Users must accept at signup, alongside these Terms and the Privacy Policy.

## 3. Eligibility

You may register and use the Platform only if you are all of the following:

- at least 18 years of age;
- capable of entering into a binding contract under Indian law;
- an Indian resident and tax resident, given the Platform operates on the National Stock Exchange of India's Nifty 500 universe;
- not prohibited from receiving financial-market-related services under any applicable law, regulatory order, or court order;
- personally invited to the Platform by the Operator during Phase A.

If any of these ceases to be true after you register, you must stop using the Platform and notify the Operator.

## 4. Phase A — what the Platform is and is not

The Platform is currently in Phase A, a private evaluation phase. During Phase A:

- the Platform is invite-only and not open to public registration;
- the Platform does not charge fees to any User;
- the Operator is not registered with the Securities and Exchange Board of India (SEBI) as an Investment Adviser, Research Analyst, Portfolio Manager, stock broker, sub-broker, or any other category of regulated financial intermediary;
- the Operator's personal FYERS API application is used for Platform integration, as disclosed in the Tester Acknowledgement;
- the Platform is under active development and may have defects, data quality issues, missed notifications, or incorrect values at any time.

The Platform is intended for your personal research, decision support, and record-keeping. It is not, and must not be treated as, investment advice.

## 5. Not investment advice

The Platform produces outputs — scan matches, position sizing values, stop levels, add and reduce advisories, portfolio heat readings, drawdown alerts, backtest results — by running automated tools against parameters you yourself configure. These outputs are informational. They are not investment advice, personal recommendations, or solicitations to buy or sell any security.

You are the sole decision-maker for every trade. The Platform never places orders on your behalf. Every order you place is routed through the FYERS API Connect widget using your own FYERS account credentials and confirmed by you before submission.

The full platform disclaimer is published separately and linked from every page of the Platform. You must read it.

## 6. Your account

### 6.1. Registration

To register, you sign in via an OAuth identity provider (Google, Microsoft, or Facebook/Meta). Your account is not active until the Operator has reviewed and approved it during Phase A.

### 6.2. Secondary identity and recovery

You may link a second OAuth identity to your account from the user settings page, so that you can continue signing in if you lose access to your primary identity. If you lose access to all linked identities, admin-assisted recovery is available through the grievance email, subject to identity verification requirements disclosed in the Platform.

### 6.3. Security of your account

You are responsible for maintaining the confidentiality of the OAuth credentials that control access to your account and your FYERS account. You must enable multi-factor authentication on any OAuth identity you link to the Platform. You must notify the Operator promptly if you suspect unauthorised access.

### 6.4. FYERS authentication

You must separately authenticate with FYERS to use any feature that depends on your FYERS account. The Platform does not store your FYERS login password. The FYERS access token obtained through this authentication is stored encrypted and treated as a day-scoped operational token.

### 6.5. Account deactivation

You may request account deactivation at any time from the user settings page. Deactivation is subject to lawful retention carve-outs disclosed in the Privacy Policy — some records (trade ledger, audit events) are retained for legally-required periods.

The Operator may deactivate your account at any time during Phase A, with or without cause, particularly if you breach these Terms, misuse the Platform, or if the Phase A tester cohort composition is revised.

## 7. Permitted and prohibited use

### 7.1. Permitted use

You may use the Platform for personal research, decision support, record-keeping, and user-initiated execution assistance against your own FYERS account.

### 7.2. Prohibited use

You may not do any of the following. Each item is an independent ground for immediate account termination.

- Use the Platform to commit, facilitate, or conceal any illegal activity, including market manipulation, insider trading, fraud, tax evasion, or money laundering.
- Republish, resell, redistribute, scrape, or commercially exploit any Platform content, scan output, or calculation.
- Attempt to reverse-engineer, decompile, or otherwise derive the source code, algorithms, or internal working of the Platform, except to the extent permitted by applicable law.
- Circumvent, disable, or interfere with any Platform security feature, rate limit, content-access restriction, or audit logging mechanism.
- Submit automated traffic, bots, or scripted requests to the Platform beyond the integrations the Platform itself offers.
- Share your account credentials or allow any other person to use your account.
- Access the Platform through any means other than the intended portal interface; in particular, do not call the Platform's internal APIs directly from outside the portal.
- Upload, submit, or transmit any content that is unlawful, defamatory, infringing, harmful, or that contains malware.
- Misrepresent your identity, age, or residency to the Platform.
- Use the Platform to offer any form of advice or recommendation to a third party, whether or not for consideration. The Platform's outputs are for your personal use only.

## 8. Intellectual property

All content, interfaces, code, designs, databases, and documentation that make up the Platform are owned by the Operator or its licensors. These Terms grant you a non-exclusive, non-transferable, revocable licence to access and use the Platform for the permitted uses described above during the term of your account. No other rights are granted.

Trademarks of third parties (FYERS, NSE, Microsoft, Google, Meta, Telegram, TradingView, and others) appear on the Platform where they identify those third parties' services. No rights in those trademarks are granted to you through the Platform.

## 9. Your data

How the Platform collects, uses, stores, retains, and protects your personal data is described in the Privacy Policy, which forms part of these Terms. The Privacy Policy is accepted as a distinct affirmative action at signup and remains accessible from the persistent portal footer.

The Platform is a data fiduciary under the Digital Personal Data Protection Act, 2023. Your rights — access, correction, erasure, and grievance — are exercised through the process described in the Privacy Policy.

## 10. Third-party services

The Platform integrates with third-party services that are operated independently of the Platform:

- **FYERS** — broker integration for market data and user-initiated order placement;
- **Microsoft, Google, Meta** — OAuth identity providers;
- **Telegram** — optional notification delivery;
- **Microsoft Azure** — hosting infrastructure;
- **TradingView** — fundamental data widget embeds on the chart page;
- **Transactional email provider** — admin-only operational notification fallback;
- **Observability vendor** — logs, traces, and metrics.

Each third-party service is governed by its own terms and privacy policies. The Operator is not responsible for actions, omissions, pricing, outages, or policies of these third parties. Your use of each third-party service through the Platform is additionally subject to that service's terms.

## 11. No fees during Phase A

The Platform charges no fees during Phase A. If the Platform transitions to a commercial phase (Phase C), the Operator will present updated terms describing the fee structure, and you will be asked to accept them before any charge is incurred. You remain free to stop using the Platform at any time.

## 12. Platform availability

The Platform is provided on an "as is" and "as available" basis. The Operator does not warrant that:

- the Platform will be available, uninterrupted, or error-free;
- any calculation, scan output, portfolio figure, stop level, or advisory will be accurate, current, or complete;
- any integration with FYERS, Telegram, or any other third party will function reliably;
- the Platform is free from defects, bugs, or security vulnerabilities.

Planned maintenance, third-party outages, security incidents, and platform defects may interrupt service without notice during Phase A. You must not rely on the Platform for time-sensitive trading decisions; you must have an alternative means of accessing your FYERS account and any other financial information you need.

## 13. Risk disclosure and limitation of liability

Trading in listed securities carries a real risk of loss, including the total loss of invested capital. Past performance, backtest results, and indicator readings displayed on the Platform do not predict future market behaviour.

To the fullest extent permitted by applicable law:

- the Operator makes no warranty — express, implied, statutory, or otherwise — about the accuracy, completeness, timeliness, reliability, suitability, availability, fitness for any particular purpose, or merchantability of the Platform or any content it produces;
- the Operator is not liable to you or to any third party for any direct, indirect, incidental, consequential, special, punitive, or exemplary loss or damage arising from or in connection with your use of, or inability to use, the Platform, including trading losses, missed trades, incorrect advisory values, delayed or missed notifications, platform downtime, data loss, or any action or omission of any third-party service;
- if, despite the exclusions above, the Operator is found liable to you for any matter arising out of or in connection with these Terms or your use of the Platform, the Operator's aggregate liability to you is capped at ₹10,000 (Indian Rupees ten thousand).

Nothing in this Section 13 limits any liability that cannot be limited under applicable Indian law, including liability for fraud, gross negligence, or death or personal injury caused by negligence.

## 14. Indemnity

You agree to indemnify the Operator and hold the Operator harmless against any claim, loss, liability, damage, cost, or expense (including reasonable legal fees) arising from or in connection with:

- your breach of these Terms, the Tester Acknowledgement, or the Privacy Policy;
- your misuse of the Platform or the FYERS integration;
- your trading decisions, trades, or any resulting financial outcome;
- your violation of any law applicable to your use of the Platform;
- any third-party claim arising from content you submitted, actions you took, or data you provided.

## 15. Termination

### 15.1. By you

You may stop using the Platform at any time. You may request account deactivation from the user settings page. Ongoing obligations under Sections 7.2, 8, 13, 14, 16, 17, and 18 survive termination.

### 15.2. By the Operator

The Operator may suspend or terminate your access to the Platform at any time and without notice, for any or no reason, including (without limitation):

- if you breach these Terms, the Tester Acknowledgement, or the Privacy Policy;
- if continued access would expose the Operator to regulatory, legal, or security risk;
- if the Phase A tester cohort is being wound down or restructured;
- if required by applicable law, regulator instruction, or court order.

On termination, your access to the Platform ceases immediately. Trade history, audit records, and other data are retained subject to the retention schedule in the Privacy Policy.

## 16. Changes to these Terms

The Operator may update these Terms from time to time. Material changes increment the version number. When the version is bumped, you will be required to re-accept the new version on your next login before regaining access to protected features. Your continued use of the Platform after re-acceptance constitutes your agreement to the updated Terms. If you do not agree to the updated Terms, you may request account deactivation.

## 17. Governing law and jurisdiction

These Terms are governed by and construed under the laws of the Republic of India. Any dispute arising out of or in connection with these Terms or your use of the Platform is subject to the exclusive jurisdiction of the courts in `[CITY_OF_OPERATION]`, India.

## 18. General

### 18.1. Severability

If any provision of these Terms is held to be unenforceable under applicable law, that provision is severed and the remaining provisions continue in full force and effect.

### 18.2. No waiver

A failure or delay by the Operator in enforcing any provision of these Terms does not constitute a waiver of the Operator's right to enforce that provision later.

### 18.3. Assignment

You may not assign your rights or obligations under these Terms. The Operator may assign these Terms, in whole or in part, to a successor entity, including on incorporation of a company to operate the Platform commercially.

### 18.4. Entire agreement

These Terms, the Privacy Policy, the Tester Acknowledgement, and the long-form disclaimer together constitute the entire agreement between you and the Operator about your use of the Platform, superseding any prior communications or representations.

### 18.5. Notices

Formal notices to the Operator must be sent to `[GRIEVANCE_EMAIL]`. Notices to you are given through the Platform notification feed, the email address on your OAuth identity, or both.

## 19. Contact

Questions about these Terms should be directed to `[GRIEVANCE_EMAIL]`.

---

## Change Log

- **v1** — Initial in-house draft. Phase A posture: Operator is Arvind Bhairat personally, no fees, invite-only, personal FYERS app, SEBI unregistered. Intended to be revised by a registered Indian fintech lawyer before Phase B transition.
