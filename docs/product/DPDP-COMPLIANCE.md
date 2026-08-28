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
| 6(4)–(6) | Withdrawal as easy as giving | **built** | `POST /v1/identity/me/consents/{purpose}/withdraw` |
| 6(7) | Consent records retained and producible | **built** | `ConsentRecord` is append-only |
| 8(1) | Process only for the consented purpose | partial | Purpose is recorded; enforcement is by code review, not by a runtime check |
| 8(4) | Reasonable security safeguards | partial | See "Security" below |
| 8(6) | Breach notification to the Board and affected principals | **not built** | Needs a process and a notification channel |
| 8(7) | Erase when consent is withdrawn or the purpose is served | **not built** | Next cycle; see "Erasure vs. the audit log" |
| 9 | **Verifiable parental consent for under-18s** | partial | Consent is recorded on `ChildProfile`; "verifiable" needs counsel |
| 9(3) | No tracking or behavioural monitoring of children | **built by absence** | The platform does none. Keep it that way. |
| 11 | Right to access a summary of data and processing | **built** | `GET /v1/identity/me/data-export` |
| 12 | Right to correction and erasure | partial | Correction exists (profile edit); erasure does not |
| 13 | Right to grievance redressal | **not built** | Needs a named officer per Samaaj |
| 14 | Right to nominate | **not built** | |

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

### Data export

`GET /v1/identity/me/data-export` returns what identity-tenant-service holds
about the caller, in a form a person can read: the account, the roles, the
consent history, and the processing purposes.

**It is deliberately per-service.** A member's data is spread across
identity, member-family and audit, and the alternative — one service reaching
into the others synchronously — would undo the service boundaries for a feature
used a handful of times a year. Each service exposes its own export and the
portal assembles them. This is a documented gap in convenience, not in rights:
everything is reachable.

## Erasure vs. the audit log — the real tension

§8(7) and §12 require erasure. `SECURITY-CHECKLIST.md` requires the audit log
to be immutable, "no update/delete endpoint for AuditLog, ever".

Both are right, and they collide. The resolution the platform will take:

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

## Security safeguards (§8(4))

In place: passwords hashed with PBKDF2-HMAC-SHA256 at 210,000 iterations with
a per-hash salt and a stored iteration count; brute-force lockout on login;
activation codes stored as hashes, single-use, expiring, with an attempt limit;
tenant isolation enforced by a database-level query filter *and* re-checked on
every write path; JWTs short-lived and validated by every service rather than
by the gateway alone; no credential ever placed in a Kafka event, because the
audit log records payloads verbatim.

Not in place: encryption at rest (a deployment concern, not yet specified),
TLS termination policy, key rotation, a breach-detection process, and
penetration testing — the last is already Phase 5 in `DEVELOPMENT_PLAN.md`.

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
5. Are the Samaaj and the platform operator joint fiduciaries, or is the
   operator a Data Processor? This changes who notifies the Board on a breach.

Nothing in this repository should be taken as an answer to any of these.
