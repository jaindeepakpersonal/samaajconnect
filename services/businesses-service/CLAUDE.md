# businesses-service

## Purpose

Members' own businesses within a Samaaj: a member registers one or more
businesses they own, lists products under each, and other members browse a
directory (filterable by category and location) and send orders to a
business owner, who accepts or declines them and later marks an accepted
order fulfilled or cancels it.

Module-gated on `businesses`, off by default: not every Samaaj wants a
member marketplace running, the same stance `ModuleCatalog` already takes
for `boli` and `matrimony`.

Visibility is within the caller's own Samaaj only — there is no cross-tenant
directory or order, and every read and write is scoped by the ordinary
tenant query filter like any other service.

## Entities

| Entity | Status | Notes |
|---|---|---|
| `Business` | built | Tenant-scoped. `Id` is a fresh id, not the owner's own — unlike Matrimony's `BiodataProfile`, a member may own more than one. |
| `Product` | built | An owned entity of `Business`, held in its `Products` collection and mutated only through methods on it — never its own aggregate root. |
| `Order` | built | Tenant-scoped. A peer-to-peer fact between a buyer and a `Business`, not owned by either — the same reasoning that keeps Matrimony's `Proposal` and a Boli's bid history their own records. |

`Business.CategoryId` is a master-data-service `LookupItem` id,
`Category = BusinessCategory` — a flat, admin-managed platform-wide list
alongside Country/ProfessionType/Gender/MaritalStatus/Qualification, added
2026-09-12 with no migration since `LookupItem.Category` is already
`HasConversion<string>()`. `CityId`/`LocalityId` are the same master-data-service
ids every other address field on this platform already uses. This service
stores each id and nothing else — no referential integrity across the
service boundary, the same stance every service holding one of these ids
already takes.

