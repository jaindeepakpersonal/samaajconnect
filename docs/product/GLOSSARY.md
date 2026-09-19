# Glossary

Domain terms used throughout the requirements, wireframes, and code. Keep
entity/field names in code aligned to this vocabulary rather than inventing
synonyms — it keeps the ER diagram, the API, and the UI copy consistent.

| Term | Meaning |
|---|---|
| **Samaaj** | A Jain community organization. Each Samaaj is one tenant on the platform, with its own subdomain (e.g. `mahavir-samaj.samaajconnect.com`). |
| **Tenant** | The technical unit of isolation corresponding 1:1 with a Samaaj. Every tenant-owned database row carries a `TenantId`. |
| **Super Admin** | Platform-level administrator. Not scoped to any tenant; manages tenant creation, Pathshala master records, and platform configuration. |
| **Samaaj Admin** | Administrator scoped to exactly one tenant. |
| **Family Admin** | The member who manages a Family record: holds its code, decides join requests, adds children and starts conversions. Whoever creates the household starts as its admin. |
| **Family Head** | The household's eldest member - a label the Family Admin chooses from its members. Grants nothing. (The identity role named `FamilyHead` predates this split and is unrelated to the label.) |
| **Child Profile** | A dependent profile under a Family, with no independent login until the adult-child conversion flow is completed. |
| **Adult-child conversion** | The flow triggered when a Child Profile's age passes 18: the child can create their own member login while the family relationship and historical child records are preserved. |
| **Timeline** | The tenant's social feed: Samaaj announcements plus approved member posts. |
| **Volunteer Group** | A tenant-scoped group (e.g. Seva Group, Yuva Mandal) with a President who manages membership applications and roles. |
| **Social Issue** | A community problem a member reports (e.g. road safety, sanitation), which must pass an approval workflow before it is publicly visible. |
| **Celebrities of Samaaj** | The community voting/recognition module: members nominate and vote for fellow members; results publish a Top 10. |
| **Jain Pathshala** | Religious/cultural education program for children — classes, teachers, attendance, exams, and progress tracking. |
| **Boli / Auction** | A traditional Jain community bidding process (e.g. for the honor of performing a ritual), run per Occasion with defined Boli Types, bids, and a published result. |
| **Occasion** | The event or ceremony context under which one or more Boli (auction items) are opened. |
| **Module feature flag** | A per-tenant on/off switch for a business module (e.g. a Samaaj that doesn't run a Pathshala can have that module hidden). |
| **Tenant context** | The resolved `TenantId` + `SamaajSlug` attached to a request, derived from the subdomain and re-validated server-side — never trusted from the client alone. |
| **IDOR** | Insecure Direct Object Reference — accessing another tenant's (or another user's) record by guessing/supplying its ID. Prevented by always scoping queries with `TenantId`. |
| **Gautra** | A clan/lineage name a member identifies with, maintained as each Samaaj's own list — the one piece of master data that is "Samaaj-wise" rather than shared across the whole platform. |
| **Master data** | Platform-wide reference data — Country, City, Locality, ProfessionType, Gender, MaritalStatus — plus each Samaaj's own Gautra list, owned by master-data-service and consumed by every registration and profile screen's dropdowns. |
| **Locality** | An area/neighborhood within a City (e.g. Hiran Magri, Surajpole). What the wireframes and this glossary call the "locations master". |
| **Biodata** | A member's matrimony profile — a one-time copy of select base-profile fields (name, DOB, Gautra, profession, qualification, city, locality) plus matrimony-specific fields (height, marital status, diet, family details, partner preference), created only with the member's own explicit consent and never auto-synced back to the base profile. |
| **Proposal** | A matrimony interest expressed by one member's biodata toward another's, within the same Samaaj: Pending until the recipient Accepts or Declines, or the sender Withdraws it. |
| **Business** | A commercial listing a member owns and registers within their own Samaaj — a member may own more than one, each with its own category, location, contact details and products. Visible in the directory immediately; no admin moderation. |
| **Product** | An item listed under a Business, with a name, price and availability. Never deleted, only toggled unavailable, so a past order still names a real product. |
| **Order** | A request one member sends a business owner for one or more products: Pending until the owner Accepts or Declines, then Fulfilled or Cancelled once Accepted. Its total is computed from each product's price at the moment it was placed, not read live afterwards. |
