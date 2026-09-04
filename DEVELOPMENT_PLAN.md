# Development Plan — samaajconnect

Living document — check items off as they're completed, and keep
**Current Status** below up to date. This is the actionable/trackable
version of the plan; the reasoning behind the ordering lives in
`docs/product/ROADMAP.md` and shouldn't be duplicated here.

## Current Status

- **Stage:** every module has a service and member screens; Phase 5 hardening under way
- **Last updated:** 2026-09-04 - **the way out of a household, and whose consent
  a child's record is held on.**

  Last cycle gave a member a way to take back a request nobody had answered, and
  said in the same breath that it withdraws a request and *never* a membership.
  That was the right line to draw and it left the obvious question standing:
  **once you are in a household, there was no way out at all.** Joining was
  permanent short of erasing your entire account — which is a rights request
  under DPDP section 12, not a way to correct a family you joined by mistake or
  have since left.

  `DELETE /v1/families/mine/membership` is that way out. Headship passes to the
  longest-standing remaining member by the same rule the erasure consumer
  already used, so the two paths out of a household cannot disagree about who
  takes over.

  **The one refusal is about children, and it is a real one.** The last member of
  a household that has children may not leave. Nothing on this platform deletes a
  child record — a child record ends when the consent behind it ends, through
  erasure — so a household emptied of adults would leave those records standing
  with nobody able to manage them, permanently. The service answers 409 by name
  and the screen says why rather than offering a button that always fails.

  **And that turned up the actual bug.** Erasure removed "the children of the
  household this member heads". Both halves of that were the wrong question. It
  read the *household*, so a parent who had given consent and then left took none
  of it with them; and it read `IsHead`, so a consenting parent who was not the
  head erased none of the children their consent was holding up. Section 9 makes
  the parent's consent the basis on which a child's record may be held at all —
  so erasure has to follow the consent, not the family tree.
  `ListByConsentGiverAsync` does, wherever the child now sits, and there is a
  test for exactly the case the old code got wrong.

  This was invisible before this cycle because the two could not come apart:
  leaving was impossible, so a consent-giver was always still in the household.
  Adding one feature is what made an existing rule wrong.

  Whether the household's *continuation* is itself a basis for those records is
  a question for counsel, not for the code, and it is written down as one -
  `docs/product/DPDP-COMPLIANCE.md`, open question 7. The platform's answer today
  is the strictest reading.

  Verified by three injections: making succession a no-op on leaving, letting the
  last member of a household with children leave, and putting the household-shaped
  lookup back into erasure. Each failed the test that should have caught it, and
  each passed again unmodified afterwards.

  324 of 324 smoke checks green through the gateway, which is 320 plus
  exactly the four added here. One run at a time, which is last cycle's lesson
  and cost nothing to remember.

  1,657 tests green, up 13 — 3 unit and 4 integration in member-family-service,
  6 in the member portal.
- **Previously:** 2026-09-04 - **two ways a member could be stuck with no way
  out.**

  Three cycles of static checks in a row was enough, so this one is behaviour.
  member-family-service's own CLAUDE.md named a gap — "a household whose head has
  erased can no longer decide a join request; re-heading one is a known gap" —
  and looking at it turned up a second, sharper one that needed no erasure at all.

  **A member could ask to join a household and never take it back.** A pending
  request counts as belonging to one, deliberately, so nobody can ask two
  families at once and have both heads accept. Nothing could cancel it. So a
  head who was slow, or who never looked, left that member unable to join
  anywhere *or create a household of their own* — indefinitely, with no way out
  that did not run through somebody else. That is not an edge case; it is what
  happens when a head is simply slow. `DELETE /v1/families/join-requests/mine`
  is the member's own way out, and the family screen now has the waiting state
  it never had.

  **It withdraws a request and never a membership.** If the head accepted while
  the member was deciding to withdraw, the call is refused by name rather than
  quietly succeeding — otherwise "cancel my request" would silently mean "leave
  my family" for exactly the person whose request had just been accepted.

  **And headship now passes to the longest-standing member when a head erases**,
  which reverses a decision this repository had written down. The erasure
  consumer said re-heading "belongs in an admin command, not here". An admin
  command needs an administrator to notice and nothing tells them, so the
  household stayed frozen until somebody complained — and four things were
  frozen, because all four are gated on `IsHead`: deciding a join request,
  adding a child, starting a conversion, and seeing the family code to invite
  anyone. Doing it in the consumer means the headless state never exists rather
  than existing until repaired.

  Longest-standing needs no judgement and explains itself in a sentence. It is
  the earliest to have *joined*, not to have asked, so a request accepted last
  week does not outrank a member of ten years — there is a test for exactly that
  distinction.

  **Two of my own first versions were wrong, and both were caught by running
  them rather than by reading them.**

  The refusal message sat inside the waiting card, and withdrawing re-reads — so
  the reload that made the message true was the same reload that removed the card
  displaying it. A test caught that; it is at page level now.

  And the smoke block used the invited Samaaj administrator as its second member.
  Every check answered 404, because an invited admin account is created by
  invitation, which does not publish `identity.user.registered.v1` — so
  member-family-service has no profile for them and they are not a member of
  anything. Three checks failed for a reason with nothing to do with
  withdrawing, and had they happened to pass they would have proved nothing at
  all. The block registers a real member now and waits for the profile to arrive
  over Kafka before using it.

  Verified by three injections: making succession a no-op, letting withdrawal
  remove an accepted membership, and removing the validator-shaped guard. Each
  failed the test that should have caught it.

  **Two smoke runs against one stack destroy each other, and the wreckage is
  convincing.** I started a second run before the first had finished. They both
  wrote `.smoke.log`, so the output interleaved into one plausible-looking file,
  and they both toggled the same Samaaj's module keys — so the volunteer-groups
  section read `community` as off immediately after its own section had switched
  it on, and every check answered the gateway's module-gate 404. Twenty minutes
  went into a contradiction that was not real. The clean run is 320 of 320,
  which is 313 plus exactly the seven checks added here. One run at a time.

  1,644 tests green, up 20 — 14 in member-family-service and 6 in the member
  portal. Seven new smoke checks through the gateway.
- **Previously:** 2026-09-04 - **a decision note longer than its column
  answered 500.**

  The next unenforced rule after the last two. Root `CLAUDE.md` §4.3 asks for one
  validator per command or query, and `ValidationBehavior` runs the validators
  that exist — so a request with none has **no input validation at all**, not a
  lighter version of it. 61 of the platform's requests had no validator.

  Most of those are right: a query with no parameters has nothing to check. The
  ones that matter carry a **string**, because that is the input that can be the
  wrong length and a length is what a database refuses. Three did.

  **`DecideChildConversionCommand` was the real one.** It takes a free-text
  decision note, had no validator, and `DecisionNote` is capped at 1000
  characters in the column — so a longer note reached Postgres, was refused with
  SQLSTATE 22001, `UnhandledExceptionBehavior` turned that into a generic
  failure, and a Samaaj administrator who wrote a long note was told only that
  something had gone wrong.

  **That was measured, not reasoned about.** The test was written to assert the
  400 it should give, run against the unfixed code, and answered
  `500 InternalServerError` with `Failure/Unexpected` in the log. Then the
  validator, then green — and relaxing the rule to 100,000 puts the failure
  straight back.

  The length now lives in `ChildConversionRequest.MaxDecisionNoteLength`, which
  the validator and the EF configuration both read. They were two numbers and
  only one existed; making them one number is what stops the 500 coming back the
  next time somebody changes the column.

  **The other two findings were fine, and saying why is the point.**
  `WithdrawConsentCommand.Purpose` is parsed against an enum in its handler and
  answers 404 on anything else; `ListIssuesQuery.Category` is a parameterised
  equality where an unknown value matches nothing. Neither is stored. A
  validator on either would be ceremony, so `scripts/validator-coverage.sh`
  reports rather than fails — a list carrying permanent known-good entries is
  one people learn to skim, which is the same reasoning the endpoint sweep uses.

  The rule worth remembering is narrower than "every command needs a validator":
  **free text that is persisted must be bounded before it reaches the database**,
  because the database's refusal is a 500 and a validator's is a 400.

  1,624 tests green, up 1. CI runs the sweep.
- **Previously:** 2026-09-04 - **the sweep for fields nothing can write.**

  The previous cycle ended by naming a gap and not filling it: **a field the API
  can read and nothing can write is the same family of gap as an endpoint with
  no caller**, and `scripts/unreachable-endpoints.sh` finds only the second.
  `scripts/unwritable-fields.sh` is the first. It reads every domain property
  with a private setter - 396 of them - and reports any that nothing in its
  service assigns.

  **It finds nothing today, and that is only worth saying because it was proven
  able to find something.** Run against the tree one commit back it reports
  `Tenant.LogoUrl`, which is exactly the field it was written for; run against
  main it reports none, because that field is genuinely gone. A sweep validated
  only by its own silence is the trap this repository has now hit three times.

  **The first version was silent for the wrong reason.** Its property-matching
  regex matched nothing at all, so a clean run meant "found no properties"
  rather than "found no problems" - 0 of 0 rather than 0 of 396. Printing the
  denominator is what makes that visible, which is why the summary carries it.

  **Two false-positive classes, both real and both fixed.** Counting only `X =`
  reported `ActivationCode.FailedAttempts`, which `RecordFailedAttempt()`
  increments; `++`, `--`, `+=` and `-=` are writes too. And
  `Notification.DeliveryAttempts` is written by raw SQL -
  `delivery_attempts = delivery_attempts + 1` inside the claim query, where it
  has to be, because the increment must be atomic with claiming the row. The
  sweep now looks for the snake_case column name as well.

  That second fix was itself verified rather than assumed: deleting the SQL
  increment brings the finding straight back, so the SQL branch narrows the
  search rather than blanket-suppressing it. A suppression that could not fail
  would have left a check that always passes.

  **Reported, never failed**, like the endpoint sweep and for the same reason: a
  property with no writer is not automatically a bug, and a list with a
  permanent known-good entry on it is one people learn to skim.

  1,623 tests green, unchanged - this cycle adds a check, not behaviour.