`Business.ContactPhone`/`ContactEmail` are plain fields the owner types in
for this listing, **not** a read of the member's own privacy-gated
Mobile/Email (member-family-service's `FieldPrivacy`). Publishing a business
is a different act from that member's own base-profile privacy choices, and
reading those here would let a `Business` write leak a value the member
chose to keep private elsewhere.

**No photo field.** There is no file storage this service could use, and a
field nothing can populate is worse than an absent one — the same reasoning
identity-tenant-service's own `CLAUDE.md` records for a tenant logo before
upload existed.

`Order.Items` is an owned collection of `OrderItem` (`ProductId`,
`ProductName`, `UnitPriceInPaise`, `Quantity`) — **snapshotted at placement
time, not read live off `Product` afterwards.** An order is a record of what
was actually agreed; an owner editing a price next week must not silently
rewrite last week's order. `TotalAmountInPaise` is computed server-side from
these snapshots and never trusted from the client.

`Order.BusinessOwnerMemberId` is also snapshotted at placement time, from
`Business.OwnerMemberId` — this is what lets `Order`'s own domain methods
enforce "the owner only" internally (the same belt-and-braces double-gate
Matrimony's `Proposal.Respond` already applies to "the recipient only"),
without a live join back to `Business` for every state transition. Reading a
business's own orders (`ListReceivedOrdersQuery`) is a plain filter on this
field, needing no join at all.

## Commands

Self-scoped, gated by the same role list member-family-service's own
profile-update command uses (`Member, FamilyHead, VolunteerGroupPresident,
PathshalaTeacher, PathshalaStudent, ContentModerator, BoliManager,
SamaajAdmin, SuperAdmin`) — no new permission key is minted, following that
command's own precedent of role-only gating for a purely self-scoped write.

| Command | Policy | Status |
|---|---|---|
| `CreateBusinessCommand` | role list above | built |
| `UpdateBusinessCommand` | role list above, owner only | built |
| `SetBusinessStatusCommand` | role list above, owner only | built |
| `AddProductCommand` | role list above, owner only | built |
| `UpdateProductCommand` | role list above, owner only | built |
| `SetProductAvailabilityCommand` | role list above, owner only | built |
| `PlaceOrderCommand` | role list above | built |
| `RespondToOrderCommand` | role list above, business owner only | built |
| `FulfillOrderCommand` | role list above, business owner only | built |
| `CancelOrderCommand` | role list above, buyer-while-Pending or owner-while-Accepted | built |
| `EraseBusinessesDataCommand` | `[InternalRequest]` | built |

`PlaceOrderCommand` refuses: an inactive business, a product that does not
exist or is not `IsAvailable` on that business, a zero-item order, or
ordering from a business the caller owns.

`EraseBusinessesDataCommand` is raised only by this service's own Kafka
consumer, never mapped to an endpoint — see "Events consumed" below.

## Queries

Reuse the existing `Members.Read` permission rather than minting a new key,
mirroring Matrimony's own search/candidate queries and
volunteer-groups-service's list/get queries.

| Query | Policy | Status |
|---|---|---|
| `ListMyBusinessesQuery` | `Members.Read`, caller's own | built |
| `SearchBusinessesQuery` | `Members.Read` | built |
| `GetBusinessQuery` | `Members.Read` | built |
| `ListMyOrdersQuery` | `Members.Read`, caller's own | built |
| `ListReceivedOrdersQuery` | `Members.Read`, caller's own businesses | built |

`SearchBusinessesQuery` is the directory: active businesses in the caller's
own Samaaj, filtered by `CategoryId`/`CityId`/`LocalityId`/`Term`, capped at
50 by default (the same default-50/max-100 directory-search convention
member-family-service's own member search and Matrimony's own biodata
search already use), `InclusiveBetween(1, 100)`.

`GetBusinessQuery` shows every product to the business's own owner and only
`IsAvailable` ones to anyone else — the same field, filtered by who is
asking, rather than two response shapes. 404 if inactive-and-not-owner or in
a different Samaaj.

## Events published

| Event | Topic | Raised by |
|---|---|---|
| `BusinessCreatedDomainEvent` | `businesses.business.created.v1` | `Business.Create` |
| `BusinessStatusChangedDomainEvent` | `businesses.business.status-changed.v1` | `Business.SetActive` |
| `OrderPlacedDomainEvent` | `businesses.order.placed.v1` | `Order.Place` |
| `OrderRespondedDomainEvent` | `businesses.order.responded.v1` | `Order.Accept` / `.Decline` |
| `OrderFulfilledDomainEvent` | `businesses.order.fulfilled.v1` | `Order.Fulfill` |
| `OrderCancelledDomainEvent` | `businesses.order.cancelled.v1` | `Order.Cancel` |

All six have `audit-notification-service` `KnownEvents.cs` descriptors. No
event is raised for `Product` CRUD or for `Business.Update`'s field edits —
routine self-service catalog edits with no other consumer and no distinct
actor, the same reasoning `LookupItem` field edits don't each get their own
audited fact beyond Created/Updated/Deactivated.

`OrderRespondedDomainEvent` and `OrderFulfilledDomainEvent` both carry
`BusinessOwnerMemberId` and point `KnownEvents.cs`'s `ActorIdProperty` at
it — responding to, and fulfilling, an order is always the owner's own
act. This was missing on first ship: a live audit-log check found both
topics recording the order and nothing else, with no way to answer "who
accepted this?" at all, because neither event carried the owner's id in
the first place — not merely an undescribed topic, but a genuine gap in
what the event itself could say. Fixed by adding the field, the same
"belongs on the event, not just the descriptor" shape every other
actor-carrying topic on this platform already uses.

`OrderCancelledDomainEvent` is the one exception, and deliberately so:
cancelling can be either the buyer's act (while Pending) or the owner's
(while Accepted), and naming one unconditionally would be wrong for the
other half of the time — the same restraint `post.reported.v1` already
takes for a field this catalogue would otherwise have to guess at.

## Events consumed

**`identity.user.erased.v1`** — a member's full account erasure removes
every `Business` they owned outright (unlike Matrimony's `Proposal`, no
other party has a stake in a `Business` row itself, only in the `Order`s
referencing it by plain id) and de-identifies (strips `Note`, keeps the row)
every `Order` they placed as a buyer, so the business owner's own order
history is not silently broken. Idempotent against Kafka's at-least-once
delivery: nothing found returns success rather than an error, matching
`CreateProfileForNewUserCommandHandler`'s own "unusable payload → log a
warning and succeed" precedent.

No `[InternalRequest]`-gated synchronous endpoint exists anywhere in this
service — the only cross-service data flow is this one Kafka consumer.

## API endpoints

| Method | Path | Roles |
|---|---|---|
| GET | `/v1/businesses/mine` | `Members.Read` |
| POST | `/v1/businesses/mine` | role list above |
| PATCH | `/v1/businesses/mine/{businessId}` | role list above, owner only |
| POST | `/v1/businesses/mine/{businessId}/status` | role list above, owner only |
| POST | `/v1/businesses/mine/{businessId}/products` | role list above, owner only |
| PATCH | `/v1/businesses/mine/{businessId}/products/{productId}` | role list above, owner only |
| POST | `/v1/businesses/mine/{businessId}/products/{productId}/availability` | role list above, owner only |
| GET | `/v1/businesses` | `Members.Read` |
| GET | `/v1/businesses/{businessId}` | `Members.Read` |
| POST | `/v1/businesses/orders` | role list above |
| GET | `/v1/businesses/orders/mine` | `Members.Read` |
| GET | `/v1/businesses/orders/received` | `Members.Read` |
| POST | `/v1/businesses/orders/{id}/respond` | role list above, business owner only |
| POST | `/v1/businesses/orders/{id}/fulfill` | role list above, business owner only |
| POST | `/v1/businesses/orders/{id}/cancel` | role list above, buyer or owner |
| GET | `/health` | anonymous |

## Decisions worth knowing before you change this service

**No admin moderation, on purpose.** A business goes live the moment its
owner registers it, exactly like Matrimony's biodata. There is no oversight
screen and no dashboard tile in admin-portal for this module in this pass.

**Deactivating a business is reversible and says so; erasing one is not,
and only happens through account erasure.** These are different acts for
the same reason Matrimony keeps withdrawal and erasure distinct: one is a
preference the owner can reverse, the other is the consequence of the
account itself no longer existing.

**An order's total is computed server-side from snapshotted line items and
never trusted from the client.** The client displays a running total for
the buyer's convenience while placing an order, but the authoritative
number is `Order.TotalAmountInPaise`, set once at `Order.Place` from
`Product.PriceInPaise` at that moment — never recomputed from a client-sent
figure and never re-read from `Product` afterwards.

**`Order.BusinessOwnerMemberId` is a deliberate denormalization, not an
oversight.** It is what lets `Order.Accept`/`.Decline`/`.Fulfill`/`.Cancel`
enforce "the owner only" as a domain invariant (defense in depth alongside
the handler's own 404-not-403 guard) and lets `ListReceivedOrdersQuery`
avoid a join back to `Business` entirely. Any change to who owns a business
(there is none today — `Business.OwnerMemberId` is set once at `Create` and
never reassigned) would need to consider whether past orders' snapshot
should follow.

**Domain-assigned keys are `ValueGeneratedNever`.** `Business.Id` and
`Order.Id` are both fresh `Guid.NewGuid()` from their factories. This
repository has hit the EF default several times already in other services;
see their own `CLAUDE.md`s.

## Dependencies

- **Postgres** `samaajconnect_businesses` — `ConnectionStrings__Default`
- **Kafka** — `Kafka__BootstrapServers`, producer and consumer
  (`identity.user.erased.v1`)
- **Jwt** — `Jwt__SigningKey`, validation only. This service never mints
  tokens.
- No Redis dependency.

## Testing

- `Sangam.Businesses.UnitTests` — `Business` (status-change idempotency,
  product add/update/availability, unknown-product guards) and `Order`
  (every state transition, the owner-only guards, the buyer-vs-owner cancel
  branches, can't-order-your-own-business, the zero-item guard,
  server-computed total, de-identifying the buyer).
- `Sangam.Businesses.IntegrationTests` — Testcontainers Postgres, no Kafka
  broker (bootstrap address points at an unreachable address on purpose, so
  outbox rows and the erasure consumer can be asserted without racing a real
  one). Covers creating/updating a business and its products, the
  directory's category/location filters and its exclusion of inactive
  businesses, the full place/accept/decline/fulfill/cancel order lifecycle,
  the unavailable-product and own-business refusals, tenant isolation, and
  the `identity.user.erased.v1` consumer removing owned businesses while
  de-identifying placed orders and leaving the other party's own order
  history intact.

```
dotnet test services/businesses-service/Sangam.Businesses.sln
```

`scripts/smoke-through-gateway.sh` covers this service through the gateway:
the module toggled on, a business registered and a product added, the
directory finding it, the can't-order-your-own-business refusal, an order
placed/accepted/fulfilled, the Received list showing the outcome,
deactivating the business (drops out of the directory, order history
survives), and the module toggled off again with every route answering 404
while the underlying data survives.
