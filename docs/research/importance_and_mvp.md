# Feature importance, prioritization frameworks, MVP scope and roadmap for a time and attendance (clock in/out) product

Scope note: builds on /Users/sagit/Desktop/shift-tracking-system/reports/Time and attendance system features.md (feature landscape, differentiators, BIPA/GDPR, pricing). Not repeated here. Evidence quality caveat: ~10 tool calls; no rigorous independent buyer survey was found; most sources are vendor pages, vendor-authored comparisons, or review-site aggregates. Reddit threads were not retrievable via search.

## Which features drive purchase/retention (reviews, surveys, buyer guides) and what are the top complaints?

### Takeaway
No formal survey with sample sizes was found; review-site aggregates consistently point to clock in/out, timesheets, automated overtime, payroll integration/export, mobile access, scheduling and reporting as the most valued. Complaints cluster on mobile/clock reliability (freezing, GPS errors, no offline), correction friction, and feature-gating/missing features.

### Cited Findings
- G2 says automated overtime calculation and seamless payroll integration stand out as the most valued features (category-trend based, not a survey) — [G2 category / search summary](https://www.g2.com/categories/time-attendance)
- Capterra lists clock in/out, timesheet management, payroll management, reporting/analytics, mobile access and employee scheduling among features reviewers value most for attendance tracking — [Capterra attendance buyer's guide](https://www.capterra.com/attendance-tracking-software/buyers-guide/)
- SelectHub's checklist centers on automated clock in/out, absence and leave management, and integrations; other vendor lists add cloud access, simple UX, real-time tracking, employee self-service — [SelectHub](https://www.selecthub.com/time-and-attendance/time-and-attendance-system-features/); [Netchex](https://netchex.com/blog/12-essential-time-and-attendance-software-features/)
- Asure (vendor) argues buyers wrongly choose by feature count rather than workforce fit and should start from payroll edge cases they cannot tolerate (so rules/pay-calculation correctness is the real purchase driver) — [Asure](https://www.asuresoftware.com/why-most-time-and-attendance-buying-decisions-start-in-the-wrong-place/)
- G2 aggregates: Connecteam ~4.6/5 with 3,505 reviews; Deputy ~4.6/5 with 665 reviews. Deputy's top cons are scheduling issues (48 mentions) and shift management (41); Connecteam's top cons are "missing features" (442) and "limited features" (424). Counts may come from different snapshots — [G2 compare Connecteam vs Deputy](https://www.g2.com/compare/connecteam-vs-deputy)
- Connecteam Capterra reviewers: time clock "freezes", "forgets" clock state; admin tricky between scheduler and time clock; geofencing praised for stopping early clock-ins — [Capterra Connecteam reviews](https://www.capterra.com/p/153140/Connecteam/reviews/?page=9)
- Deputy: reviewer (on a competitor's site, so biased) said the clock is labelled "start shift" (hard to find) and cannot clock in without internet — [Connecteam review of Deputy](https://connecteam.com/reviews/deputy/)
- Buddy Punch complaints: mobile app sometimes fails to record accurately, frequent photo requirements make punching cumbersome, GPS errors recurring, time-entry corrections require manager intervention, no native offline for primary app, privacy worries about photos/location. Largely positive overall; ease of use for small teams praised. Sources are a competitor page and an aggregator — [Jibble on Buddy Punch](https://www.jibble.io/reviews/buddy-punch/5-ways-buddy-punch-sucks); [CheckThat.ai](https://checkthat.ai/brands/buddy-punch/reviews)
- Homebase time clock: Capterra reviewers mention occasional glitches and device issues — [Capterra Homebase vs Deputy](https://www.capterra.com/compare/153076-167811/Homebase-vs-Deputy)

### Inferences
- Retention is driven by "it records time correctly every time" (reliability, offline sync, easy corrections) plus accurate overtime/pay rules and payroll export; purchase is driven by ease of use and price.
- Complaint themes map to MVP quality bars: a reliable clock and correction workflow matter more than feature count. "Missing features" as Connecteam's top con shows that breadth gating also hurts, but it is a scale-stage problem.

### Gaps
- No independent survey (Gartner, Nucleus, SHRM) with quantified feature importance; SHRM page surfaced only as a white paper title.
- Reddit sentiment not retrieved. Capterra/G2 pages returned 403 on direct fetch, so only snippets via search.

## What do Clockify, Toggl, Homebase, Jibble, Deputy, Connecteam, Kimai include free vs paid (MVP vs later proxy)?

### Takeaway
Free tiers deliver core capture (clock/timer, timesheets, basic reports); paid tiers add location/photo verification, approvals, overtime/break rules, retention, billing, integrations and security. Gating differs: Jibble gates by feature scale not users; Homebase/Connecteam cap at 10 users; Clockify/Toggl cap at ~5 or limit reporting.

### Cited Findings
- Homebase: Basic $0, up to 10 employees, 1 location: basic time tracking (clock in on tablet, computer or POS; timesheets stored 90 days). Essentials $30/location/mo adds location-based mobile clock-in, photo capture, custom overtime/break settings, clock reminders, 4+ years of timesheet storage. Plus $70, All-in-One $120. Labor-cost controls (OT alerts, early-clock-in blocking, auto clock-out) tier not confirmed on the page; earlier report says Plus — [Homebase pricing](https://www.joinhomebase.com/pricing)
- Jibble: free forever for unlimited users incl. clock in/out, offline mode, reminders, GPS and face recognition with limits (reported 2 geofences). Premium adds overtime, multiple work groups, 3 kiosks, 8 geofences (own page), leave tracking; Ultimate adds projects/clients, API, SSO, 2FA, data residency; Enterprise custom. Prices conflict ($1.99/$3.99 per user on Jibble's page vs $2.49-$5.99/$4.99-$10.99 on third parties) — [Jibble plans](https://www.jibble.io/upgrade-plans); [Capterra Jibble](https://www.capterra.com/p/156992/Jibble/pricing/)
- Clockify: Free plan (pricing page says up to 5 users; other reviews say unlimited users — conflict) with 1-month report date range; invoicing and timesheet approval start at Standard (~$5.49 annual); budgets, labor cost/profit, screenshots at Pro; 5 plans total — [Clockify paid features](https://clockify.me/learn/resources/paid-features-explained/); [Capterra Clockify](https://capterra.com/p/169607/Clockify/pricing/)
- Toggl Track: free for up to 5 users (you plus 4), unlimited tracking/projects/clients/tags, idle detection, 100+ integrations, API 30 requests/hour; new free users limited report views — [Toggl support](https://support.toggl.com/payment-plans-at-toggl-track); [Toggl community](https://community.toggl.com/t/30-report-views-limit-for-new-free-users/4621)
- Deputy: no permanent free plan, free trial; $5 Lite / $6.50 Core / $9 Pro per user/mo, $30 minimum; biometric verification supported — [Capterra Homebase vs Deputy](https://www.capterra.com/compare/153076-167811/Homebase-vs-Deputy)
- Connecteam: free for up to 10 users with time clock and scheduling (limits of one time clock and one job schedule); paid hub pricing from $29/mo for up to 30 users; paid Basic adds GPS time clock and kiosk — [Connecteam vs Homebase (Homebase page, vendor)](https://www.joinhomebase.com/comparison/connecteam)
- Kimai: open source (AGPL-3.0), self-hostable free; plugins add invoicing, approval workflows, expenses, some paid; cloud plans reported ~EUR 2.99-3.99/user/mo (unverified) — [Kimai](https://www.kimai.org/en/open-source-time-tracker); [MakeTechEasier](https://maketecheasier.com/self-hosted-kimai-best-time-tracker)

### Inferences
- Free-tier proxy for MVP: clock in/out (web/tablet/PIN or timer), timesheet list, basic report/export, limited history. Proxy for v1.x: GPS/geofence, photo, approvals, OT/break rules, long retention, kiosk. Proxy for later: billing/invoicing, integrations/API, SSO, audit, projects/job costing, analytics.
- Competing on price at the low end means the MVP must be genuinely free-tier-competitive; Jibble includes GPS+face in free, raising the floor.

### Gaps
- Official pages for Deputy/Connecteam/Clockify/Toggl not fetched directly; prices vary by source and billing period. Kimai feature list from vendor only.

## Which prioritization frameworks fit, and what would a Kano classification of T&A features look like?

### Takeaway
Use Kano to classify (must-be / performance / delighter) from segment surveys, MoSCoW to cut MVP scope, RICE or impact/effort to rank within tiers. The Kano classification below is my own inference from the evidence, not a published T&A Kano study (none found).

### Cited Findings
- MoSCoW classifies (no score/ranking within a category); good for fast scoping in about an hour — [Plane](https://plane.so/blog/feature-prioritization-frameworks-rice-moscow-and-kano-explained); [People in Product](https://medium.com/people-in-product/beyond-the-backlog-understanding-prioritization-with-moscow-kano-and-rice-frameworks-1c147fe520fc)
- RICE = (Reach x Impact x Confidence) / Effort; suited to large backlogs (30+ items); inputs are estimates, so imprecise — [Plane](https://plane.so/blog/feature-prioritization-frameworks-rice-moscow-and-kano-explained); [LogRocket](https://blog.logrocket.com/product-management/product-feature-prioritization-frameworks-strategies)
- Kano classifies by satisfaction effect via paired functional/dysfunctional survey questions; it does not rank; common combos are MoSCoW to filter then RICE to rank, or Kano to classify then RICE within type — [Lovable guide](https://lovable.dev/guides/rice-moscow-kano); [People in Product](https://medium.com/people-in-product/beyond-the-backlog-understanding-prioritization-with-moscow-kano-and-rice-frameworks-1c147fe520fc)

### Inferences (proposed Kano classification; validate with a survey of ~15-30 users per segment)
- Must-be (absent = dissatisfied, present = neutral): clock in/out, accurate timestamps, timesheets, manager edit/approve with audit trail, CSV/payroll export, overtime and break calculation, reliable offline capture, data security/access control. Evidence: universal in free tiers; reliability and correction friction are top complaints.
- Performance (more is better): mobile/kiosk/PIN ease and speed, GPS/geofence accuracy, scheduling and shift swaps, leave/PTO, reports, payroll/accounting integrations, alerts (missed punch, OT).
- Delighters: face verification with liveness, auto clock-in on geofence entry, Slack/Teams punching, anomaly/compliance warnings, predictive labor-cost view, jurisdiction-aware rules engine, employee-facing earnings estimate.
- Indifferent for many: screenshots/activity monitoring for hourly staff.
- Reverse (can reduce satisfaction): frequent photo prompts, always-on GPS tracking, forced biometrics (privacy complaints; legal exposure noted in prior report).
- Suggested scoring for RICE inside tiers: Reach = share of segment affected; Effort in person-weeks; offline sync, rules engine and integrations are the high-effort items (see roadmap below).

### Gaps
- No published Kano study for T&A software. Classification is hypothesis.

## Typical MVP scope and phased roadmaps for a time-tracking/attendance app

### Takeaway
Dev-agency write-ups converge on: MVP = capture + timesheets with approval + basic reporting + sync; then integrations/billing/mobile/GPS; then analytics/compliance/enterprise. Typical estimates are 3-5 months and roughly $25K-$45K for MVP. Caveat: the main source is project/billable time tracking rather than hourly attendance, so offline, kiosk, and labor-rule features are underweighted there.

### Cited Findings
- 8ration (dev agency): Phase 1 MVP = time capture (manual timers, idle detection), timesheets with manager approval/editing, reporting by project/client/task, cross-device sync. Phase 2 = integrations (Jira, GitHub, QuickBooks, payroll, calendar), billing/invoicing, activity context (screenshots, GPS for field), native mobile. Phase 3 = analytics, role-based access, audit logs, GDPR controls, ERP. Timeline: discovery 2-4 wks, core build 8-12 wks, integrations/billing/mobile 4-8 wks, testing 2-4 wks; MVP 3-5 months, full 6-9 months. Cost: lean MVP $25K-$45K, full $60K-$150K, enterprise $150K+. Data migration flagged as schedule risk; cost drivers: integrations, native vs cross-platform, compliance — [8ration](https://www.8ration.com/blogs/time-tracking-software-development/)
- 13labs: essentials are start/stop timer, project selection, basic reports; invoicing, team tracking, detailed reports, integrations later — [13labs](https://www.13labs.au/build/time-tracking)
- Open-source example roadmaps put core timer/projects/dashboard in P0 and goals, analytics, mobile in P1 — [dwyl MVP](https://www.github.com/dwyl/mvp/blob/main/README.md)
- Vendor field guidance: offline clock-in that syncs later is a core field need (ClockShark, Factorial); GPS not federally required for timekeeping (Everhour) — [ClockShark](https://www.clockshark.com/services/time-tracking); [Everhour](https://everhour.com/time-tracking/time-tracking-app-for-field-services)

### Inferences (proposed attendance-specific phasing, built from the above plus prior report)
- MVP (Must): web/PWA clock in/out with optional shared-kiosk PIN, break start/stop, timesheet view, manager edit with reason and audit log, approve/lock period, OT calculation (single configurable rule), CSV export, roles (employee/manager/admin), offline queue with idempotent sync, basic daily/weekly hours report.
- v1.x (Should): GPS stamp and geofence warning, photo selfie (non-biometric), missed-punch and OT alerts, scheduling basics and shift swaps, PTO requests, payroll integrations (one or two), rounding settings.
- v2 (Could): face verification with consent/fallback, multi-jurisdiction rules, job/project costing, Slack/Teams, API/webhooks, SSO, analytics, anomaly detection.
- Because the 8ration estimate is for project time tracking and from a vendor, treat cost and timeline as order-of-magnitude.

### Gaps
- No attendance-specific (hourly/kiosk) MVP case study found; no independent cost data.

## How do needs differ by segment (small business, shift/hourly, field/mobile, office/hybrid, contractors)?

### Takeaway
Evidence by segment is thin and vendor-sourced. Patterns: shift/hourly need kiosk/PIN, scheduling, OT/break compliance and payroll; field need offline, GPS and job tagging; office/hybrid need lightweight timers/attendance and leave; contractors need project/client tagging and invoicing; small business weights price, setup simplicity and free tier.

### Cited Findings
- Field/contractor: offline clock-in with later sync is core; GPS/geofence automate clock in/out and curb time theft; useful location data is tied to jobs rather than punches; job-based tracking links hours to sites — [ClockShark](https://www.clockshark.com/tour/gps-time-clock); [Factorial](https://factorialhr.com/blog/best-field-service-time-tracking/); [Housecall Pro](https://www.housecallpro.com/resources/time-clock-app/)
- Small hourly teams (restaurants/retail): Homebase's free tier bundles scheduling, time tracking, team chat and POS integration for one location and up to 10 employees — [Homebase pricing](https://www.joinhomebase.com/pricing); Deputy's complaints centre on scheduling/shift management — [G2](https://www.g2.com/compare/connecteam-vs-deputy)
- Project/billable time tools (Clockify, Toggl, Kimai) gate invoicing, budgets, approvals and billable rates in paid plans — [Clockify](https://clockify.me/learn/resources/paid-features-explained/); [Kimai](https://www.kimai.org/en/open-source-time-tracker)
- Everhour: federal rules do not require GPS for timekeeping, so GPS is a business-control choice — [Everhour](https://everhour.com/time-tracking/time-tracking-app-for-field-services)

### Inferences (proposed segment matrix; hypotheses)
- Small business: Must = fast setup, free/cheap, payroll export. Shift/hourly: Must = kiosk/PIN, break/OT rules, scheduling/swaps, offline. Field/mobile: Must = offline, GPS stamp, job/site tag. Office/hybrid: Must = simple timer/clock, leave, reports; low interest in kiosks and geofencing; surveillance features may be reverse-Kano. Contractors: Must = project/client tagging, reports/invoicing; no OT/scheduling.
- An MVP aimed at one beachhead (e.g., small hourly shift teams, or field crews) is more defensible than serving all; the above matrix suggests shift/hourly and field share the clock/timesheet/offline/OT core.

### Gaps
- Search found no source directly comparing office vs shift vs field needs; no segment-level survey data; office/hybrid evidence essentially absent.