- **Previously:** 2026-09-04 - **Samaaj logos, and a field nothing could
  write.**

  The other half of the image work, and it started by finding that the problem
  was not the one the plan described. `LogoUrl` was tracked as a client-supplied
  link carrying the same third-party tracking risk as a member photo. It was
  worse than that and also harmless: **no command ever took a logo**, so the
  column was null on every row the platform has ever had, while the admin
  wireframe's Create Samaaj screen drew an "Upload Logo" control with nothing
  behind it. The security note was about something that could not happen, which
  dilutes the notes that can.

  Worth generalising, because this repository has spent several cycles on the
  mirror image: **a field the API can read and nothing can write is the same
  family of gap as an endpoint with no caller.** `scripts/unreachable-endpoints.sh`
  finds the second and nothing looks for the first.

  **The read is anonymous, and that is the one real difference from a member's
  photo.** Somebody registering picks their Samaaj before they have an account,
  so `ListRegisterableTenantsQuery` is anonymous by necessity and a logo needing
  a token could not appear beside the name it already publishes. A logo is an
  organisation's public mark - the one on its letterhead - and reveals nothing
  about a person. So it is the only image on the platform outside per-request
  authorization, `Cache-Control` is `public` rather than `private`, and both the
  checklist and the service's own CLAUDE.md say so explicitly rather than
  letting the member-photo tick imply it covers logos.

  A consequence worth noticing: logos need no `scAuthedSrc`. A plain `<img src>`
  works precisely because the endpoint is anonymous, which is the design being
  coherent rather than a shortcut.

  **`scripts/security-invariants.sh` failed on the first run of this cycle, as
  designed.** Adding an anonymous endpoint fails until somebody writes a
  paragraph on the checklist explaining why - the mechanism built two cycles ago
  doing its job on the next cycle that needed it. The drift check also grew a
  second shape: `ImageContent` is copied verbatim into identity-tenant-service,
  and the existing loop could not express "identical wherever it appears"
  because it fails a file for being absent.

  **Two things went wrong.** An injection making the upload endpoint anonymous
  did not fail anything, because `TenantAuthorizationBehavior` refuses on the
  request attribute regardless - defence in depth working, but it meant the test
  had to be re-proved by opening both layers, which failed two tests as it
  should. And the smoke script had grown a second `trap ... EXIT` that silently
  replaced the first; there is one scratch root and one trap now.

  Nine smoke checks through the gateway, including that a member and an
  anonymous caller both fail to set a logo that anyone may read.

  1,623 tests green, up 29 — 18 in identity-tenant-service and 11 across the
  two portals.
- **Previously:** 2026-09-04 - **the platform hosts its own photos now.**

  The last Phase 5 item that needed nothing from outside. A member's or a child's
  photo was a URL a client supplied, validated as absolute `http(s)` - which
  closed the `javascript:` hole and said plainly, in `ImageUrl`'s own remarks,
  that it did nothing about the real one: every member who opened the directory
  fetched the picture from whatever host it named, handing that host their IP
  address. On a `ChildProfile` that is exactly the third-party tracking of
  children DPDP s.9(3) prohibits, and this platform's compliance answer for
  s.9(3) has always been "we do not do it".

  **The bytes live in member-family-service's own Postgres, and the obvious
  alternative was considered rather than skipped.** MinIO in compose and S3 in
  production is the standard answer; at this scale it buys nothing and costs a
  second place data lives - one `scripts/backup-restore-drill.sh` does not dump.
  A platform that has spent three cycles proving its backups restore would have
  quietly acquired a store outside them. In the database the images are inside
  the existing dump, inside the tenant query filter, and inside the transaction
  that writes the profile row. `IImageStore` is the seam that makes changing that
  one implementation and a migration rather than a rewrite.

  **The type is read from the bytes and never from the upload.** A declared
  content type is a string the uploader chose, so `StoredImage.Capture` has no
  parameter for one at all and a test asserts that absence. JPEG, PNG, WebP.
  **SVG is refused, and that exclusion is the load-bearing one**: an SVG is a
  document that can carry script and these are served from the platform's own
  origin, so accepting one would trade a tracking hole for a stored-scripting
  hole.

  **Authorization is the profile's own rule, which is why the owning service
  serves the bytes.** Who may see a member's photo is who may see the member;
  a child's photo is their household's, so `Members.Write` does not open it -
  the same line `DecideJoinRequestCommand` already draws. A media service would
  have had to be told those rules, asked about them, or handed a signed URL.
  That also gives `SECURITY-CHECKLIST.md`'s "authorization-checked per request,
  not just obscured by a random URL" for free, and that box is ticked now.

  **A plain `<img src>` cannot render these, and working out why was the
  interesting part.** Both apps keep the token in `sessionStorage` and attach it
  in an interceptor - deliberately, because a cookie would be sent automatically
  and this platform has no CSRF protection. A tag the browser fetches by itself
  carries no `Authorization` header, so the very thing that makes
  token-in-storage safe is what breaks an image tag. `libs/shared`'s
  `AuthedImageDirective` fetches through `HttpClient` and owns the object URL's
  lifetime. Unauthenticated-but-unguessable was the alternative and the checklist
  rules it out in as many words.

  **Four things went wrong that are worth keeping.** A test asserting an upload
  error contained "JPEG, PNG or WebP" was passing off the static help text under
  the file input, which says exactly that - it would have passed with no error
  shown at all. `describeError` reads `detail`, not `title`, which is what
  exposed it. Putting both endpoint groups in one file broke CLAUDE.md §4.6 and
  `unreachable-endpoints.sh` caught it by mis-reporting three `/v1/children`
  routes as `/v1/members` ones. A child-photo test 404'd for the wrong reason
  until the parent uploaded first. And editing the smoke script while it was
  running corrupted the run - bash reads a script by byte offset.

  Verified by seven injections across the three layers - dropping the self-check,
  dropping the family check, making the sniffer always answer JPEG, and the
  directive's three guards - each caught by the check it should be. Nine smoke
  checks through the gateway, including a JPEG named `.png` coming back as a
  JPEG.

  What is left: a Samaaj's `LogoUrl`, and virus scanning, which needs a scanner
  in the deployment rather than a check in a domain type.

  1,594 tests green, up 58 — 36 in member-family-service and 22 across the two
  frontend suites.
- **Previously:** 2026-09-04 - **acting on the lesson from yesterday: the
  other hand-written lists.**

  §9 now says a hand-written list of the ten services is a list something will
  fall off, and it will not fail when it does. Having written that, the thing to
  do with it was go and look. The sweep was cheap and found two more, neither of
  them about services.

  **The permission table on `SECURITY-CHECKLIST.md` had fallen two keys behind
  `AuthorizationCatalog`.** `Roles.Manage` and `VolunteerGroups.Lead` are both
  in the catalogue, seeded by migration and gated on in code, and neither was on
  the page that documents permissions. `Roles.Manage` is the load-bearing one:
  it is the lock-out floor a Samaaj administrator cannot lose, which the admin
  panel draws as a fixed tick rather than a checkbox, and it was undocumented.

  `scripts/security-invariants.sh` now checks all three copies against each
  other - the catalogue, each service's `PermissionKeys.cs`, and the table. The
  first pair matters more than the third: a service gating on a key no role
  holds is an endpoint that answers 403 to everybody, which is a live bug rather
  than a documentation one. They agree today at 21.

  **The module keys are three lists in three languages and nothing held them
  together.** `ModuleCatalog`, `libs/shared`'s `ModuleKeys`, and the gateway's
  route metadata. Both code comments already said "adding a module means adding
  it in three places" - a rule written where it is read only by somebody who
  already knows it. `scripts/module-keys.sh` reads all three from the files that
  own them. They agree today at 5.

  What a mismatch does is worth restating, because none of it fails loudly. A
  portal key the catalogue lacks never matches, so the feature is invisible to
  every Samaaj forever with nothing logged - which is exactly what happened to
  Home's Events and Volunteer tiles. A gateway route gated on a key nobody can
  enable is permanently 404, indistinguishable from a Samaaj that switched the
  module off. A catalogue key with no route is a toggle that switches nothing.

  **Two checks I wrote and then removed**, both for the same reason. A
  string-literal scan for module keys in components found four hits and all four
  were `path: 'pathshala'` in the route tables - a URL segment spelling the same
  word - and the real case is already a compile error, because `ModuleKey` is a
  union type and `ModuleTile.moduleKey` is typed to it. And my first permission
  extraction read one key per table row, which reported `Members.Write` and
  `Timeline.Moderate` as missing when both were sitting in the table beside
  their pair. Caught before either shipped, but they are the same trap in both
  directions: a check that fails for the wrong reason, and a check that would
  have passed for one.

  Verified by four injections: a module dropped from `libs/shared`, a catalogue
  key misspelled, a permission key deleted from the table, and a service gating
  on a key no role holds. Each caught by the check it should be.

  1,536 tests green, unchanged.
- **Previously:** 2026-09-03 - **boli-service was not in CI, and had not been
  for eleven cycles.**

  The previous cycle made the security checklist check itself, and the obvious
  next question was which other rule this repository states in prose and
  enforces with nobody. Root `CLAUDE.md` §9's last bullet - at least one test
  per service **through the gateway** - was the candidate, because it had
  already been silently false once: boli-service had no gateway coverage at all
  while its module key was toggled on and off around it.

  Writing the check found the worse thing. **Its solution was never added to
  the CI matrix.** Forty-nine tests - the anti-sniping suite added yesterday,
  `ConcurrentBiddingTests`, and `BidIndexTests`, which is what holds "a Boli has
  exactly one highest bid" - had never run anywhere but a developer's machine.
  Nor was it in the migrations job, so a model change without a migration would
  have stayed invisible until a deployment failed against a real database.

  Nothing failed, because that is not how this goes wrong. A service missing
  from a hand-written list does not break the list; the list is simply shorter,
  and shorter lists pass faster. boli-service happened to be absent from three
  at once - CI matrix, migrations job, gateway smoke - and the only reason the
  third was ever noticed is that somebody went looking for endpoints no screen
  could reach.

  `scripts/service-coverage.sh` asks the one question behind all three: **is any
  service quietly left out?** It reads each mapping from the file that owns it -
  the gateway's route table for prefixes and clusters, `ci.yml` for the two
  lists, the smoke script for the calls - and checks all of them against the ten
  directories under `services/`. A new service now fails until it is in every
  one. CI runs it.

  Verified before trusting it: boli-service builds clean in Release with
  warnings-as-errors, its 49 tests pass, and it has no pending model changes -
  so the CI entry was missing rather than omitted for a reason. And by four
  injections, each caught by the check it should be: removing every Boli call
  from the smoke script reproduces the historical gap exactly, dropping the
  matrix entry reproduces the one found here, repointing a cluster at a service
  that does not exist, and deleting a cluster outright.

  The generalisation is now written into §9, because it is worth more than the
  instance: **a hand-written list of the ten services is a list something will
  fall off, and it will not fail when it does.** Every such list should be
  derived from the directories or checked against them.

  1,536 tests green - 49 of which now run somewhere other than here.
