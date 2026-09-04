# DPDP Act, 2023 — what the platform does, and what it still needs

India's Digital Personal Data Protection Act, 2023 applies to samaajconnect:
it processes personal data of people in India, so each Samaaj is a **Data
Fiduciary** and each member a **Data Principal**.

> **This document is an engineering mapping, not legal advice.** It records
> which obligations the software can satisfy, how, and where it stops. The
> judgement calls — retention periods, the wording of a notice, whether a
> particular Samaaj is a Significant Data Fiduciary, how "verifiable" parental
> consent must be in practice — need someone qualified in Indian data
> protection law. Every one of those is flagged below as **needs counsel**.

## Why this platform is unusually exposed on one point

samaajconnect stores **children's data** by design: `ChildProfile` exists so a
family can record its children, and Jain Pathshala enrolls them. Section 9 of
the Act treats anyone under 18 as a child and requires **verifiable parental
consent** before processing their data, plus a bar on tracking, behavioural
monitoring and targeted advertising directed at children.

That is the single largest compliance surface here, and it is not optional or
deferrable — it attaches the moment the first child record is created.

## Obligation by obligation

| § | Obligation | Status | Where |
|---|---|---|---|
| 5 | Notice, itemised, at or before consent | **built** | `ConsentNotice`, shown on Register |
| 6 | Consent: free, specific, informed, unconditional, unambiguous | **built** | `ConsentRecord` per purpose |
| 6(4)–(6) | Withdrawal as easy as giving | **built** | `POST /v1/identity/me/consents/{purpose}/withdraw` for a member's own; `DELETE /v1/children/{id}/parental-consent` for a child's. The second was missing until 2026-09-04 — see "The withdrawal that could only be reached through erasure" below |
| 6(7) | Consent records retained and producible | **built** | `ConsentRecord` is append-only |
| 8(1) | Process only for the consented purpose | partial | Purpose is recorded; enforcement is by code review, not by a runtime check |
| 8(4) | Reasonable security safeguards | partial | See "Security" below |
| 8(6) | Breach notification to the Board and affected principals | **not built** | Every member of a Samaaj can now be addressed at once, but in-app only; the provider, the Board form, the text and the detection do not exist. See "Breach notification" below |
| 8(7) | Erase when consent is withdrawn or the purpose is served | **built** | `POST /v1/identity/me/erase`; see "Erasure vs. the audit log" |
| 9 | **Verifiable parental consent for under-18s** | partial | `ParentalConsent` on `ChildProfile`, required to create one; "verifiable" needs counsel |
| 9(3) | No tracking or behavioural monitoring of children | **built by absence** | The platform does none. Keep it that way. |
| 11 | Right to access a summary of data and processing | **built** | A `/me/data-export` in each of the three services |
| 12 | Right to correction and erasure | **built** | Correction is the profile edit; erasure is `POST /v1/identity/me/erase` |
| 13 | Right to grievance redressal | **built** | `GrievanceContact` per Samaaj, published on the public summary |
| 14 | Right to nominate | **not built** | No longer blocked on a channel; needs a nominee field, and counsel on what a nominee may do |

## What is built

### Notice and consent

`ConsentNotice` is a versioned document: an id, a version, the purposes it
covers, and the text. Registration will not proceed without consent to the
purposes marked required, and the **version consented to is recorded on the
record**, so it is always possible to say what someone was actually shown.

Purposes are separate records, not one flag, because §6 requires consent to be
*specific*. Bundling "run your membership" with "send you Samaaj news" into one
tick would make neither valid.

### Consent records are append-only

Granting and withdrawing both write rows; nothing is updated in place. §6(7)
requires a Fiduciary to be able to produce the consent it relied on, which is
impossible if the record is mutable.

### Parental consent for children

A `ChildProfile` cannot be constructed without a `ParentalConsent`: the factory
requires the attesting member, and the command validator refuses a request that
does not carry the attestation. Section 9 makes that consent the basis on which
the data may be held, so a record without it should not be creatable, not merely
discouraged.

The consent stores the notice version **and the attestation text verbatim**.
Keeping only a version number would mean reconstructing the wording from source
control to answer "what did they actually agree to?".

`GET /v1/children/data-notice` is what the parent is shown first. The notice
says plainly that the platform does not track children, monitor their behaviour
or advertise to them — which is section 9(3), and which the platform satisfies
by not doing any of those things. Keep it that way.

