# Core Time and Attendance (Clock In / Clock Out) Features in Leading Commercial Systems

Note: Vendor pages were fetched via summarizing tool; "page does not mention X" means absent from that page only, not that the product lacks it. Pricing/tier gating changes often. Many roundups are vendor-affiliated.

## 1. What clock in/out methods exist?

### Takeaway
Web, mobile app and shared tablet/kiosk (often PIN-based) are table-stakes across all vendors. Biometric (face, fingerprint), badge/NFC/QR, hardware terminals and chat-based (Slack/Teams) punches are differentiators, concentrated in enterprise WFM (UKG, ADP, Paycor, BambooHR hardware) and in Jibble (broadest low-cost set).

### Cited Findings
- Homebase: mobile phone clock-in (free Basic plan), shared tablet/computer/POS as time clock, unique employee PIN, kiosk and offline modes. — [Homebase](https://www.joinhomebase.com/time-clock)
- Deputy: facial recognition on Kiosk app (iPad) and Time Clock app (Android tablet) with voice commands for touchless punches; GPS location stamp on mobile app; works on computers, phones, tablets. — [Deputy](https://www.deputy.com/features/time-clock)
- Deputy can turn an existing POS (Clover, Square, Epos Now) into a time clock (POS integration needs Core plan, $6.50+/user/mo per Buddy Punch roundup). — [Buddy Punch roundup](https://buddypunch.com/?p=29258)
- Jibble: web, iOS/Android, desktop (Windows/Mac), Chrome extension, shared-tablet kiosk, Slack and Teams commands, QR code, NFC card/tag, manual entries; free plan has unlimited users and includes time tracking, GPS, facial recognition. — [Jibble features](https://www.jibble.io/features)
- Jibble Slack/Teams: clock in/out via slash commands/bot; face recognition not described as applying to chat punches (vendor-stated, unverified). — [Jibble search results](https://www.jibble.io/time-and-attendance)
- Clockify Kiosk: PIN, QR code, or photo at a shared device; break tracking in kiosk. — [Clockify features](http://clockify.me/features/)
- When I Work: iOS, Android, web; phone, tablet, laptop, POS, or terminal. — [When I Work](https://wheniwork.com/features/time-clock)
- Rippling: mobile app, web, or physical time tracking device. — [Rippling](https://www.rippling.com/time-and-attendance)
- TimeClock Plus: on-site, mobile, or web clocks, can mix devices per location; biometric/badge/IVR not mentioned on that page. — [TCP](https://tcpsoftware.com/products/timeclock-plus/)
- ADP: ADP Kiosk (mounted, works offline, facial recognition, optional fingerprint scanner); Time Kiosk app on iPad/Android tablet (facial recognition, fingerprint login, optional voice commands on iOS); mobile clocking; real-time transfers between departments/locations/jobs. Badge and geofence punches not named on page. — [ADP](https://www.adp.com/what-we-offer/time-and-attendance/employee-time-clocks.aspx)
- Paycor: timeclocks with Wi-Fi, PIN/badge entry, multi-language readers; mobile clock adds tablet use, facial scan, offline capture; no fingerprint-specific clock confirmed (secondary summary). — [Paycor search results](https://www.paycor.com/solutions/time-attendance-tracking)
- BambooHR: physical Time Clock supports ID code, facial recognition, badge scan, fingerprint scan; Time Kiosk tablet app (iOS) with ID code and photo capture; geolocation mobile only. — [BambooHR](https://www.bamboohr.com/platform/time-and-attendance/)
- UKG: InTouch DX is the current time clock hardware, compatible with UKG Dimensions; UKG publishes a finger/face scan data statement for InTouch devices. Product now branded UKG Pro Workforce Management (formerly Dimensions); a state agency reports Workforce Central shuts down Dec 31, 2025. — [UKG device statement](https://www.ukg.com/sites/default/files/2023-10/UKG-Device-Finger-and-Face-Scan-Data-Statement.pdf); [UKG Pro WFM](https://www.ukg.com/solutions/ukg-dimensions-suite/time-and-attendance)
- Connecteam: kiosk app on shared tablet with optional selfie; vendor page cites code, biometric, iPad/Android login; NFC clock-in referenced in release notes. — [Connecteam](https://connecteam.com/time-clock-app/)
- Timeero: mobile app, Face ID on iOS only. QuickBooks Time: computer, tablet kiosk, mobile. — [Buddy Punch roundup](https://buddypunch.com/?p=29258)
- Truein and Oloid (ADP marketplace add-on for ADP Workforce Now Essential Time) sell face-recognition clocks layered on payroll/HR suites. — [Oloid on ADP Marketplace](https://apps.adp.com/en-us/apps/330441/oloid-verify-time-clock-for-adp-workforce-now-essential-time/features)

### Inferences
- Table-stakes: web + mobile + shared tablet/kiosk with PIN. Differentiators: face recognition on kiosk (now common in SMB too: Deputy, Jibble, Connecteam), fingerprint/badge hardware (UKG, ADP, Paycor, BambooHR), QR/NFC and Slack/Teams punches (Jibble most explicit).
- Free tiers (Homebase Basic, Jibble Free) push basic clocking toward commodity.

### Gaps
- Specific UKG InTouch DX modalities (RFID, fingerprint, palm) not verified from a primary page. Kronos/UKG, Paycor, TCP, When I Work, Rippling pages gave limited detail. Fetch of Connecteam time clock page failed (returned image).
- Not researched: Clockshark specifics.

## 2. Anti-fraud features

### Takeaway
GPS stamps and geofencing, photo/selfie at punch, and unique PINs are near table-stakes at SMB tier (often gated to mid plans). Face recognition with liveness/anti-spoofing, IP/location restriction, early-punch prevention and manager-approval of out-of-fence punches are differentiators.

### Cited Findings
- Homebase: unique PINs; alert when someone clocks in before coworkers; photo capture at clock in/out on shared devices (Essentials+, not personal phones); geofenced mobile clock-in (Essentials); early clock-in prevention and auto clock-out (Plus+). No IP control mentioned. — [Homebase](https://www.joinhomebase.com/time-clock)
- Deputy: facial verification on tablets aimed at buddy punching; GPS stamps; geofencing; automatic attestation requests when late, unscheduled, missed break or early leave; Deputy's page does not state whether geofence + face can be combined on one device. — [Deputy](https://www.deputy.com/features/time-clock)
- Jibble: face recognition, selfie with each punch, GPS stamp, geofencing blocking out-of-zone clock-ins, auto clock in/out on zone entry/exit, live location; anti-spoofing checks static and dynamic 2D images (vendor-stated); "Speed Mode" auto clock-in at device. Capterra reviews note privacy concerns. — [Jibble features](https://www.jibble.io/features); [Jibble summary](https://www.jibble.io/face-recognition-attendance-system)
- Connecteam: geofence radius 500-2,500 ft; outside-fence punches trigger manager approval request; optional selfie; auto clock-out on leaving geofence or shift end; auto clock-out reportedly Advanced plan (single competitor source). — [Connecteam time theft](https://connecteam.com/employee-time-clock-app/time-theft/); [Clockify review of Connecteam](https://clockify.me/reviews/connecteam-review)
- ADP: facial recognition with liveness detection ("scans multiple angles"), restriction to certain areas or IP addresses. — [ADP](https://www.adp.com/what-we-offer/time-and-attendance/employee-time-clocks.aspx)
- When I Work: GPS capture, geofencing preventing early/off-site punches, photo clock-ins; IP/device restrictions not mentioned. — [When I Work](https://wheniwork.com/features/time-clock)
- Buddy Punch: GPS, geofences blocking offsite punches, selfie on punch; IP locking per roundup. OnTheClock layers GPS with IP, Wi-Fi, device, computer restrictions. — [Buddy Punch roundup](https://buddypunch.com/?p=29258); [TechRepublic roundup](https://www.techrepublic.com/de/article/best-time-and-attendance-software/)
- BambooHR geolocation: mobile only, all-or-nothing toggle, location saved only at clock in/out, admins see map, address may be imprecise when signal weak. — [BambooHR geolocation](https://www.bamboohr.com/product-updates/geolocation-for-time-tracking)
- Clockify: GPS location tracking, route review and privacy controls; geofencing, face recognition not mentioned on features page. — [Clockify](http://clockify.me/features/)
- QuickBooks Time: GPS geofences auto clock in/out or prompt; no photo/face. Insightful/Time Champ add screenshots, idle detection. — [Buddy Punch roundup](https://buddypunch.com/?p=29258)
- Legal constraint: Illinois BIPA and Texas require notice/written consent, retention schedules for biometric clocks; sources say a fallback punch method is advisable. UKG states it does not concede scans are biometric data. — [Paycor search](https://www.paycor.com/resource-center/articles/6-considerations-for-employee-attendance-policies); [UKG statement](https://www.ukg.com/sites/default/files/2023-10/UKG-Device-Finger-and-Face-Scan-Data-Statement.pdf)

### Inferences
- Liveness/anti-spoofing is explicitly claimed by ADP and Jibble; others say "face recognition" without detail. Selfie capture (record for review) differs from true biometric matching.
- Biometric legal risk is a buying consideration favoring PIN/badge fallbacks.

### Gaps
- No independent testing of liveness efficacy. Kronos/UKG anti-fraud specifics (e.g., palm/finger templates) not confirmed. Clockshark not covered.

## 3. Timesheets, breaks, overtime, rounding, pay rules, job tracking, edits/audit, offline

### Takeaway
Auto timesheets, break tracking, overtime calc, approvals and payroll export are table-stakes. Configurable rounding, multi-jurisdiction pay/OT rules, break compliance/premium pay, job costing and immutable audit trails are where enterprise suites (UKG, ADP, Paycor) differentiate; offline mode is common on kiosks/mobile but unevenly documented.

### Cited Findings
- Deputy: timesheet auto-created at shift end with hours and breaks for approval; one-click payroll export; kiosk offline up to 24 hours including face verification and breaks; alerts managers to missed breaks; rounding and audit log not on page. — [Deputy](https://www.deputy.com/features/time-clock)
- Homebase: tracks required meal/rest breaks (paid, unpaid, missed, waived), auto-calculates missed break pay; OT approach alerts (Plus+); managers fix punches from phone/computer; flags missed punches; rounding not mentioned. — [Homebase](https://www.joinhomebase.com/time-clock)
- Jibble: time rounding; OT rules with thresholds/multipliers; regular/OT/holiday hour split; paid/unpaid break logging; projects by client, activities; multi-level timesheet approvals; role permissions; offline sync; no dedicated audit log mentioned. — [Jibble features](https://www.jibble.io/features)
- Clockify: weekly/monthly timesheet approvals with reminders and history; projects/tasks, multiple hourly rate levels, budgets/estimates; auto tracker; rounding, OT, audit log, offline not on that page. — [Clockify](http://clockify.me/features/)
- BambooHR: standard OT rules (US federal/state, Canadian provinces, by employee location) or custom daily 1.5x, daily double 2x, weekly 1.5x, scoped to Time Tracking groups; changing rules recalculates open timesheets; shift differentials; project tracking; approval workflows. — [BambooHR OT rules](https://www.bamboohr.com/product-updates/custom-overtime-rules-and-overtime-rules-for-canada)
- Rippling: prebuilt labor policies (OT, breaks) applied by work location with risk flags; OT and break alerts; job costing by job/task/project; says systems include audit trails. — [Rippling](https://www.rippling.com/time-and-attendance)
- When I Work: auto break tracking, OT alerts tied to scheduled shifts, multiple pay rates. — [When I Work](https://wheniwork.com/features/time-clock)
- Paycor: 10+ overtime policies; rounding option in settings (legacy help doc); claims comprehensive audit trails (fields undocumented in sources found). — [Paycor rounding help](https://legacycare.paycor.com/en_US/23018101451539-Account-Configuration/360021199234-Payroll-Time-Clock-Rounding); [Paycor](https://www.paycor.com/solutions/time-attendance-tracking)
- UKG: pay rules define how time segments are marked and what counts as OT; rounding configurable, often differing by department/schedule class; universities apply rules like 7-minute rounding on punches. — [UKG developer docs](https://developer.ukg.com/wfm/docs/timekeeping-overview-doc); [Surety Systems](https://www.suretysystems.com/insights/how-can-ukg-timekeeping-solutions-help-you/)
- TimeClock Plus: complex pay rules, compliance policies, job codes, OT; rounding and audit not on page. — [TCP](https://tcpsoftware.com/products/timeclock-plus/)
- Rounding legality: neutral over time; common 5/6/15-minute increments; DCAA expects audit trail of timesheet changes including approvals and rounding. — [Paycor search](https://www.paycor.com/resource-center/articles/6-considerations-for-employee-attendance-policies)
- ADP kiosk works offline; Paycor mobile clock offline capture; Jibble offline sync; Homebase offline mode. (see Section 1 links)

### Inferences
- Rounding appears rarely on SMB feature pages (absent from Deputy, Homebase, When I Work pages) but is standard in enterprise suites and Jibble.
- Edit audit trails are asserted by enterprise vendors but rarely detailed publicly; verify in demos.

### Gaps
- Pay-rule engine depth (e.g., California meal premium, union rules, differentials, accruals) not verified for UKG/ADP from primary docs. Auto-deduct breaks, grace periods, and punch-edit history fields not verified. Clockshark, TCP biometric options not found.

## 4. Table-stakes vs differentiators (synthesis)

### Takeaway
Table-stakes: web/mobile/kiosk clock, PIN, GPS stamp, geofence (mid-tier), break tracking, auto timesheet, OT calc/alerts, approvals, edits by managers, payroll export. Differentiators: biometric w/ liveness, hardware terminals (badge/fingerprint), QR/NFC, Slack/Teams punch, auto zone clock in/out, attestation prompts, early-punch blocking, configurable rounding/pay rules by jurisdiction, job costing, DCAA-grade audit trails, long offline windows.

### Cited Findings
- Roundups list geofencing and biometric verification as standard; roundups are often vendor-affiliated. — [TechRepublic](https://www.techrepublic.com/de/article/best-time-and-attendance-software/)
- Jibble free plan includes GPS and facial recognition (unlimited users), Homebase free includes phone and PIN; paid features: Homebase photo/geofence from Essentials, auto clock-out from Plus. — [Jibble](https://www.jibble.io/features); [Homebase](https://www.joinhomebase.com/time-clock)

### Inferences
- Because Jibble gives face + GPS free, these are being commoditized; enterprise differentiation shifts to compliance rules engines, hardware, and integrations.

### Gaps
- Clockshark, Kronos Workforce Central legacy, Gartner Peer Insights/G2 comparisons not fetched.