- **Previously:** 2026-09-03 - **the security checklist checked itself, and
  found three things.**

  The re-pass was waiting on the erasure gap, which closed two cycles ago. Its
  status block said "walked against all ten services and the gateway" with a
  date on it - which is worth exactly as much as the date. The pass before that
  one covered three services, and six of the seven that shipped afterwards were
  never re-checked. This repository has now watched that same shape play out on
  the accessibility audit, the isolation probe and the backup drill in turn.

  So the parts a machine can do, `scripts/security-invariants.sh` does, in a
  second, in CI: all 134 request types carry one of the four authorization
  attributes; the anonymous and internal sets match the lists **written on the
  checklist itself** and read out of it, so either side moving fails the check;
  no route reaches an `[InternalRequest]` command; every `DbContext` filters by
  reflection over `ITenantScopedEntity`; every service calls `TenantWriteGuard`;
  and the eleven files meant to be identical across all ten services are.

  **That last check is the one that found something.** `KafkaProducer` had
  drifted: eight of the ten defaulted `ClientId` to `"member-family-service"`,
  copied and never changed. It is the producer-side twin of the consumer-group
  bug from a fortnight ago - six services sharing the group id
  `timeline-service` - and that fix looked at consumers only. Nothing was ever
  misattributed at the broker, because every service overrides it in its own
  `appsettings.json`; what was there was the trap. The default is empty now and
  falls back to the running assembly, so an unconfigured service names itself
  rather than another one.

  Two more, both the page being confidently wrong about itself. It cited
  `PipelineBehaviorTests`, which does not exist in this repository and appears
  never to have. And it said the isolation probe makes 36 cross-tenant
  attempts; it makes 65 and reports its own coverage, so the number is gone
  rather than corrected - a second copy of a count is the thing that goes
  stale.

  Verified by injecting four failures - stripping an attribute, making a Boli
  command anonymous, naming an internal command in an endpoint file, and
  changing one byte in one `KafkaProducer` - each caught by the check it should
  be, each restored and re-run green. The script also refuses to pass when it
  finds fewer than 100 request types, because a scan that reads nothing reports
  everything it read as fine.

  Its attribute scan skips comments, and that is not tidiness:
  `RecordIntegrationEventCommand`'s own remarks say it carries `[InternalRequest]`
  rather than `[AllowAnonymousRequest]`, and the first version read that sentence
  and concluded the command was anonymous. A checker that reads prose can be
  talked into anything.

  1,536 tests green, unchanged.
- **Previously:** 2026-09-03 - **a Boli can no longer be won by arriving
  last.**

  The final Open Decision that needed nothing from outside. I had read it as
  waiting on a Samaaj to choose the rule; re-reading it, the *window length* is
  the Samaaj's choice and the mechanism was always mine to build, exactly as
  `MinIncrement` already was. `AutoExtendSeconds` is that window, per Boli,
  0 (off) by default so nothing that existed before it behaves differently.

  **The extension is measured from the bid, not from the old close**, and that
  is the whole design rather than a detail. Adding a fixed amount to `EndAt`
  would still reward waiting — bid with a second to go and the room gets a
  second plus the window, while bidding early costs the sniper nothing.
  Measured from the bid, every bid buys everybody the same full window, so
  there is no moment better to bid at than any other. That is what makes
  sniping pointless rather than merely harder, and it is the one thing here
  worth getting right.

  **The member portal says so, and that is part of the rule rather than a
  garnish.** With the window on, `endAt` is a time the server moves, so printing
  it alone would be the portal stating something that stops being true — and
  sniping is only pointless once the people bidding know a late bid buys
  everybody else another window. The line quotes minutes only when the window is
  a whole number of them, because rounding 90 seconds up to two would print a
  longer window than the server keeps.

  `ExtendIfClosing` runs inside `PlaceBidCommandHandler`, under the row lock
  that already makes "one highest bid" true — two bidders racing in the last
  second cannot both read the old close and write conflicting new ones. It
  raises `boli.extended.v1` only when the window actually moved, because an
  outbox row per bid for something almost never true sits on this service's
  busiest write path.

  Verified by injecting the sniper's-advantage bug — `EndAt = EndAt + window`
  instead of `EndAt = now + window` — which failed 5 tests including the one
  named for it, then restored and re-run green. The smoke run checks the pair
  through the gateway: a Boli with a window moves when bid on in its closing
  minutes, and the one without a window does not, because a bug that extended
  every Boli would pass the positive check alone.

  1,536 tests green, up 18.
- **Previously:** 2026-09-03 - **the smoke run stops at a bad id instead of
  blaming ten services for it.**

  The last actionable item on the Phase 5 list that needed nothing from outside.
  The session-id bug it was written after was invisible for a specific reason:
  `json_field id` returned the *Pathshala's* id rather than the session's, and
  every check below it reported somebody else's service answering 404 while
  nothing pointed at the variable.

  `require_id` now stops the run at the extraction, naming what failed.

  **The item said the pattern was "everywhere in this script", and counting it
  found otherwise.** Of 31 extractions, 21 already had a check that reported and
  carried on - including the session id itself, which guards the subtler case by
  asserting the id it read is not the Pathshala's. Eight had nothing at all.
  Guarding only those is the whole change: adding a second guard to the other 21
  would have turned a counted failure into an abort, which is worse rather than
  louder.

  Verified by breaking one extraction - the run stops with one clear line
  instead of a screenful - and then clean from empty volumes at 294 of 294. The
  one place an empty id is normal is creating a Samaaj that already exists,
  which answers 409 with no id; that guard sits after the fallback that resolves
  it, not before.

  1,518 tests green, unchanged.
- **Previously:** 2026-09-03 - **the backup drill failed, and the backups were
  fine.**

  It was the last of the three hand-run scripts. Unlike the isolation probe it
  had not gone stale - it discovers its databases from `pg_database` rather than
  a hand-written list, so a new service's database appears on its own. What it
  had instead was a false-failure mode, and one that would fire on every run of
  a real deployment.

  It compares the restored copy against the **live** original. `pg_dump` takes a
  consistent snapshot; the count it is compared against is read afterwards, off
  a database that may still be moving. Nine databases were idle. The tenth was
  `audit_notification`, which consumes every event the platform publishes and is
  therefore never idle - it reported `audit_logs=26` against a restored `25` and
  announced "a database did not come back the same as it went in".

  Verified before concluding: the count settled at 26 once the consumer caught
  up, and a re-run against the now-quiet system passed all twenty checks. The
  dumps were never the problem.

  A mismatch is now re-checked against the original a second time. If the
  original itself moved between two reads, the difference says nothing about the
  dump and is reported as such rather than as a failure. Proved three ways: a
  quiet system still passes 20 of 20; under a real write every second, two
  databases report as moved and the run exits 0; and a genuine row loss on a
  static database still fails, so the fix has not simply made everything pass.

  **CI runs it**, last in the smoke job so the databases have real data — a
  drill against ten empty schemas proves the plumbing and nothing else. Verified
  end to end in CI's order from empty volumes: probe, then smoke 294 of 294,
  then the drill.

  1,518 tests green, unchanged.
- **Previously:** 2026-09-03 - **CI runs the isolation probe now.**

  It was complete and self-auditing and still only ran when somebody remembered,
  which is how it went stale in the first place. The smoke job already stands a
  stack up and tears it down, so the probe runs there, **before** the smoke
  script rather than after: that script's last section deliberately exhausts the
  credential rate limit with four hundred sign-in attempts, and anything signing
  in during the following minute gets a 429.

  Three things had to be true first, and none of them was.

  **The probe owned only one of its two Samaaj.** A defaulted to `smoke-samaj`,
  left over from when it looked A up rather than creating it. Sharing a Samaaj
  with the smoke suite means each can move a count the other asserts on; it owns
  `probe-samaj-a` and `probe-samaj-b` now, and the two scripts can run in either
  order without knowing about each other.

  **It assumed two Kafka consumers had caught up.** A member's profile is
  created by member-family-service consuming `identity.user.registered.v1`, and
  the welcome notification by audit-notification-service consuming the same
  event - so neither exists the instant registration returns. Against empty
  volumes that took out five fixtures, then one. Both are waited for now, the
  way the smoke script has always waited for the profile.

  **And three `grep`s in a pipeline could kill the script mid-setup** under
  `set -euo pipefail`, printing nothing after "building one of everything in
  Samaaj A". That is the third instance of that trap in this repository.

  Verified end to end in CI's order on one stack from empty volumes: probe 65
  refusals and complete coverage, then smoke 294 of 294.

  1,518 tests green, unchanged.
- **Previously:** 2026-09-03 - **the isolation probe covers everything now.**

  64 of the platform's 73 id-taking endpoints, 9 deliberately excluded with a
  written reason each, and nothing left over. 65 cross-tenant attempts, every
  one refused with 404 and none with 403. Re-run twice to confirm it is
  repeatable rather than passing off a first run's state.

  The seven closed this cycle were the most sensitive remaining: deciding who
  joins a household, converting a child to an adult account and approving that
  conversion, deciding a group application, repositioning a group's member,
  deciding a nomination, and reading another Samaaj member's notification. They
  needed a second member in Samaaj A, because A's first is the head of its
  family and the president of its group and cannot apply to either.

  **Three of the four fixtures failed first, all for the same reason: assuming a
  response shape.** Applying to a group answers `{groupId, applied, status}` and
  hands back no application id at all; nominating answers `candidateId`, not
  `id`; and a repeat join request is refused, so its id has to be read off the
  head's view of the household rather than the response. The fixture guard
  caught all three and refused to run - which is the guard doing exactly its
  job, since an empty id turns a probe into a request against a list endpoint
  that answers 200 for anybody.

  1,518 tests green, unchanged.