**Needs counsel:** whether a family head's recorded attestation is "verifiable"
parental consent for a community organisation whose members know each other in
person.

### Data export

Each service answers for what it holds:

| Endpoint | Covers |
|---|---|
| `GET /v1/identity/me/data-export` | Account, roles, consent history, processing purposes |
| `GET /v1/members/me/data-export` | Profile and privacy settings, family, children and their consents |
| `GET /v1/audit/me/data-export` | Notifications, and the actions this member took |

**Per-service is deliberate.** One service reaching synchronously into the
others would undo the service boundaries for a feature used a handful of times a
year. Each response names what it does *not* cover, so no single export can be
mistaken for the whole picture. This is a gap in convenience, not in rights.

Two things are deliberately absent. The identity export omits the password hash:
a credential is data about a person only in the sense that a lock is about a
key. The audit export omits the payload of each row and covers only actions
where the member is the *actor* — an audit log is largely a record of
administrators' work, and handing someone else's actions to a member on request
would turn a transparency right into a surveillance tool.

### Grievance redressal

`PUT /v1/identity/tenants/{id}/grievance-contact` names the person, and the
contact appears on the Samaaj's public summary — published, as section 13
requires, rather than visible only to members. It is kept separate from the
Samaaj's general contact: conflating the two would make it impossible to tell
whether a Samaaj has actually named one. A name with no email or phone is
refused, because that is not a means of redressal.

**And until 2026-09-04 the only way to name anybody was curl.** This section
described the endpoint, the table above marked the obligation **built**, the
admin panel's API client had the method written — and no screen called it. The
obligation was built in the sense that the platform could store an answer, and
unmet in the sense that no Samaaj could give one. `scripts/unreachable-endpoints.sh`
reported nothing, because it finds callers by looking for `/v1/` literals in app
code and the literal was sitting in the API client itself; the dead end had moved
one layer up. `scripts/uncalled-api-methods.sh` is the sweep that sees that layer.

The Samaaj screen now carries a **Grievance contact** control per Samaaj, and a
Samaaj that has named nobody says "Not named" on its row without anything being
opened — the count of Samaaj that have not met section 13 is the number a
platform operator needs.

## The withdrawal that could only be reached through erasure

Section 6(4) requires that withdrawing a consent be about as easy as giving it.
For a member's own consents the platform has always met that: giving was a tick
during registration, withdrawing is one call and the member portal deliberately
puts no confirmation in front of it.

**For a child's, it did not, and the gap lasted until 2026-09-04.** A child's
record exists on a parent's consent under section 9. Giving that consent was one
tick beside the child notice on the family screen. Withdrawing it had no
endpoint at all — the only route was `POST /v1/identity/me/erase`, which is
section 12 and takes the parent's own account, their household membership, their
timeline posts and everything else with it.

That is not a shortfall in ease. It is the right made conditional on
surrendering unrelated ones, which is closer to the "unconditional" wording in
section 6 than to a usability problem. Three separate files described the
consent as the basis for the record and none of them noticed that nothing could
remove it.

`DELETE /v1/children/{id}/parental-consent` closes it. What the design turns on:

- **Only the person who gave the consent may withdraw it.** Not the household
  head, not another parent, not a Samaaj administrator. The right belongs to
  whoever gave it, and the platform stores `GivenByMemberId` precisely so this
  question has an answer rather than an assumption.
- **A converted child is refused.** Once conversion completes, that person's data
  is held on their own consent and they have section 12 for themselves. Allowing
  a parent to erase an adult's records on their own say-so would be a worse
  failure than the one being fixed.
- **The consent record survives its own withdrawal.** Section 6(7) requires a
  Fiduciary to be able to produce the consent it relied on, so `GivenAt`,
  `NoticeVersion` and the verbatim attestation stay, with `WithdrawnAt` and
  `WithdrawnByMemberId` added beside them. A withdrawal that wiped its own
  history could not demonstrate that consent had ever been properly obtained.
- **The event carries no child in it.** `members.child.consent-withdrawn.v1`
  holds ids and a timestamp. audit-notification-service subscribes by a catch-all
  pattern and stores payloads verbatim in an append-only table, so an event
  announcing that a child's data may no longer be held would otherwise become
  the one copy of it that outlives everything else.

