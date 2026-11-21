# Security, Data-Integrity, Privacy and Architecture Considerations for a Clock In/Out Web App

Scope note: research budget was limited (about 17 tool calls). Several primary sources were unreachable (ilga.gov refused connection; ICO biometrics page returned 403). Where a claim rests on general engineering knowledge rather than a fetched source, it is placed under Inferences, not Cited Findings. Sections follow the key questions.

## 1. Time-fraud and integrity (buddy punching, device/time/GPS manipulation, tamper-evidence, corrections)

### Takeaway
Browser-reported location and device clocks are user-controlled and cannot be trusted; integrity must come from server-side timestamps, an append-only event model, and a recorded-reason correction workflow. These are cheap at schema-design time and very expensive to retrofit. No independent source was found quantifying buddy-punching losses or browser-GPS-spoof detection efficacy.

### Cited Findings
- Chrome/Edge DevTools "Sensors" lets anyone override the browser Geolocation API with preset cities or custom lat/long — [Microsoft Edge DevTools docs](https://learn.microsoft.com/microsoft-edge/devtools/sensors)
- Extensions can additionally override accuracy, heading, speed and altitude; Firefox lacked an equivalent native override in the sources found (Mozilla bug open) — [Chrome Web Store extension listing](https://chromewebstore.google.com/detail/devtools-advanced-locatio/pboglnnmnkkbdgflckmjhakpdejmnpli); [Mozilla Bugzilla 1069882](https://bugzilla.mozilla.org/show_bug.cgi?id=1069882)
- A vendor guide says the DevTools override changes only the HTML5 API response and does nothing to the IP address, so IP/timezone/Accept-Language mismatches are a possible (non-conclusive) cross-check — [iMobie guide (commercial source)](https://www.imobie.com/location-change/chrome-location-spoofer-qa-guide.htm)
- FLSA regulations require no particular timekeeping method; a time clock or timekeeper is fine "as long as it is complete and accurate"; employers with fixed schedules may record the schedule and record actual hours on an exception basis — [US DOL Fact Sheet 21](https://www.dol.gov/agencies/whd/fact-sheets/21-flsa-recordkeeping)
- DCAA expects an audit trail of timesheet changes including approvals and rounding (from earlier report) — [Paycor](https://www.paycor.com/resource-center/articles/6-considerations-for-employee-attendance-policies)
- Real-world integrity/availability failure: the Dec 2021 ransomware attack on UKG's Kronos cloud took client timekeeping/payroll offline for weeks; workers sued over short paychecks computed by averaging prior hours; core capabilities restored by late January. SHRM calls the service "Public Cloud," TechTarget "Private Cloud" (discrepancy) — [TechTarget](https://www.techtarget.com/enterprise-software/news/252512253/Tesla-PepsiCo-workers-bring-lawsuit-over-UKG-payroll-outage); [SHRM](https://www.shrm.org/resourcesandtools/hr-topics/technology/pages/ukg-hack-fallout-includes-lawsuits-data-breaches.aspx)

### Inferences
- Browser GPS is a soft signal only: store lat/lng + accuracy + server-observed IP/geo + a "verification status" flag, never use it as a hard gate unless a native app with attestation is added. Flag-and-review beats block.
- Tamper-evident design (cheap early): server-assigned UTC timestamp on receipt, plus client-claimed time and offset stored separately (so offline punches show both); punches are immutable insert-only rows; corrections are new "adjustment" rows referencing the original with actor, reason, before/after, approver; DB role for the app has no UPDATE/DELETE on the event table. Optional hash-chain per tenant for tamper evidence (moderate effort, mostly useful for enterprise/audit sales). This is general engineering practice, not drawn from a cited source.
- Buddy punching: non-biometric mitigations are cheap (per-user credentials/PIN + device binding, kiosk with photo-on-punch where lawful, impossible-travel and duplicate-device anomaly flags); biometric mitigation brings legal cost (see section 2).
- Retrofitting an insert-only event model onto a mutable "timesheet row" design requires data migration and loses history already overwritten; it cannot be reconstructed.

### Gaps
- No independent study on buddy-punching prevalence or on false-positive rates of IP/timezone cross-checks.
- No primary source on WebAuthn/passkey-based device binding for punches or on browser attestation options; not researched.

## 2. Privacy and legal minimums (GDPR/UK GDPR, BIPA, FLSA, EU working time, Israel)

### Takeaway
FLSA retention is verified from primary sources: 3 years for payroll records, 2 years for time cards/wage computation records, available within 72 hours if kept centrally. EU law requires an "objective, reliable and accessible" daily-working-time system. Location tracking and biometrics are the high-risk data types: collect only during shifts, prefer less intrusive alternatives, and make retention/deletion configurable from day one.

### Cited Findings
**FLSA (US) — verified against primary sources**
- Payroll records, collective bargaining agreements, sales/purchase records: 3 years. Time cards, wage rate tables, work/time schedules, records of wage additions/deductions: 2 years — [US DOL Fact Sheet 21](https://www.dol.gov/agencies/whd/fact-sheets/21-flsa-recordkeeping)
- Regulation text: 29 CFR 516.5 (3 years from last date of entry) and 516.6 (2 years); 516.7 requires records kept at place of employment or a central office, produced within 72 hours of notice; 516.1(a) allows automated data processing memory if viewing equipment is available, copies are clear and identifiable by date/pay period, and transcriptions are provided on request — [eCFR Title 29 Part 516](https://www.ecfr.gov/api/renderer/v1/content/enhanced/current/title-29?part=516)
- Required per-worker data includes the time and day the workweek begins, hours worked each day, total hours each workweek, regular rate, straight-time and overtime earnings, deductions, total wages and pay period dates; the Fact Sheet itself says the Act "requires no particular form" — [US DOL Fact Sheet 21](https://www.dol.gov/agencies/whd/fact-sheets/21-flsa-recordkeeping)

**EU working time**
- CJEU Case C-55/18 (14 May 2019, CCOO v Deutsche Bank): Member States must require employers to set up an objective, reliable and accessible system measuring daily working time; recording only overtime is not enough — [FRA case-law summary](https://fra.europa.eu/sv/caselaw-reference/cjeu-case-c-5518-judgment); [CJEU press release](https://curia.europa.eu/jcms/upload/docs/application/pdf/2019-05/cp190061en.pdf)
- Spain anticipated the ruling via Royal Decree-Law 8/2019 (previously only overtime was recorded) — [Courthouse News / search summary](https://courthousenews.com/eu-employers-must-track-hours-not-just-overtime-court-rules/)
- Implication in the earlier report: 48-hour average week, 11 hours daily rest, 24 hours weekly rest — [Your Europe](https://europa.eu/youreurope/business/human-resources/working-hours-holiday-leave/working-hours/index_en.htm)

**GDPR / UK GDPR: location and monitoring**
- Irish/UK-side guidance summaries: tracking should be limited to working hours; tracking precise location where a simple entry log would suffice is unlikely to be justified; ICO small-business blog says employers should be able to turn off tracking outside working hours because recording then would be unfair and potentially unlawful; DPIA emphasized for monitoring — [Law Society of Ireland Gazette (Mar 2026)](https://lawsociety.ie/gazette/top-stories/2026/march/hard-limits-on-a-bosss-right-to-track-workers); [ICO blog](https://ico.org.uk/for-organisations/advice-for-small-organisations/whats-new/blogs/employee-monitoring-is-it-right-for-your-business/); [ICO announcement of guidance](https://ico.org.uk/about-the-ico/media-centre/news-and-blogs/2023/10/ico-publishes-guidance-to-ensure-lawful-monitoring-in-the-workplace)
- Austrian DPA (1 Mar 2022) ordered an employer to stop GPS tracking of 15 company cars because legitimate interest did not outweigh employee rights when manual records already captured working hours accurately (no fine amount stated) — [Datenschutz-Notizen](https://www.datenschutz-notizen.de/unlawful-use-of-a-gps-tracking-tool-installed-in-company-cars-was-found-by-the-austrian-dpa-5045693/)
- French Cour de cassation: geolocation used to monitor working time is unlawful if the monitoring can be done by another means; Belgian DPA says employers must be able to disable tracking outside working hours; Luxembourg CNPD says it must be the only available means — [Hunton](https://www.hunton.com/privacy-and-cybersecurity-law-blog/french-court-of-cassation-sanctions-company-for-misuse-of-a-geolocation-device); [Ius Laboris](https://iuslaboris.com/insights/tracking-service-vehicles-in-luxembourg-data-privacy-pointers-for-employers/) (these are secondary summaries)
- ICO biometric-for-attendance page (ICO says fingerprints are special category; less intrusive alternatives expected) was cited in the earlier report; the page itself returned HTTP 403 for this researcher, so not re-verified — [ICO](https://ico.org.uk/for-organisations/uk-gdpr-guidance-and-resources/employment/monitoring-workers/can-we-use-biometric-data-for-time-and-attendance-control-and-monitoring/)

**Illinois BIPA**
- SB 2979 (effective 2 Aug 2024): damages accrue per person rather than per violation for repeated 15(b)/15(d) violations; other sections' damages unchanged; electronic signatures defined to allow clickwrap-style written releases — [Baker Donelson](https://www.bakerdonelson.com/bipa-amendments-passed-by-illinois-legislature-what-companies-need-to-know); [Duane Morris](https://blogs.duanemorris.com/classactiondefense/2024/08/05/illinois-corrects-the-bipas-cataclysmic-jobs-killing-damages-regime-in-new-reform-legislation/)
- One secondary report says the Seventh Circuit held on 1 Apr 2026 that the per-person amendment applies retroactively; not verified against the opinion — [Search summary of law-firm sources; see also JD Supra](https://jdsupra.com/legalnews/bipa-amendments-passed-by-illinois-3541943)
- Pending 2026 bill SB3122 would amend Section 15; status not determined — [ILGA](https://ilga.gov/ftp/legislation/104/SB/10400SB3122.htm)

**Israel (brief)**
- Section 25 of the Hours of Work and Rest Law places personal responsibility on the employer to keep records of actual working hours, weekly rest, overtime and pay; in the absence of records the burden of proof reportedly shifts to the employer — [Chambers 2025 Israel](https://practiceguides.chambers.com/practice-guides/employment-2025/israel); [Jerusalem Post](https://www.jpost.com/business-and-innovation/banking-and-finance/article-901060). Retention period conflicts: Harvest says 7 years, Rivermate says typically 3; both commercial, neither verified against statute — [Harvest](https://www.getharvest.com/time-tracking/timesheet-software-in-israel); [Rivermate](https://rivermate.com/guides/israel/working-hours)

### Inferences
- Because FLSA minimums (2-3 years) differ from other regimes (UK holiday records reportedly 6 years; Israel unclear), build retention as a per-tenant, per-record-class configurable policy with a legal-hold flag, not a hard-coded value. Cheap early; costly later because purge jobs and backups touch every table.
- Cross-border design: the same product must let a tenant disable GPS entirely, capture location only at punch (not continuously), and store coarse (geofence-match yes/no) instead of raw coordinates by default. Punch-time-only location is the data-minimizing default and is consistent with the "only during working hours" guidance.
- Photos/biometrics: ship non-biometric default; if photo-on-punch is offered, treat as personal data with short retention and tenant-set toggle; if face/fingerprint templates are offered, require consent capture, retention schedule, deletion, and a fallback method (matches the earlier report's recommendation).
- Data-subject rights (access/export/erasure of non-statutory data) need a user-keyed data model; retrofitting is hard if identifiers are scattered across logs and analytics.

### Gaps
- BIPA Section 15(a)'s retention/destruction wording (commonly stated as destroy at purpose satisfied or within 3 years of last interaction, whichever first) and Section 20 damages ($1,000 negligent / $5,000 reckless) could not be verified from ilga.gov (connection refused); the $1,000-$5,000 range is only from the earlier report's Littler cite.
- Texas CUBI and Washington biometric laws not researched.
- EDPB guidance on location/biometrics (e.g., Guidelines 3/2019) not retrieved; ICO biometric page not retrievable.
- Israel retention period and caps unresolved; statute/Ministry of Labor text not reached. Germany's post-2019 time-recording obligation not confirmed (search returned no details).
- UK six-year holiday record rule is from the earlier report only; not re-verified.

## 3. Security basics (authentication, RBAC, multi-tenancy, rate limiting, OWASP, SOC 2)

### Takeaway
Tenant isolation and RBAC/object-level authorization are the costliest to retrofit; use Postgres RLS (non-owner role, transaction-scoped tenant context) as a safety net over application-level checks. SOC 2 is the typical gate for business customers: Type 1 to unblock early deals, Type 2 for enterprise; costs and timelines are vendor-estimated.

### Cited Findings
- OWASP API Security Top 10 (2023): API1 Broken Object Level Authorization, API2 Broken Authentication, API3 Broken Object Property Level Authorization, API4 Unrestricted Resource Consumption, API5 Broken Function Level Authorization, API6 Unrestricted Access to Sensitive Business Flows, API7 SSRF, API8 Security Misconfiguration, API9 Improper Inventory Management, API10 Unsafe Consumption of APIs — [OWASP API Security](https://api-security.owasp.org/editions/2023/en/0x11-t10)
- AWS Prescriptive Guidance says RLS is required to maintain tenant isolation in a pooled PostgreSQL model (shared tables) — [AWS Prescriptive Guidance, RLS recommendations](https://docs.aws.amazon.com/prescriptive-guidance/latest/saas-multitenant-managed-postgresql/rls.html)
- RLS pitfalls: table owners bypass policies unless FORCE ROW LEVEL SECURITY; superusers bypass; session-level tenant context can leak across pooled connections so use transaction-local settings; application-layer filters are easy to omit — [AWS Database Blog](https://aws.amazon.com/blogs/database/multi-tenant-data-isolation-with-postgresql-row-level-security); [Encore guide](https://encore.dev/articles/postgres-row-level-security-multi-tenant) (secondary)
- SOC 2 estimates (vendor/auditor sources, wide ranges): Type 1 about 3-8 months and roughly $5k-$60k depending on auditor; Type 2 adds a 3-12 month observation window, about $12k-$100k; common path is Type 1 to unblock a deal then Type 2; a claim that ~95% of enterprise buyers accept Type 2 versus ~60% of SMB buyers for Type 1 comes from a single vendor-affiliated source; scope (Trust Services Criteria, number of environments) is the main cost lever — [Drata](https://drata.com/learn/soc-2/type-1-vs-type-2); [Vanta](https://www.vanta.com/blog/soc-2-type-i-or-type-ii); [Scrut](https://www.scrut.io/soc-2/cost-of-soc-2-audit); [Rippling](https://www.rippling.com/blog/soc-2-for-startups)

### Inferences
- Must-have from day one (cheap): tenant_id on every row plus RLS; roles (employee, manager, admin, payroll) with object-level checks (API1/API5 are the most likely bugs in a punch/timesheet API where users guess IDs); server-side validation; rate limiting on login and punch endpoints (API4/API6: punch endpoints are "sensitive business flows"); audit log of admin actions; secrets management and encrypted storage; TLS; dependency scanning; MFA for admin/manager roles.
- Moderate effort, can wait for first business customers: SSO (SAML/OIDC) and SCIM provisioning; per-tenant data export; formal MFA for all; IP allowlist per tenant (note: IP allowlists are a weak control for mobile workers).
- Defer until sales demand: SOC 2 Type 2 itself; but adopting its controls early (access reviews, change management, logging, backups, incident process) makes the audit cheaper. Kronos 2021 incident shows availability/backup/recovery matter to payroll-critical software.
- Retrofitting multi-tenancy (adding tenant_id and isolation after launch) is among the most expensive changes: every table, query, index, cache key, and background job is affected, and a missed filter is a cross-tenant breach.

### Gaps
- No primary-source data on SSO/MFA adoption expectations by SMB time-tracking buyers.
- SOC 2 price/time figures are vendor-sourced; no neutral benchmark found.
- ASVS/OWASP Top 10 web (2021) mapping and session/cookie guidance not researched.

## 4. Reference architecture (data model, timezones, API, offline PWA, burst load)

### Takeaway
A punch-event ledger with derived shifts/timesheets and explicit adjustments is the sound core. Offline PWA is feasible but Background Sync is not Baseline, so design an app-level IndexedDB outbox with idempotency keys. Little published engineering literature specific to time-tracking was found; most content below is inference.

### Cited Findings
- Background Sync API: "limited availability", not Baseline; HTTPS and service-worker required; example queues work in IndexedDB and replays in the service worker's `sync` event; the page does not cover retry behavior or quotas — [MDN Background Synchronization API](https://developer.mozilla.org/en-US/docs/Web/API/Background_Synchronization_API) (MDN text does not itself name browsers; Chromium-only support is general knowledge, check MDN compat table)
- FLSA requires recording the time and day the workweek begins and daily/weekly hours, so workweek start (per tenant) is a required configuration field, not just a display option — [US DOL Fact Sheet 21](https://www.dol.gov/agencies/whd/fact-sheets/21-flsa-recordkeeping)

### Inferences (general engineering practice; no source fetched)
- Data model: `tenants`, `users`, `locations/sites` (with timezone IANA name), `punch_events` (immutable: id, user, type in/out/break_start/break_end, server_received_at UTC, client_claimed_at + client offset, source = web/kiosk/mobile/manual, geo + accuracy, ip, device id, idempotency_key), `shifts` (derived), `timesheets` (period, status: open/submitted/approved/locked), `adjustments` (reference original event or shift, delta, reason, actor, approver, timestamps), `audit_log` (append-only, all admin/config/permission changes), `policy_config` (rounding, OT rules, workweek start, retention).
- Timezones: store instants in UTC plus the site's IANA zone (not offset) at the time of punch; compute day/week boundaries in the site zone using the tenant's workweek start; handle DST (23/25-hour days, overnight shifts spanning midnight) in derivation, not storage. Retrofitting from "local time stored as naive timestamps" is very costly and ambiguous around DST changes.
- Rules engine (rounding, OT, breaks) should be derived from immutable events, versioned by effective date, so past pay periods can be recomputed or locked. Locking approved timesheets prevents silent changes.
- Offline: queue punches in IndexedDB with client UUID idempotency keys and client-claimed time; server records its own receipt time and flags large skew; fall back from Background Sync to on-open/online-event flush since support is partial; show "pending" state to avoid double punches.
- Burst load at shift start: punch endpoint should be a tiny, idempotent insert (no synchronous derivation); push derivation to async workers/queue; avoid per-punch heavy reads; client-side jitter on retries and exponential backoff; cache config per tenant; connection pooling (note RLS transaction-scoped setting compatible with transaction pooling). 9:00 spikes are bounded per tenant (a few thousand writes in a minute is within a single Postgres instance's range); a managed Postgres with a queue is likely sufficient for early scale, so premature microservices are not needed.
- API design: versioned REST; idempotency keys on write; cursor pagination; bulk export endpoints for payroll; webhooks later.

### Gaps
- No open-source codebase or engineering blog on time-tracking architecture was reviewed (e.g., Kimai, Solidtime, OrangeHRM); recommended follow-up.
- No load numbers from real vendors; burst sizing above is unsourced reasoning.
- Safari/iOS PWA offline and persistent-storage limits not researched.

## 5. Cheap early vs costly to retrofit (synthesis)

### Takeaway
Items that define the data shape (immutable events, UTC + IANA zones, tenant_id/RLS, audit log, retention fields, consent records) are cheap before launch and very costly afterwards; features layered on top (SSO, SOC 2 audit, geofence anomalies, hash-chaining) can wait.

### Cited Findings
- Retention minimums differ by regime (FLSA 2-3 years) — [US DOL Fact Sheet 21](https://www.dol.gov/agencies/whd/fact-sheets/21-flsa-recordkeeping); EU requires objective, reliable daily time records — [CJEU press release](https://curia.europa.eu/jcms/upload/docs/application/pdf/2019-05/cp190061en.pdf); biometrics litigation exposure is real — [Littler via earlier report](https://www.littler.com/news-analysis/asap/damage-control-illinois-enacts-amendment-states-high-risk-biometric-information)

### Inferences (prioritization proposal)
- Cheap early / very costly to retrofit (MUST at MVP): immutable punch ledger + adjustments with reason/approver; server-side UTC timestamps + IANA zones + per-tenant workweek start; tenant_id everywhere + RLS + object-level authz tests; append-only audit log; idempotent punch API with offline outbox keys; configurable retention + deletion/export primitives; consent/notice records; data minimization defaults (location only at punch, off by default per tenant); timesheet lock states.
- Cheap early, easy later (SHOULD at MVP): rate limiting, MFA for managers/admins, anomaly flags (duplicate device, impossible travel, IP/geo mismatch, clock skew), encrypted backups with restore tests, basic DPIA/privacy notice templates.
- Defer (build when sales demand): SSO/SCIM, SOC 2 Type 2 audit, hash-chained logs, biometrics (high legal cost; avoid until needed), per-tenant IP allowlists, multi-region data residency.
- Risk of omission: wrongly computed or untraceable pay (FLSA records, wage claims), cross-tenant data leak, regulatory action on location/biometrics, lost enterprise deals without SOC 2. Risk of retrofit: unrecoverable history, DST-ambiguous data, multi-table tenant migration.

### Gaps
- Effort estimates are qualitative (relative cost), not sourced from measured projects.
- No source quantifying retrofit cost; ranking is reasoned from the nature of the changes.