- **Previously:** 2026-09-03 - **the probe knows what it misses now.**

  Three cycles running, the thing that had gone wrong was a check going stale
  rather than code regressing: the accessibility audit predated eight screens,
  and the tenant-isolation probe had never touched 29 of the platform's 73
  id-taking endpoints. Both were found by looking, which is not a mechanism.

  So the probe audits its own coverage. It enumerates every id-taking route the
  services map - the same way `unreachable-endpoints.sh` does - and names the
  ones no probe reached. A new endpoint now shows up in that list on the next
  run instead of being quietly absent from a report that says "every
  cross-tenant attempt was refused".

  57 of 73 probed, up from 50; 58 clean refusals, no 403s, nothing unrefused.
  Nine are deliberately excluded and each carries a reason in the script -
  platform administration, where a Super Admin acting across Samaaj is the
  entire point, and the caller's own consent. **A list with permanent entries in
  it is a list people stop reading**, which is how this went wrong in the first
  place, so the exclusions are named rather than left to be re-argued each time.
  Seven remain genuinely outstanding and are listed in Phase 5.

  **Two more faults in the probe, both about assuming rather than
  establishing.** It waited on nothing before its first request, so a gateway
  that answers before the services behind it have migrated produced "could not
  sign in as Super Admin" - which reads as a wrong password. And the coverage
  report itself died silently on its first run: a `grep` that matches nothing
  exits 1, and under `set -euo pipefail` that killed the script after it had
  printed the heading and nothing else. `unreachable-endpoints.sh` carries a
  note about that exact trap; this script now does too.

  1,518 tests green, unchanged.
- **Previously:** 2026-09-02 - **the tenant-isolation probe had gone stale,
  and one endpoint had stopped being probed without saying so.**

  Same pattern as the accessibility audit two cycles ago: a check written at a
  point in time, and a platform that grew past it. 29 of the 73 id-taking
  endpoints were never touched by it - most of them the Pathshala teaching
  cluster, added months after the probe and never covered. 36 probes are 51 now,
  and all 51 refuse a cross-tenant attempt with 404.

  **Two faults in the probe itself, both of the kind that make a check useless
  while it still prints a summary.**

  It could not run on its own: it looked Samaaj A up and never created it,
  assuming `smoke-through-gateway.sh` had already made `smoke-samaj`. Against
  empty volumes it stopped at "could not sign in both members". A security check
  that silently depends on another script having been run first is one that gets
  skipped.

  And the body for `PATCH /v1/members/{id}` had gone stale - it omitted
  `isListedInDirectory`, which the command gained afterwards. Validation runs
  before the handler, so the request answered 400 and never reached the tenant
  check; the endpoint had stopped being probed. **Isolation was intact**,
  verified three ways with curl before drawing any conclusion: stale body 400,
  complete body cross-tenant 404, complete body on its own Samaaj 200. A 400
  now reports as `STALE ... NOT probed`, because a command will gain a required
  field again.

  1,518 tests green, unchanged. This cycle ran a check rather than adding one.
- **Previously:** 2026-09-02 - **a rule stated in prose, duplicated ten
  times, and enforced by nothing.**

  The plan was to audit boli-service, the next-lowest test ratio. It turned out
  not to need it: 57 source files against 38 tests looks thin until you notice
  most of those files are `Result`, `Error`, `ICommand` and the five behaviours
  copied verbatim into every service. Its domain - the window, the floor, the
  increment, idempotent closing, the two-step publish - is covered properly, and
  manufacturing tests to move a ratio would have been the wrong work.

  What the audit did turn up sits one level above any single service. Root
  `CLAUDE.md` §4.4 calls the MediatR pipeline order "fixed, load-bearing" and
  ends "Do not reorder these without updating this file and every service's
  `Program.cs` together" - a rule written in prose, duplicated across ten copies
  of one file, and checked by nothing at all.

  **`scripts/pipeline-order.sh` checks it now, and CI runs it.** The expected
  order is read out of §4.4 rather than hard-coded, so the documentation is the
  single source of truth and the check fails whichever side moves - verified
  both ways by breaking each in turn. All ten services agree today, so this
  finds no current bug; it is a guard on a security property, which is that
  tenant authorization runs before validation so an unauthorized caller never
  learns the validation rules for data they cannot see.

  **CI also now runs `scripts/unreachable-endpoints.sh`**, reported rather than
  failed - a listed endpoint is not automatically a bug. That sweep drove six
  cycles of work and was only ever run by hand.

  1,518 tests green, unchanged: this cycle added no tests, which is the honest
  outcome when the thing that was missing was a check rather than a test.
- **Previously:** 2026-09-02 - **the function that decides who wins an
  election had no tests.**

  Applying the gateway audit to the ten services put celebrity-voting bottom by
  a distance: 59 source files, 22 tests. Its aggregate was well covered - the
  windows, the forward-only sequence, who may see a running count - and its
  concurrency tests are the strongest on the platform. What nothing touched was
  `RankBy`, which lives in the application layer rather than the aggregate, and
  which `PublishResultsCommand` calls to produce the ranking it then freezes
  forever. 33 tests now.

  **One of the new tests passed for the wrong reason and was rewritten.** The
  tie-break is held by two mechanisms that agree: `Ballot` returns candidates in
  nomination order, and `RankBy` adds `ThenBy(NominatedAt)` on top of a sort
  LINQ already performs stably. Deleting either alone changes nothing - so the
  first version of the test, which created its candidates in order, passed
  whichever one was removed. It creates them out of order now, which
  distinguishes "one of the two is working" from "neither is". That is the most
  a test at this level can honestly claim, and the claim is written down rather
  than implied.

  1,518 tests green (1,017 backend across 21 suites, 501 frontend). No smoke
  run: nothing changed but tests.
- **Previously:** 2026-09-02 - **tests for the gateway's two untested files**,
  the last unexamined part of the request path.

  25 tests covered three of the gateway's nine source files. The two with none
  were `RedisTenantCache` and `RateLimiting`, and both hold a promise that only
  stands if something checks it. 46 now.

  **"A cache failure degrades to a cache miss, never to a failed request"** is a
  comment in `ITenantCache.cs`, and every request through the gateway resolves a
  tenant - so a throw there turns a Redis blip into a platform outage caused by
  an optimisation. Tested now for a read that throws, a write that throws,
  content that will not deserialise, and a disconnected multiplexer.

  **The smoke script proves the rate limit fires; it cannot prove it refuses the
  right caller.** Four hundred sign-in attempts show that *something* eventually
  says no. The unit tests cover the partitioning: one source cannot spend
  another's budget - a global bucket would hand an attacker a platform-wide
  denial of service rather than take one from them - sign-in cannot spend the
  registration budget, an unlimited route survives a burst against a limited
  one, and a refusal carries no body and no `Retry-After`.

  **One trap found while writing them.** `ResolvedTenant` is a record whose
  `EnabledModules` is a collection, so its generated equality compares that by
  reference: a tenant that has been through JSON is never `Be`-equal to the one
  it was serialised from. The first round-trip test failed for that reason and
  not for a real one. Written into `gateway/CLAUDE.md`.

  1,507 tests green (1,006 backend across 21 suites, 501 frontend). No smoke
  run: the gateway's own behaviour is unchanged, only its tests.
- **Previously:** 2026-09-02 - **tests for `libs/shared`**, the code both apps
  run on.

  The intent was to test the member portal's oldest screens, and that turned out
  to be unnecessary: every component in both apps is already covered. The gap
  was underneath them. `libs/shared` had 17 tests across two of its ten files,
  and the two with none at all were `tenant.interceptor` - which rewrites the
  URL of every request either app makes - and `token.store`, which holds the
  session. 61 tests now, and each new guarantee was checked by breaking it.

  **One of them is a check nothing else in the repository could make.**
  `module-keys.spec.ts` reads the gateway's `appsettings.json` and asserts that
  the client's module keys are exactly the ones the gateway gates routes on. A
  wrong key does nothing visible - the filter never matches, so a feature is
  missing from the portal for every Samaaj with nothing logged - and that has
  already happened once here, to the Events and Volunteer tiles on Home.

  **`money.ts` moved to `libs/shared` last week and its tests did not.** They
  were still in the member portal's Boli spec, describing a shared module from
  inside one of the two apps that use it. They live beside the code now, with
  the edge cases the move was a good moment to add: `1e5`, `.5` and `0` all
  parse the way they should.

  **A spec here is type-checked twice, and more strictly the second time.** Both
  app builds compile this library including its specs, so a `PLATFORM_ID`
  provider typed `object` satisfied vitest and then failed `ng test
  member-portal`. Written into `libs/shared/CLAUDE.md`, which this cycle also
  added - the library had none.

  1,486 tests green (985 backend across 21 suites, 501 frontend). No smoke run:
  nothing backend changed.
- **Previously:** 2026-09-02 - **a second accessibility pass**, because the
  first one had gone stale in a week.

  The 2026-09-01 audit predated the eight admin screens built after it, so those
  had never been looked at. Four findings came out of it, and every one of them
  also applied to screens the first pass *had* covered - which is the part worth
  remembering: an audit is a snapshot of the code on the day, and this
  repository was adding screens faster than that.

  **A confirmation panel that replaces its own trigger drops keyboard focus to
  the body** (WCAG 2.4.3). Three screens did it - publishing a Boli result,
  publishing a campaign result, standing a Pathshala down - by rendering the
  panel in an `@else`, so the button the user had just pressed left the DOM.
  Disabling the trigger instead is the same bug wearing a hat, since a disabled
  control is blurred and taken out of the tab order. Those panels were also
  silent to a screen reader and are now `role="status"`.

  **All 23 tables were unnamed**, and five screens draw three apiece. Each now
  has an `sr-only` caption.

  **Every screen in both apps went `h1` straight to `h3`.** Card titles are `h2`
  now, sub-headings inside a card stay `h3`, and both are given the size an `h3`
  rendered at before - checked in a browser against the built CSS at 18.72px, so
  nothing moved on screen. The member portal had the same defect and was fixed
  with it: a finding that applies to both apps is not one app's finding.

  Five tests hold the focus and caption fixes; breaking either fails exactly the
  test that claims it.

  1,450 tests green (985 backend across 21 suites, 465 frontend). No smoke run:
  nothing backend changed.