**Erasure takes the same route, and did not at first.** When a parent erases
their account, every child record held on their consent goes with it — and that
originally ran through a separate method that de-identified the row and left the
consent untouched. The consent went on reporting itself as standing, months
after the person who gave it had no account, and nothing was announced. Two
paths to one outcome and only one of them wrote down that it had happened: for
every child whose parent had erased, section 6(7)'s question of *when* a consent
stopped standing had no answer on the record. Both paths are one method now, and
a test asserts the closed set of ways a child record can stop being held.

## Erasure vs. the audit log — the real tension

§8(7) and §12 require erasure. `SECURITY-CHECKLIST.md` requires the audit log
to be immutable, "no update/delete endpoint for AuditLog, ever".

Both are right, and they collide. The resolution the platform takes:

- **Personal data is erased.** Profile, contact details, family links, the
  login itself.
- **Audit rows are retained but de-identified.** The fact that an action
  happened, and when, survives; the actor becomes a tombstone id with no
  personal data attached. An audit trail that can be erased is not an audit
  trail, and §8(7) has an exception for retention required by law — which is
  what an audit log of administrative actions is for.
- **The mapping from tombstone to person is destroyed**, so the retained rows
  cannot be re-identified.

**Needs counsel:** whether that reading of the retention exception is correct
for this platform, and how long audit rows may be kept.

### How it runs

`POST /v1/identity/me/erase` takes the caller's password and nothing else.
There is no admin approval: §12 gives the Data Principal a right, not a request
for permission, so an admin deciding it would be the wrong shape. The password
is the identity check a Fiduciary needs before acting on something with no undo.
A Super Admin cannot erase this way — nothing but the bootstrap on an empty
database can recreate one, and there is no second Super Admin to notice.

identity-tenant-service clears the account and publishes
`identity.user.erased.v1` **in the same transaction**, so an erasure that
commits is always announced. The event carries two ids and nothing else,
because audit-notification-service records every payload verbatim.

| Service | What it does on that event |
|---|---|
| identity-tenant-service | Clears name, contact, password hash, roles and any outstanding activation code; status becomes `Erased` |
| member-family-service | Clears the profile and closes its privacy settings; erases the children held on this member's own parental consent, wherever they now sit; removes their household membership |
| audit-notification-service | Deletes their notifications; de-identifies the audit rows where they were the actor; records the erasure itself |

Three decisions inside that are worth knowing:

**The identifier is freed.** `MobileOrEmail` is unique platform-wide, so
keeping it would mean a person who left could never come back. It is replaced
with a per-account value at an unroutable domain, which keeps the uniqueness
constraint satisfiable without leaving anything to sign in as.

**Children go with the person who consented for them, not with the household
they sit in.** Their records exist on that consent (§9), and consent that no
longer exists cannot keep justifying the data it covered. This was a household-
shaped lookup at first — every child of the erasing member's family — which was
right only for as long as nobody could leave a household. Once a member could,
a head who left took their headship with them and left the children they
consented to behind, so the old lookup erased nothing for them. Erasure now
follows the consent-giver by id, wherever the child now sits. The birth year
survives, shifted to 1 January, because age is what decides conversion
eligibility and the row still has to behave; the exact birthday is how a child
would be recognised.

**The household itself stays.** Deleting it would take the remaining members'
join with it and orphan the child rows — other people's records restructured
because one person exercised their own right. **Headship passes to the
longest-standing remaining member** when the head erases, by the same rule a
member leaving uses. This section used to say re-heading "is a known gap and
belongs in an admin command" — that was wrong even at the time: an admin
command needs an administrator to notice, and nothing told them, so a household
whose head erased stayed frozen — no join request could be decided, no child
added, no conversion started — until somebody complained.

The single narrowly-scoped exception to append-only audit lives in
`ErasePersonalDataCommandHandler` and `IErasureRepository`. Nothing else on the
platform changes or removes an audit row, there is no endpoint for it, and the
update cannot touch the action, entity, topic or timestamps. If counsel decides
audit rows must be deleted rather than de-identified, those two files are the
whole change.

**What erasure does not reach:** anything published before it. Delivery is
at-least-once and one-way, so a service that consumes platform events and is
built later will not see the erasures that happened before it existed. Any new
consumer needs to subscribe to `identity.user.erased.v1` on the day it ships,
the way member-family-service and audit-notification-service do.

### Six services shipped without doing that

