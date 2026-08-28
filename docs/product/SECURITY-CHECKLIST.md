# Security Checklist

Derived from the cross-cutting requirements in both requirement docs.
Treat this as a PR review checklist for anything touching a new
endpoint, not just a one-time setup task.

## Tenant isolation

- [ ] Every tenant-owned entity has `TenantId` and an EF Core
      `HasQueryFilter` applying it on every read.
- [ ] Every write handler re-validates that the target entity's
      `TenantId` matches `ITenantContext.TenantId` — do not rely on the
      query filter alone for writes (IDOR protection).
- [ ] `TenantId` is never read from a client-supplied field in a
      request body. It comes only from the resolved gateway context.
- [ ] Super Admin tenant-override requests are logged with both the
      actor's identity and the overridden `TenantId`, on every request,
      not just at session start.

## Authorization

- [ ] Every endpoint has an explicit `[Authorize(Policy = "...")]` or
      equivalent — no endpoint relies on "nobody will call it directly."
- [ ] UI hiding of a nav item or button is never the only control —
      confirm the backend rejects the action too.
- [ ] `TenantAuthorizationBehavior` runs before `ValidationBehavior` in
      the pipeline (see `ARCHITECTURE.md` §3) so an unauthorized caller
      never learns anything about validation rules for data they can't
      access.

## Permission key naming convention

Use `{Module}.{Action}`, matching the style already used in your
existing platform (`Pathshala.Attendance.Write`, `Issue.Approve`):

| Key | Grants |
|---|---|
| `Tenant.Manage` | Create/activate/deactivate tenants |
| `AdminUsers.Manage` | Create/invite admins, assign roles |
| `Members.Read` / `Members.Write` | Directory search / profile correction |
| `Family.Write` | Family/child management |
| `Family.ApproveConversion` | Approve adult-child conversion |
| `Timeline.Post` / `Timeline.Moderate` | Post / moderate content |
| `VolunteerGroups.Manage` | Create groups, decide applications |
| `Events.Publish` | Create/publish events |
| `SocialIssues.Approve` | Approve/reject/publish issues |
| `CelebrityVoting.Configure` | Create campaign, close, publish results |
| `Pathshala.Manage` | Super Admin: create Pathshala master record |
| `Pathshala.Attendance.Write` | Teacher: mark attendance |
| `Pathshala.Exams.Write` | Teacher: record results |
| `Boli.Manage` | Occasion/type/open/close |
| `Boli.PublishResults` | Publish (irreversible without correction flow) |
| `Audit.Read` | View audit log |

## Audit logging

- [ ] Every state-changing command that matches an item in the "Audit
      Logs" section of the admin requirements doc publishes a domain
      event that `audit-notification-service` consumes.
- [ ] Audit rows are immutable (no update/delete endpoint exists for
      `AuditLog`, ever).
- [ ] Before/after JSON snapshots are captured for corrections and
      status changes, not just creation events.

## Session & auth

- [ ] Sessions/JWTs are tenant-scoped and expire; refresh tokens are
      revocable server-side (e.g. on suspicious activity or admin
      forced logout).
- [ ] Rate limiting and brute-force lockout on `/login` and any OTP
      endpoint.
- [ ] All production traffic is HTTPS-only; no mixed content.
- [ ] Sensitive admin actions (publish Boli result, publish voting
      results, deactivate a tenant) are candidates for step-up
      authentication (re-enter password / OTP) — see the "Suggested
      Enhancements" callouts in the requirements docs.

## File handling

- [ ] Uploaded files (post media, social issue evidence, profile
      photos) are size/type restricted and virus-scanned before being
      served back to any user.
- [ ] File storage access is authorization-checked per request, not
      just obscured by a random URL.

## Data privacy

- [ ] Member directory respects `PrivacyLevel` per profile field, not
      just an all-or-nothing visibility toggle.
- [ ] Exports of member data are logged as audit events and restricted
      to roles with an explicit export permission.