- **Previously:** 2026-09-02 - **the last five endpoints**. Every endpoint
  this platform maps is now reachable from an app.

  Volunteer groups (create with a president, stand one down), the social issues
  approval queue, and the two Pathshala ones. 126 of 127; the odd one out is
  `GET /v1/identity/tenants/by-id/{id}`, which is the gateway's and was never
  meant for an app.

  **One of the five was hidden by this repository's own documentation.**
  `apps/admin-portal/CLAUDE.md` said a Pathshala create form "would be a control
  that always answers 403" - true of a Samaaj administrator and wrong about the
  panel, which a Super Admin also uses, scoped into a Samaaj. A note explaining
  why an endpoint has no caller is a note that stops anybody asking again; this
  one was wrong for several cycles and the sweep only surfaced it once it
  learned about verbs.

  **The other four were the ordinary shape.** A group without a president has
  nowhere for its join requests to go, so the president is part of creating one
  rather than a later step. Standing a group down keeps its members and history
  and is reversible; standing a Pathshala down keeps every register and exam
  result and is not. And the issues queue is the mildest gap of the whole run,
  which the screen says: a reviewer holding an issue's id could already decide
  it - what was missing was any way to learn that something was waiting.

  **Six cycles of the sweep, and it found all of it.** 27 → 20 → 21 (when it
  stopped undercounting) → 14 → 10 → 6 → 1. Not one of these gaps was reported
  by a person or caught by a test; every service's own suite was green
  throughout.

  1,460 tests green (985 backend across 21 suites, 475 frontend). No smoke run
  this cycle: nothing backend changed, so there was nothing new for it to prove.
- **Blocking item:** none. **Every module has a service, member screens and an
  administrative view**, the role matrix is editable per Samaaj, and nothing the
  platform exposes is unreachable. What is left needs things this repository
  cannot supply on its own: TLS and a backup drill need a deployed environment,
  platform-hosted images need storage,
  the two remaining DPDP obligations - breach notification and the right to
  nominate - are no longer blocked on a channel but still need a real provider
  and, for s.8(6), a detection process and the Board form; and a screen-reader
  pass needs a person. The vote endpoint's throughput load test is
  carried to Phase 5, since it needs a deployed environment; its correctness
  half is done. The five questions in `docs/product/DPDP-COMPLIANCE.md` still
  need counsel before any of this ships to real users.

*(Update these three lines at the start/end of every work session —
they're what a coding agent or a teammate should read first to know
where things stand.)*

---

## Stage 0 — Walking Skeleton

Do this before starting any other service. The goal is one full
vertical slice working end-to-end, not broad partial progress.

- [x] Shared infra running locally (`docker-compose up`: Postgres,
      Redis, Kafka) - one logical database per service created on first
      start by `infra/postgres/init-databases.sh`
- [x] `identity-tenant-service` scaffolded (`.claude/skills/new-microservice`)
  - [x] `Tenant` aggregate + `CreateTenantCommand` + `GetTenantBySlugQuery`
  - [x] `User` aggregate + `RegisterMemberCommand` + `LoginCommand`, plus
        seeded roles/permissions, `/me`, tenant activation and a configurable
        Super Admin bootstrap
  - [x] `dotnet build` / `dotnet test` pass (101 unit, 34 integration)
- [x] `audit-notification-service` scaffolded - regex-subscribed consumer
      recording every platform event into an append-only audit log, plus
      member notifications. Verified with Testcontainers Kafka, not a fake.
- [x] Gateway: subdomain → tenant resolution + JWT validation wired
      for `/v1/identity/**`, `/v1/audit/**` and `/v1/notifications/**`,
      with the module feature-flag gate and inbound tenant-header stripping
- [x] `apps/member-portal` shell created with the shared interceptors in
      `libs/shared` (npm workspace; Angular workspace root is the repo root)
- [x] Login / Register / Home screens ported from
      `docs/product/wireframes/member-portal-wireframes.html`
      (`.claude/skills/wireframe-to-angular`), calling real endpoints
- [x] **End-to-end proof:** register → select a Samaaj → log in → Home
      renders with that Samaaj's name, the member's name, the welcome
      notification the Kafka consumer raised, and only the modules that
      Samaaj has enabled. Verified in a browser against the compose stack.
      The subdomain redirect is implemented but exercised only by unit test
      locally, since `localhost` has no subdomains.
- [x] CI running build + test on every push (`.github/workflows/ci.yml`):
      per-service .NET build and test, the two frontend suites, a check that
      no EF model has drifted from its migrations, and the gateway smoke test
      against a stack built from scratch

> The through-the-gateway coverage CLAUDE.md §9 requires is
> `scripts/smoke-through-gateway.sh`, run against the compose stack and in CI.

**Exit criteria:** the Platform Foundation acceptance criteria in
`docs/product/requirements/samaajconnect-product-requirements.docx`
§11 are demonstrably true against running code, not just individually
unit tested.

## Phase 1 — Platform Foundation (remainder)

- [x] `member-family-service` scaffolded - profile created from the
      `identity.user.registered.v1` event, no synchronous call between the
      two services
  - [x] Profile update flow, with per-field privacy levels
  - [x] Family create / join-request / decide
  - [x] Child profile create
  - [x] Adult-child conversion request + approval flow, admin-approved. The
        member-family half is done and announces
        `members.child-conversion.approved.v1`; nothing consumes it yet, so
        the login is not created - see below.
- [x] identity-tenant-service consumes `members.child-conversion.approved.v1`
      and creates the login. A Samaaj admin issues a one-time activation code
      (shown once, stored as a hash) and hands it over in person; redeeming it
      sets the first password and closes the loop back to member-family-service.
      A channel now exists that could carry the code instead, but sending it
      needs a real provider first - a code delivered to a log is a code handed
      over in person with extra steps
- [x] DPDP Act: the compliance mapping (`docs/product/DPDP-COMPLIANCE.md`),
      versioned consent notice, per-purpose append-only consent records
      captured at registration, withdrawal, and per-service data export
- [x] DPDP Act: parental consent required to create a `ChildProfile` (s.9),
      data exports from all three services (s.11), and a published grievance
      contact per Samaaj (s.13)
- [x] DPDP Act: the right to erasure (s.8(7), s.12) - `POST /v1/identity/me/erase`,
      password-gated and with no admin in the way, fanning out over
      `identity.user.erased.v1` to clear the profile, the children held on that
      member's parental consent and the household link, delete their
      notifications, and de-identify rather than delete their audit rows
- [x] An outbound notification channel - `INotificationChannel`, a dispatcher
      that claims, retries and gives up, and a delivery record per message.
      The adapter behind it writes to the log and delivers nothing, so `Sent`
      means "handed to the channel", which the service says at Warning on
      every start. Registration now sends a welcome to the identifier the
      member signed up with, proving the path end to end
- [x] The notification endpoints the API contract promised and nothing
      implemented: mark one read, mark all read, and a Samaaj-wide announcement
      with the recent-announcement list beside it. Read state moved off the
      notification and onto a row per person - a broadcast is one row a whole
      Samaaj shares, so a read flag on it was marked by the first member to open
      it and read for everyone after. `NotificationStatus` lost `Read` with it
      and is now purely about delivery. Screens in both apps; the member
      portal's unread badge counted the whole list until now
- [x] The member portal's **My Profile** screen. The welcome notification every
      registration raises says "complete your profile", `PATCH /v1/members/{id}`
      existed, `MembersApi.updateMe` existed, and no screen called either - so a
      member could be told to do something the app gave them nowhere to do.
      Basic details, the five privacy levels beside them, and the wireframe's
      "Profile listed in directory" checkbox, which had no field behind it until
      now: per-field privacy cannot take a member out of the directory, because
      a listing is a name. It hides them from the directory search and from
      nothing else - a profile stays reachable by id, which is what group
      applications and post authorship need
- [x] The admin panel's **Timeline / Content Moderation** screen. Nothing on the
      platform could approve a post before it: a member writes one, it lands
      `PendingReview`, and the only way it ever reached the Samaaj's timeline
      was somebody curling the moderate endpoint. Both that endpoint and the
      queue existed; no screen in either app called either. The buttons come
      from `TimelinePost.AvailableDecisions` rather than from the status, so a
      state added to the domain cannot leave the panel offering the wrong ones
- [x] Redeeming an activation code, in the member portal at `/activate`. Three
      screens in the admin panel told people to do this there - the invite
      screen, the admin sign-in screen and the conversion queue - and the member
      portal had nowhere to do it, so **no invited administrator could ever sign
      in and no converted adult child could ever get an account**. The endpoint
      was complete and covered by the smoke script through curl
- [ ] A real email or SMS provider, so `Sent` means a person was reached.
      One class implementing `INotificationChannel` and one registration; the
      choice of provider is a hosting decision
- [ ] DPDP Act, remaining: breach notification (s.8(6)) and the right to
      nominate (s.14). Neither is blocked on a channel any more. s.8(6) still
      needs a provider, a way to address every affected member at once, the
      Board form, and the detection that starts it; s.14 needs a nominee field
      and counsel on what a nominee may do
- [x] DPDP Act: a member-portal surface for consent withdrawal, data export and
      erasure, at `/privacy`. No wireframe covers it — the prototype's
      `#profile` screen is per-field directory privacy, which is a different
      thing — so the screen was designed against the Act rather than translated.
      Withdrawing is one click with no confirmation, because s.6(4) requires it
      to be as easy as giving and giving was a tick; erasing asks for the
      password, because it cannot be undone. What erasure keeps is printed
      beside what it erased. The copy is assembled in the browser from the three
      services that hold it, since the platform deliberately has no single
      export endpoint. Driven end to end against the running stack, including a
      real erasure of a throwaway account
- [x] Admin backend: Super Admin tenant list (`GET /v1/identity/tenants`), a
      closed `ModuleCatalog` with runtime toggles, the role and
      permission matrix, listing administrators, inviting one with a one-time
      activation code, and granting/revoking a role
- [x] Admin portal: the Angular SPA itself — sign-in, the Samaaj list with
      status and module toggles, Create Samaaj, administrators with role
      assignment, Invite Admin with its one-time code, the role
      matrix, the adult-child conversion queue, and the audit log. Screens with
      no service appear in the nav, disabled, saying why
- [x] Admin: an editable role and permission matrix, per Samaaj. The three
      preconditions `ListRolesQuery` named all exist now:
      `RolePermissionOverride` records only where a Samaaj departs from the
      platform defaults (so one that changes nothing keeps tracking those
      defaults, and one that undoes a change has its override deleted rather
      than pinned); `identity.role-matrix.changed.v1` carries who changed what
      and what it was before; and `MatrixEditing` is the floor - SuperAdmin
      cannot be edited by a Samaaj, and a Samaaj Admin cannot lose
      `Roles.Manage`, which is the one revocation a Samaaj could not undo for
      itself. Gated on its own key rather than `AdminUsers.Manage`: inviting an
      administrator hands somebody an existing bundle, this redefines it
- [x] `docs/product/SECURITY-CHECKLIST.md` pass on both Stage-0 +
      Phase-1 services. Every box walked against the code; the file now records
      what is asserted, what is missing and where each gap is tracked. Added on
      the way: a `TenantWriteGuard` that refuses a cross-tenant write at
      `SaveChanges` whatever the handler did, per-source rate limits at the
      gateway, before-state on corrections and status changes, audit events for
      data exports, and validation of the photo links that were previously only
      length-checked