Found by the security-checklist pass on 2026-09-01, and recorded here rather
than quietly fixed, because what the right fix is differs per service and two of
them need counsel.

`timeline`, `events`, `volunteer-groups`, `social-issues`, `celebrity-voting`
and `boli` all hold member ids and none subscribes to the erasure event. The
table above lists three services; there are ten.

**For four of them the residual data is a bare id.** Events registrations,
volunteer-group memberships, votes and bids carry a `MemberId` and nothing else
about the person — no service outside identity and member-family stores a name
or a contact, and both of those clear on erasure. So what survives is a GUID
that resolves to nobody. That is the same de-identification the audit log does
deliberately, arrived at by construction rather than by design, and it is
probably sufficient — but "probably" is not a compliance position, and it is
question 5 below.

Two of those four also have a reason the id must stay. The double-voting
guarantee **is** the unique index on `(CampaignId, VoterMemberId)`; removing or
randomising the voter id would not de-identify a vote so much as re-enable
voting twice. A Boli bid is a financial record the Samaaj collects against, and
retention of those is the kind of thing §8(7) contemplates other law requiring.

**For two of them it was not a bare id, and that was the real gap — now
closed.** `timeline` holds `Post.Body` and `social-issues` holds `Issue.Title`
and `Issue.Description`: free text an erased member wrote, which can identify
its author whatever happens to the id beside it. No amount of reasoning about
GUIDs touches that. Both now consume `identity.user.erased.v1`.

| Service | What it does on that event |
|---|---|
| timeline-service | Empties and hides the posts that member wrote, and empties their comments on anyone's post. Other members' comments and reactions survive |
| social-issues-service | Empties the issues they submitted and drops the locality, and drops any reason they wrote in a history entry. Reviewers' decisions and reasons survive, and the status is not moved |

The rule both follow is **the words go and the shape stays**. A post and an
issue are containers — other people's comments, and a reviewer's decisions,
hang off them — so they are emptied rather than deleted, for the same reason
erasure leaves a household standing rather than deleting it out from under the
people still in it. A social issue's status is deliberately not moved either: a
published issue that vanished would leave a Samaaj wondering what happened to
something it was told about. What it said is gone; that it existed is not the
submitter's alone to erase.

Building those two consumers exposed a second thing. **Six services carried
`"GroupId": "timeline-service"` in their Kafka config**, having been scaffolded
from it, and nothing had broken only because just one of them actually ran a
consumer. Kafka gives each message in a group to exactly one member, so adding
these two would have had erasure events delivered to a service that ignores
them, committed, and lost — intermittently, by partition assignment, which is
the worst way for an erasure to fail. The group id now lives only in each
service's `ConsumerOptions`, beside its topic list, where a copied
`appsettings.json` cannot reach it.

The four services holding bare ids remain as described above, and are counsel
question 6.

## Security safeguards (§8(4))

In place: passwords hashed with PBKDF2-HMAC-SHA256 at 210,000 iterations with
a per-hash salt and a stored iteration count; brute-force lockout on login;
activation codes stored as hashes, single-use, expiring, with an attempt limit;
tenant isolation enforced by a database-level query filter *and* re-checked on
every write path; JWTs short-lived and validated by every service rather than
by the gateway alone; no credential ever placed in a Kafka event, because the
audit log records payloads verbatim.

Cross-tenant isolation is probed rather than assumed:
`scripts/tenant-isolation-probe.sh` has a second Samaaj's member and
administrator attempt 36 reads and writes against the first Samaaj's ids, and
all 36 are refused. And the databases are provably restorable —
`scripts/backup-restore-drill.sh` dumps all ten and restores each into a scratch
copy, comparing row counts and the unique indexes that are correctness
guarantees.

Not in place: encryption at rest (a deployment concern, not yet specified),
TLS termination policy, key rotation, a breach-detection process, and
penetration testing — the last is already Phase 5 in `DEVELOPMENT_PLAN.md`.

**Logs are a place personal data can escape to, and outbound notifications are
where that risk starts.** A message to a member is addressed to them and written
for them, so a log of both is a copy of personal data outside the database —
somewhere erasure does not reach and retention policy usually does not cover.
The logging channel therefore writes a redacted address (`r***@example.com`) and
the message title, never the body, unless
`NotificationDelivery:Logging:RevealContent` is turned on. It is off by default,
on in `docker-compose.yml` because that stack is local development, and the
service says so at Warning on every start so it cannot be left on unnoticed.