- [x] Session revocation: refresh tokens stored as hashes, single-use and
      rotating, with reuse treated as theft and the whole chain revoked;
      `POST /v1/identity/logout` ending one session or all of them; erasure
      ending every session; and the access token down from 60 minutes to 15.
      Refreshing re-reads the account, its Samaaj and its roles, so a
      suspension or a revoked role bites within one token lifetime. Both
      portals renew silently rather than sending members to the login screen
- [x] Step-up authentication on deactivating a Samaaj — and on archiving one,
      which is the only status change that cannot be undone. Shared with
      erasure through `IStepUpAuthentication`. Two things came out of doing it:
      a failed step-up must answer 403 rather than 401, because the portals'
      interceptor renews the token on a 401 and *retries the original request*,
      which on these endpoints would resubmit the destructive command after a
      typo — erasure had this defect and now does not; and the step-up has to
      read the account past the tenant query filter, since a Super Admin's own
      account sits at `PlatformTenantId` while they act on a Samaaj

## Phase 2 — Social & Community Engagement

- [x] `timeline-service` (feed + moderation queue) — posting with the member/announcement split, the moderation queue that reported posts rejoin, comments, reactions and reporting. The platform's first module-gated route: switching `community` off makes the whole area answer 404
- [x] `volunteer-groups-service` — groups, the join-application flow, and the president's review queue. Introduced `VolunteerGroups.Lead`, a permission every member holds, because gating a president's own group on an admin permission made those endpoints unreachable
- [x] `events-service` (with capacity/waitlist) — draft/publish/cancel, RSVP and a waitlist that actually moves: giving up a place promotes whoever waited longest, and a promoted member keeps their queue position
- [x] `social-issues-service` (full Draft → Published workflow) — eight states declared as a transition table, with publishing reachable only from Approved, and an append-only history that answers "why was mine rejected?". The first service on its own module key
- [x] **Member portal: Timeline** (wireframe `#timeline`) — the feed with its
      three real states, composing, reactions, comments and reporting. The
      first member-facing screen for any service beyond sign-in, and the one
      that establishes the pattern the rest follow: a feature folder with its
      own `*.api.ts` and `*.models.ts`, wire types mirroring the service's
      responses, and per-item errors kept off the page-level error
- [x] **Member portal: Events** (wireframes `#events` and `#eventdetail`) - the
      list with the two states the wireframe drew and four it did not
      (cancelled, already going, already waiting, no capacity limit), and the
      detail screen with the real capacity bar. RSVP and joining the waitlist
      are one button and one call, because which of the two a member gets
      depends on a count the portal cannot see the current value of. Verified
      against the stack: RSVP, join a queue, and watch a place move down it
- [x] **Member portal: Volunteer Groups** (wireframes `#groups` and
      `#groupdetail`) - the directory with the reader's standing on each card,
      the detail screen with the apply flow, and **the president's review
      queue**, which no wireframe covers but without which every application a
      member sends sits unanswered forever. The queue is fetched only when the
      group says the reader leads it, because the endpoint answers 404 to
      anyone else. Verified against the stack: applied as a member, accepted
      with a position as the president
- [x] **Both Angular apps are containerised.** Neither had a Dockerfile, so
      neither could be deployed - the portals only ever ran from `ng serve` on
      a developer's machine. member-portal is an SSR Node image on 4200 whose
      server proxies `/v1` to the gateway, and the gateway also serves it at
      the root as the public front door; both are same-origin, which is what
      its production `gatewayUrl: ''` needs. admin-portal is a static build behind
      nginx on its own origin, proxying `/v1` to the gateway - deliberately not
      sharing an origin, because both apps use the same sessionStorage token
      keys and one origin would mean one session. Smoke checks cover the root,
      a deep link and `/health` not being swallowed
- [x] **Member portal: Social Issues** (wireframe `#issues`, plus a detail
      screen no wireframe covers) - the submission form, the published list,
      and "My Submissions" with the wireframe's progress strip. The strip is
      drawn only for the four states on the happy path; Rejected,
      ChangesRequested and Closed are where an issue leaves it, so those say so
      in words rather than showing an issue as partway to a publication it is
      not heading for. Every workflow button comes from the service's
      `availableTransitions`, so the portal holds no copy of the eight-state
      table. The detail screen surfaces the reviewer's reason at the top, which
      is what the append-only history was built to answer
- [x] **Member portal: Members and Family** (wireframes `#members`,
      `#memberdetail`, `#family`, `#children`) - the directory, one member's
      privacy-filtered profile, and the household with its children as one
      screen. Building it found that `GET /v1/members/{id}` had been in
      API-CONTRACTS.md since the contract was written and never implemented, so
      that shipped too, through the same per-field privacy mapper the directory
      uses. Adding a child shows the DPDP notice before the form and sends its
      version with the consent
- [x] **Member portal: Celebrities of Samaaj.** The campaign list and one
      detail screen carrying the wireframe's ballot and its results table,
      because they are the same campaign at two points in its life and a member
      arriving after publication wants the result where the ballot was. Every
      control reads `acceptsNominations`/`acceptsVotes` rather than the status,
      so a window that has closed cannot still offer a button. Driving it
      against the running stack found a **gateway** bug, not a portal one: a
      module-gated route answered 404 to a caller whose access token had merely
      expired, so the portals' renew-and-retry never fired and every gated
      screen said "No such endpoint." fifteen minutes after sign-in, for good
- [x] **Member portal: Jain Pathshala.** The directory with a parent's enrol
      request, and one enrolment screen carrying the wireframe's `#myclass`,
      `#attendance`, `#exams` and `#progress` — four views of one enrolment,
      whose own `#progress` already reprints the attendance percentage from
      `#attendance`. Waiting for a place is a first-class state: the screen
      reads `classId` and asks for neither the class nor the exams until there
      is one, because `my-class` answers 409 by design while a child is
      unplaced. The wireframe's "Events: 7 participated" tile is dropped —
      nothing records Pathshala event participation, so it would be a number
      the app made up
- [x] **Member portal: the rest.** Boli shipped as the tenth service with its
      list, detail and occasion screens

## Phase 3 — Celebrity Voting

- [x] `celebrity-voting-service` — nominations with an approval step, one vote per member, and a result that is frozen when announced. The double-voting guarantee is a unique index on `(CampaignId, VoterMemberId)`, not the handler's check and not a Redis lock; the vote is written on its own scope so voters are not serialised by the request's transaction
- [ ] Load-test the vote-cast endpoint specifically (highest
      concurrency write path on the platform — see
      `docs/product/ROADMAP.md`). The **correctness** half is done:
      `ConcurrentVotingTests` proves twenty racing requests from one
      member leave exactly one vote, and `VoteIndexTests` proves the
      index is what refuses the second. What remains is throughput
      under sustained load, which needs a deployed environment rather
      than a Testcontainer — carried to Phase 5 hardening

## Phase 4 — Jain Pathshala

- [x] `pathshala-service` — the school, its sessions and classes, a two-step enrolment, the register and exams. Enrolment is a parent's request and a Pathshala's placement, because the Pathshala picks the class and because placing is the only check this service can make that a child is the caller's. Attendance is held to one mark per child per class day by a unique index; the register is written on one connection outside the request transaction, after two mistakes documented in the service's own `CLAUDE.md`
- [x] Teacher and Student "My…" views built in parallel (shared
      underlying data) — the backend for both. My Class, My Attendance,
      My Exams and My Progress are one set of queries over the same
      tables, gated on `Members.Read` and decided against the enrolment
      rather than on the `PathshalaStudent` role, which nothing grants.
      Progress is computed rather than stored, so a corrected mark
      cannot leave it quietly wrong
- [x] The Angular screens for both, from the wireframes' `#myclass`,
      `#attendance`, `#exams` and `#progress` - built as one enrolment screen

## Phase 5 — Boli + Hardening

- [x] `boli-service` — occasions, Boli types, bidding, and a result that
      is recorded before it is announced. The correctness requirement here
      is not "one each" as in celebrity voting but **one highest**: a row
      lock on the Boli serialises bidders (deliberately the opposite of the
      vote path, which avoids serialising them — here it is the point),
      and a unique index on `(BoliId, Amount)` is what holds if a future
      code path forgets the lock or the service runs on two instances.
      Being outbid answers 200 with `accepted: false` and the amount now
      needed. Amounts are integer paise. The bid history never names who
      bid, and a recorded result names nobody until it is published
- [x] Member portal: the Boli screens - the hub, one Boli with its bid form
      and history, and an occasion screen the wireframe's "View Occasion"
      button had nothing behind it. Money is integer paise converted in one
      place; being outbid is an info notice with the new minimum already in the
      field, not a red error; and "You are leading" is only said while bidding
      is open, because on a closed-but-unpublished Boli it would announce the
      winner before the Samaaj did
- [x] Full `SECURITY-CHECKLIST.md` pass across every service. The previous pass
      covered three; six of the seven that shipped after it had never been
      re-checked. The mechanical properties held everywhere — all ten apply the
      tenant query filter by reflection, call `TenantWriteGuard` at
      `SaveChanges`, register the five behaviors in order, and fail closed on an
      unannotated request. Two findings, one fixed and one tracked below
- [x] **Fixed: the step-up was an unthrottled password oracle.** `/me/erase`
      and the tenant-status endpoint check a password and counted nothing, so
      anyone holding a borrowed access token could brute-force the account
      password at full speed without ever tripping the login lockout — turning
      the fifteen-minute window the stateless-token design accepts into a
      permanent credential compromise. `StepUpAuthentication` now shares the
      login lockout, and both paths carry the gateway's `credential-attempts`
      policy
- [x] **Fixed: erasure now reaches the two services holding free text.**
      `timeline` and `social-issues` consume `identity.user.erased.v1`, emptying
      the posts, comments, issues and reasons an erased member wrote while
      leaving other people's records — comments, reactions, reviewer decisions —
      standing. Verified through Kafka against the running stack
- [x] **Fixed: six services shared one Kafka consumer group.** All carried
      `"GroupId": "timeline-service"` from being scaffolded off it. Only one ran
      a consumer, so nothing had broken; adding the two above would have had
      erasure events delivered to a service that ignores them and committed
      away, intermittently by partition assignment. The group id now lives only
      in each service's `ConsumerOptions`, where a copied `appsettings.json`
      cannot reach it
- [ ] **Counsel: is a bare `MemberId` still personal data after erasure?**
      `events`, `volunteer-groups`, `celebrity-voting` and `boli` hold one and
      no name or contact. Two of them cannot drop it regardless — the voter id
      is the double-voting guarantee, and a bid is a financial record. Question
      6 in `DPDP-COMPLIANCE.md`
- [x] **Full `SECURITY-CHECKLIST.md` re-pass, 2026-09-03**, the erasure gap it
      was waiting on having closed. The mechanical half is now
      `scripts/security-invariants.sh`, which CI runs: every request type
      carries one of the four authorization attributes, the anonymous and
      internal sets match the lists written on the checklist itself, no route
      reaches an internal command, every `DbContext` filters by reflection,
      every service calls `TenantWriteGuard`, and the eleven files meant to be
      identical across all ten services are identical. Three findings, all
      recorded on the checklist: a drifted `KafkaProducer` default `ClientId`,
      a test cited by name that does not exist, and a probe count that had been
      wrong for several cycles
- [ ] HTTPS-only in production: TLS termination, HSTS, secure-cookie policy,
      and `ForwardedHeaders` so the gateway rate limiter partitions on the real
      caller rather than on the proxy
- [x] **Platform-hosted images: member and child photos, 2026-09-04.**
      `PhotoUrl` was a client-supplied link, so every viewer fetched the picture
      from whatever host it named — third-party tracking of children on a
      `ChildProfile`, which DPDP s.9(3) prohibits. `StoredImage` holds the bytes
      in member-family-service's own database, behind `IImageStore` so an object
      store is one implementation away; the type is read from the bytes and
      never from the upload's header; SVG is refused; 2 MB, checked three times.
      Authorization is the profile's own rule, which is why the owning service
      serves the bytes. Both portals fetch through `[scAuthedSrc]`, because an
      `<img src>` carries no token
- [x] **Samaaj logos, 2026-09-04.** `TenantLogo` in identity-tenant-service,
      the same shape as `StoredImage` with one deliberate difference: the read
      is **anonymous**, because the registration form asks somebody to pick
      their Samaaj before they have an account. It is the only image on the
      platform outside per-request authorization and
      `SECURITY-CHECKLIST.md` says so rather than letting the member-photo tick
      imply otherwise. `LogoUrl` turned out to be a field nothing could ever
      set — no command took one — beside an "Upload Logo" control the wireframe
      had drawn since the start
- [ ] **Virus scanning**, which is what keeps the file-handling box unticked.
      Sniffing proves the bytes begin like an image and says nothing about what
      a decoder does with the rest of them; closing it needs a scanner in the
      deployment rather than a check in a domain type. Post media and
      social-issue evidence remain not uploadable at all
- [x] Tenant-isolation penetration testing (attempt cross-tenant IDOR
      on every write endpoint) — `scripts/tenant-isolation-probe.sh`. Two real
      Samaaj; B's member and B's administrator attempt reads and writes
      against A's ids through the gateway, across all ten services. All
      refused with 404, none with 403. The script proves its own paths first,
      after a first run scored three false passes against endpoints that did
      not exist at the path it used
- [x] **The probe audits its own coverage now, 2026-09-03.** It enumerates every
      id-taking route the services map — the same way `unreachable-endpoints.sh`
      does — and names the ones no probe reached. It cannot silently go stale
      again: a new endpoint appears in that list on the next run. 57 of 73
      probed, 9 deliberately excluded with a written reason each (platform
      administration, where a Super Admin acting across Samaaj is the point),
      and 7 genuinely outstanding
- [x] **CI runs the probe, 2026-09-03**, in the smoke job's existing stack and
      before the smoke script — whose last section exhausts the credential rate
      limit, so anything signing in after it gets a 429. Needed the probe to own
      both its Samaaj rather than borrowing `smoke-samaj`, to wait for the two
      Kafka consumers that create a profile and a welcome notification, and to
      stop three pipeline `grep`s killing it under `pipefail`. A check nobody
      runs is not a check, which is how this one went stale to begin with
- [x] **The last seven are probed too, 2026-09-03. Coverage is complete:** 64 of
      73 id-taking endpoints, 9 deliberately excluded with a reason each, none
      left over. 65 cross-tenant attempts, all refused with 404, no 403s. The
      seven were the most sensitive ones remaining — deciding who joins a
      household, converting a child to an adult account and approving that
      conversion, deciding a group application, and reading somebody else's
      notification — and needed a second member in Samaaj A, since A's first is
      the head of its family and the president of its group
- [x] **Fixed: the probe assumed the stack was ready.** It waited on nothing and
      its first request failed, reporting "could not sign in as Super Admin" —
      which reads as a wrong password rather than a gateway answering before the
      services behind it had migrated. It has `wait_for_stack` now, the same as
      the smoke script, which learned this first
- [x] **Re-run and extended on 2026-09-02, because it had gone stale.** 36
      probes had become 51: the Pathshala teaching endpoints — the register,
      the roll, the timetable, exam results, placing and withdrawing a child,
      and a child's own attendance and progress — were added months after the
      probe was written and it had never touched any of them. 29 of the
      platform's 73 id-taking endpoints were unprobed. All 51 now refuse with
      404, including every new one
- [x] **Fixed: the probe could not run on its own.** It looked Samaaj A up and
      never created it, on the assumption that `smoke-through-gateway.sh` had
      already made `smoke-samaj` — so against empty volumes it stopped at "could
      not sign in both members". A security check that depends on another script
      having been run first, in the right order, is one that will be skipped. It
      creates both Samaaj now
- [x] **Fixed: one endpoint had silently stopped being probed.** The body for
      `PATCH /v1/members/{id}` omitted `isListedInDirectory`, which
      `UpdateProfileCommand` gained after the probe was written. Validation runs
      before the handler (§4.4), so the request answered 400 and never reached
      the tenant check at all. Verified three ways with curl before concluding
      anything: stale body 400, complete body cross-tenant 404, complete body on
      its own Samaaj 200. **Isolation was intact** — the probe was not. A 400 now
      reports as `STALE ... NOT probed` rather than as an ambiguous failure,
      because the same thing will happen again the next time a command gains a
      required field
- [x] **Fixed: `PATCH /v1/members/{id}` answered 500 when `privacy` was
      omitted.** `PrivacySettings Privacy` is a non-nullable reference type,
      which is a compile-time claim only — the JSON deserialiser leaves it null,
      and the validator's sub-rules dereferenced it. A `NotNull` rule above them
      does not stop the rules after it; they needed a `When`. It hit any caller,
      including a member editing their own profile, and was found incidentally
      by the isolation probe
- [x] Accessibility pass (WCAG 2.1 AA) on both Angular apps. Found three real
      things: neither app had a `<main>` landmark or a skip link (2.4.1, level
      A); Home's module tiles were `<button>`s calling `router.navigateByUrl`,
      so they announced as buttons and could not be opened in a new tab; and
      nothing moved focus on navigation, so a screen reader announced nothing
      when the page changed. All three fixed, plus a `prefers-reduced-motion`
      block. The palette was measured rather than assumed and passes AA
      everywhere — tightest pair 4.56:1. What each app's `CLAUDE.md` now records
      is what was checked, so the next pass does not start over
- [x] **`scripts/pipeline-order.sh`, 2026-09-02**, and CI runs it alongside the
      endpoint sweep. Root `CLAUDE.md` §4.4 calls the pipeline order "fixed,
      load-bearing" and asks that it be changed in eleven places at once; until
      now nothing checked that it had been. The expected order is read out of
      §4.4, so the doc is the source of truth and drift on either side fails.
      All ten services agree today — this is a guard, not a fix. Auditing
      boli-service, which prompted it, found nothing worth adding: its ratio is
      thin only because most of its files are the `Result`/`Error`/behaviour
      boilerplate every service copies
- [x] **`RankBy` has tests, 2026-09-02.** The same audit applied to the ten
      services put celebrity-voting bottom by a distance — 59 source files, 22
      tests — and the gap was the function that decides who is named the
      celebrity of a Samaaj. It sits in the application layer rather than the
      aggregate, which is how it escaped a test file named for the campaign.
      Ordering, ties, candidates nobody voted for, nominations that are not on
      the ballot, and the null-versus-zero rule the admin panel draws off. The
      other nine services' ratios are all reasonable; this was the outlier
- [x] **The gateway's two untested files have tests, 2026-09-02.** 25 covered
      three of nine; `RedisTenantCache` and `RateLimiting` had none. Both hold a
      promise that only stands if something checks it — a cache failure must
      degrade to a miss rather than fail a request, and the rate limit must
      partition per source rather than globally. 46 tests now. The smoke
      script's four-hundred-attempt burst proves the limit fires; it cannot
      prove it refuses the right caller, which is what these add
- [x] **`libs/shared` has its own tests, 2026-09-02.** It had 17 across two of
      ten files; the tenant interceptor, which rewrites the URL of every request
      either app makes, and the token store, which holds the session, had none.
      61 now. The one worth naming is `module-keys.spec.ts`, which reads the
      gateway's `appsettings.json` and asserts the client's module keys are the
      ones the gateway gates routes on — the only check here that can fail when
      those drift, and a drift that is otherwise silent. `money.ts`'s tests
      moved from the member portal's Boli spec to sit beside the code, where
      they should have gone when the module did. The library also got a
      `CLAUDE.md`, which it had never had
- [x] **Second accessibility pass, 2026-09-02**, because the first predated the
      eight admin screens built after it. Four findings, all of which also
      applied to screens the first pass *had* covered - which is the point worth
      keeping: an audit is a snapshot, and this one had gone stale in a week.
      A confirmation panel that replaced its own trigger dropped keyboard focus
      to the body on three screens (2.4.3), and disabling the trigger instead is
      the same bug; those panels were also silent to a screen reader and are now
      `role="status"` (4.1.3); all 23 tables were unnamed and now carry an
      `sr-only` caption (1.3.1); and every screen in **both** apps skipped from
      `h1` to `h3`, so card titles are now `h2` with no visible change - checked
      in a browser against the built CSS. Five tests hold the first two
- [ ] Accessibility: a pass with a real screen reader, and keyboard-only
      walkthroughs of the longer workflows (the Boli bid form, the issue
      transitions, the role matrix). Those need a person, not a script