**Availability is a safeguard too, and only half of it is done.** §8(4) asks for
safeguards against loss as well as against breach. The drill proves a dump
restores; it does not make a dump a backup. The dumps land beside the database
they came from, and being full dumps the recovery point is whenever one last
ran. Off-host storage, WAL archiving for point-in-time recovery, and a schedule
are hosting decisions, and until they are made the platform can restore from a
backup it has but does not reliably have one to restore from.

### Breach notification (§8(6)) — a channel, but not the duty

There is now a way to send a member a message that is not "wait until they next
open the portal": audit-notification-service queues outbound notifications and
hands them to a channel adapter (`INotificationChannel`), with retries, an
attempt limit and a delivery record per message.

**The adapter behind it writes to a log and delivers nothing.** That is a real
constraint on what can be claimed, not a detail. A notification marked `Sent`
today means it reached the channel, not that it reached a person, so nothing
here evidences that affected data principals were told. §8(6) also wants a
Board notification and a description of the breach, neither of which is a
message-sending problem.

There is now a way to address every member of a Samaaj at once - a broadcast is
one notification row with no recipient, and every member of that Samaaj sees it.
That closes the "one at a time" gap, and closes it **in the app only**: a
broadcast reaches a member when they next open the portal, which is not
notification in any sense §8(6) would accept. Sending one by email or text needs
somewhere to read every member's address from, and audit-notification-service
holds no directory - it learns an address from an event that happens to carry
one.

What is still needed, in order: a provider so `Sent` means what it says; a
source of every affected member's contact address; the wording, and the Board's
prescribed form; and the detection that starts any of it, which is a monitoring
question the platform has not answered.

What the channel does discharge is the excuse: the reason §8(6) and §14 were
both marked "needs a notification channel" no longer applies, and what is left
in each is the obligation itself.

## What each Samaaj must decide, not the software

These are Data Fiduciary obligations that no amount of code discharges:

- Naming a **Data Protection Officer or grievance contact** and publishing how
  to reach them (§13). The platform needs a field for it; the person is theirs.
- **Retention periods** for each category of data.
- The **wording of the notice**, including the Board's prescribed contents and
  the language options §5(3) requires.
- Whether the Samaaj qualifies as a **Significant Data Fiduciary** (§10), which
  brings a mandatory DPO in India, independent audits, and impact assessments.
- How parental consent is made **verifiable** in their context.

## Open questions for counsel

1. Is de-identifying audit rows, rather than deleting them, a defensible
   reading of the retention exception in §8(7)?
2. How long may audit rows and consent records be retained after erasure?
3. What makes parental consent "verifiable" for a community organisation whose
   members are known to each other in person? Is the family head's attestation,
   recorded with a timestamp and notice version, sufficient?
4. Does a member's Samaaj-facing directory listing need consent, or is it
   "necessary for the specified purpose" of running a membership organisation?
   A member can now take themselves out of the directory from their profile
   screen, which makes the listing declinable without leaving the Samaaj - but
   it is opt-out, and whether the Act wants opt-in here is the question.
5. Are the Samaaj and the platform operator joint fiduciaries, or is the
   operator a Data Processor? This changes who notifies the Board on a breach.
6. After erasure, is a bare `MemberId` left behind in a service that holds no
   name or contact — a registration, a group membership, a vote, a bid —
   still personal data? Every service that could resolve it to a person has
   cleared, so nothing on the platform can. If the answer is yes, two of those
   ids cannot simply be removed: the voter id **is** the double-voting
   guarantee, and a bid is a financial record. See "Six services shipped
   without doing that" above.

7. A member can now leave a household without erasing their account, which
   separates two things that used to move together. If the parent who gave a
   child's consent leaves, and the other parent goes on heading the household,
   whose basis is that child's record held on afterwards? The platform's answer
   today is the strictest reading: the consent is still that person's, so
   erasing them still removes those records, wherever the child now sits
   (`ListByConsentGiverAsync`). The alternative — that the household's
   continuation is itself a basis — would leave the record standing, and is not
   something the code should decide on its own.

   The narrower half is already handled rather than left open: the **last**
   member of a household with children cannot leave at all, because nothing on
   this platform can remove a child record and those records would otherwise
   have nobody able to manage them, permanently.

Nothing in this repository should be taken as an answer to any of these.