- [x] Backup/restore drill — `scripts/backup-restore-drill.sh`. Dumps all ten
      logical databases, restores each into a `_drill` copy, and compares row
      counts per table and the unique indexes that are correctness guarantees.
      Never touches the live databases, so it is safe to run against a running
      system — a drill that can only be run somewhere safe is a drill nobody
      runs. 20 checks, all passing, and both kinds of check were shown to fail
      when a row or an index is removed from the restored copy
- [x] **Fixed 2026-09-03: it reported a false failure on a live system, and CI
      runs it now.** It compared the restored copy against the *live* original —
      fine for nine databases, wrong for `audit_notification`, which consumes
      every event the platform publishes and is never idle. A run taken while
      its consumer was catching up reported `audit_logs=26` against a restored
      `25` and said a database had not come back the same. The dumps were fine;
      the original had moved. On a real deployment that would have been the
      normal result, and a drill that cries wolf is a drill nobody reads. A
      mismatch is now re-checked against the original: if the original itself
      moved between two reads, it is reported as uncomparable rather than as a
      failure. Shown to still fail on a genuine row loss from a static database,
      so the fix did not simply make everything pass. Runs last in CI's smoke
      job, where the databases have real data in them
- [ ] **Backups are not deployed, only proven restorable.** The drill writes
      dumps next to the database they came from, which protects against nothing,
      and full dumps mean the recovery point is whenever the dump ran. A real
      deployment needs WAL archiving for point-in-time recovery, off-host
      storage, and a schedule. All three are hosting decisions
- [x] Four wrong checks in `scripts/smoke-through-gateway.sh`, found by running
      it against a stack built from empty volumes rather than trusting the
      count. None was a service bug:
      the Pathshala session id was read with `json_field id` off a response
      whose first `id` is the *Pathshala's*, so classes were created in a
      session that did not exist and **17 checks failed pointing at the wrong
      service** while the check above them passed;
      the second-vote check voted for a different candidate and so was refused
      as a self-vote, never once exercising the already-voted path it is named
      after; the self-vote check used the wrong member's token, asserting 409
      on a vote that was correctly accepted — and quietly casting a second vote
      into a campaign the next checks tally;
      and the republish check asserted 409 against a handler that deliberately
      returns the stored result, so it asserted the opposite of the documented
      behaviour. It now compares the frozen ranking instead, which is what its
      own comment was about, and ignores `publishedAt` — the first response
      carries .NET's 100ns timestamp and the second Postgres's microseconds,
      a round-trip artifact rather than a result that moved
- [x] `scripts/unreachable-endpoints.sh` — lists every endpoint the services map
      that neither app calls. Three cycles running, that list is where the next
      thing worth building turned out to be: the profile screen the welcome
      notification points at, timeline moderation without which no post could be
      approved, and redeeming an activation code, which three admin screens told
      people to do "in the member portal" while the member portal had nowhere to
      do it. All three were complete, tested, and reachable only by curl
- [x] **The sweep was itself undercounting, in three ways, and now is not.** It
      matched on the path alone and printed the verb without ever comparing it,
      so any endpoint sharing a path with a called one of a different method
      looked reached — `DELETE /v1/pathshala/pathshalas/{id}` hid behind the
      detail screen's `GET`, and creating an event, a Boli occasion, a voting
      campaign or a volunteer group each hid behind its own list. It also counted
      a path mentioned in a **doc comment** as a caller, which this repo writes
      constantly, and a path in a **spec file** — an endpoint with a test and no
      screen is the exact thing the sweep exists to find, so a test must not
      count as a caller. Fixed together: verbs are matched, comments and specs
      are excluded, and a path whose verb genuinely cannot be determined (a
      helper taking it as an argument) is recorded as reached by every method
      rather than as a gap for all of them. The count went from a reported 20 to
      a true 21 in the same cycle that built seven screens
- [x] **Closed, and it was not a bug: the "tenant filter that did not exclude
      another Samaaj's row".** Reported here on 2026-09-02 as unexplained and
      security-relevant. It was wrong. The filter is correct, verified four ways:
      against the deployed stack with the handler's extra tenant check removed
      (only the caller's own Samaaj comes back); with a three-tenant probe in the
      test host, also with that check removed (A sees A, B sees B, an unrelated
      Samaaj sees nothing); and by `ChildNamesTests` passing 7 of 7 with the
      check removed. The original failure came from reading results off a build
      that still contained a deliberately broken `ListByIdsAsync` from a
      fault-injection experiment, and attributing them to the restored code.
      The lesson - verify the restore, and re-run the unmodified case, before
      concluding anything from the next failure - is written up in
      `services/member-family-service/CLAUDE.md`. The handler keeps its explicit
      tenant check as belt and braces for a read whose ids come from another
      service, not as a workaround
- [x] **Every endpoint the platform maps is now reachable from an app**, bar
      one that is not a gap: `GET /v1/identity/tenants/by-id/{id}` is the
      gateway's, not an app's. 126 of 127. The sweep went 27 → 20 → 21 (when it
      learned about verbs and stopped undercounting) → 14 → 10 → 6 → 1 over six
      cycles, and every cluster below was found by running it rather than by
      anybody noticing:
  - [x] **Pathshala administration, setting it up (5 of 13).** The Pathshala
        detail, opening a session, adding a class, the enrolment request queue
        and placing a child. A parent asking for a place now has somebody who
        can answer, which was the dead end
  - [x] **Pathshala administration, teaching (7 of 8).** The class screen:
        teachers, timetable, roll, register, exams, results and withdrawing a
        student. Deactivating a Pathshala is the one left, and is now visible in
        the sweep for the first time — see below
  - [x] **The two Pathshala endpoints (2).** Standing one down, behind a
        confirmation on the detail screen, and creating one - which this panel
        had documented as deliberately absent because it "would always answer
        403". True of a Samaaj administrator and wrong about the panel: a Super
        Admin uses it too. The form now appears for that role with a Samaaj
        selected
  - [x] **Boli administration (7).** Two screens: occasions with the
        publication queue, and an occasion's types, Boli, closing and recording.
        Needed one new endpoint — `GET /v1/boli/results/pending`, the middle of
        the deliberately two-step record-then-publish workflow, which nothing
        could list. boli-service also had no gateway smoke coverage at all,
        which root `CLAUDE.md` §9 asks for on every service
  - [x] **Events administration (4).** Two screens: the list with drafts,
        create, publish and cancel; and an event's attendees split into going,
        waiting and gave up a place. The first cluster in three cycles that
        needed no new endpoint — all four existed and simply had no caller, and
        events-service already had full gateway smoke coverage
  - [x] **Celebrity voting administration (4).** Two screens: campaigns with
        the setup form, and a campaign's stage, ballot, nominations, running
        count and frozen result. Deciding a nomination was the one that made the
        others matter - without it a nomination sat as `Nominated` forever, and
        since the service refuses `VotingOpen` on an empty ballot, a campaign
        could be started and then not run. No new endpoints, and the service
        already had full gateway smoke coverage
  - [x] **Volunteer groups (2).** One screen: create with a president named
        from the directory, and stand a group down or bring it back. The
        president is part of creating a group because one without them has
        nowhere for its join requests to go
  - [x] **Social issues (1).** The reviewer's approval queue - the mildest of
        these, and the screen says so: a reviewer holding an issue's id could
        already decide it from the member portal. What was missing was any way
        to learn something was waiting
- [x] **A smoke run that fails loudly when an id extraction goes wrong**, done
      2026-09-03. `require_id` stops the run at the extraction, naming the
      variable, rather than letting every check below it report someone else's
      service answering 404.

      The item said the pattern was "everywhere in this script", and counting
      it found otherwise: of 31 extractions, 21 already had a check that
      reported and continued — including the session id, which guards the
      subtler case of *a plausible id belonging to something else* by asserting
      it differs from the Pathshala's. Eight had nothing at all, and those are
      the ones guarded now. Doubling up on the rest would have converted a
      counted failure into an abort, which is worse rather than louder.

      It exits rather than counting, deliberately: an empty id means the script
      or the stack is broken, not that the product regressed, and continuing
      turns one fault into a screenful pointing at innocent services. Verified
      by breaking one extraction — the run stops immediately with one clear
      line — and then clean from empty volumes at 294 of 294.

      The one place an empty id is normal is creating a Samaaj that already
      exists, which answers 409 with no id and is resolved by a fallback; that
      guard sits after the fallback rather than before it.

---

## Open Decisions

Flagged in the requirements docs as suggestions, not yet resolved.
Resolve each before the phase that depends on it starts — don't let
these sit unresolved into the sprint that needs them.

- [x] **Adult-child conversion: admin-approved.** Decided 2026-08-28. A child
      who turns 18 requests conversion; a Samaaj admin approves it before the
      login is created. Safer default and easy to relax later.
- [x] **Boli anti-abuse rules: a minimum increment, and an auto-extend window
      measured from the bid.** Decided 2026-09-03. The increment has been
      enforced since the service was built; `AutoExtendSeconds` is the window,
      per Boli, chosen by the Samaaj when the Boli is opened, capped at an hour
      and 0 (off) by default. A bid landing inside the window moves the close to
      *the bid plus the window*, not the old close plus the window — adding to
      the old close would still reward waiting, while measuring from the bid
      means no moment is better to bid at than any other. There is no cap on
      repeats: a Boli that keeps extending is one people are still bidding on,
      and a Samaaj wanting a hard stop closes it. See
      `services/boli-service/CLAUDE.md`.
- [x] **Single domain, no per-Samaaj subdomain or CNAME.** Decided
      2026-08-28. A member signs in once and the system decides which Samaaj
      they belong to, because a login identifier is unique platform-wide.
      One certificate, no wildcard DNS, and the tenant travels in the token.
      This superseded the subdomain design in `docs/product/ARCHITECTURE.md`
      §3 and §6; root `CLAUDE.md` §6 is now the source of truth. The `Domain`
      column on `Tenant` is retained but unused.
- [x] **Eventing: native Kafka Outbox, no MassTransit.** Settled by
      `CLAUDE.md` §5 and built that way in every service.
- [ ] **DPDP Act, 2023 compliance review — required.** Confirmed 2026-08-28 as
      in scope. The technical capabilities are built: consent records, data
      export, and erasure across all three services. What is still open is not
      engineering - how the notice is worded, what retention periods apply,
      what makes parental consent "verifiable", and whether de-identifying
      audit rows is a defensible reading of the s.8(7) retention exception.
      The five questions at the end of `docs/product/DPDP-COMPLIANCE.md` need
      someone qualified in Indian data protection law, and must be answered
      before Phase 1 ships to real users.
