# samaajconnect

[![CI](https://github.com/jaindeepakpersonal/samaajconnect/actions/workflows/ci.yml/badge.svg)](https://github.com/jaindeepakpersonal/samaajconnect/actions/workflows/ci.yml)

Multi-tenant platform for Jain Samaaj (community) organizations —
member portal, unified admin panel, and ten backend services behind a
YARP gateway.

## Start here

- **New to this repo?** Read [`CLAUDE.md`](./CLAUDE.md) first — it's
  the architecture/convention reference every service and both Angular
  apps follow.
- **Picking up work?** Check [`DEVELOPMENT_PLAN.md`](./DEVELOPMENT_PLAN.md)
  for current status and the next unchecked item. Update it when you
  finish a session.
- **Business requirements & rationale:** [`docs/product/`](./docs/product/) —
  start with `docs/product/README.md`.
- **Clickable wireframes:** `docs/product/wireframes/*.html` — open
  directly in a browser. These are the UI spec, not just a reference —
  see `.claude/skills/wireframe-to-angular/`.
- **Rewritten requirement documents (Word):**
  `docs/product/requirements/*.docx` — the narrative version with
  suggested-enhancement callouts, for anyone who wants that instead of
  the terse dev reference in `docs/product/*.md`.

## Running it locally

Requires Docker, the .NET 9 SDK, and Node 20+.

```bash
docker compose up -d --build
```

That brings up Postgres (one logical database per service, created on
first start by `infra/postgres/init-databases.sh`), Redis, a single-node
KRaft Kafka broker, and every service built so far.

| Service | Local URL |
|---|---|
| **gateway** | http://localhost:8080 - the entry point everything should go through |
| identity-tenant-service | http://localhost:5101 (Swagger at `/swagger`) |
| audit-notification-service | http://localhost:5102 (Swagger at `/swagger`) |
| member-family-service | http://localhost:5103 (Swagger at `/swagger`) |

The service ports are exposed for debugging only. Subdomains are supplied with
an explicit `Host:` header locally, so no `/etc/hosts` entries are needed:

```bash
bash scripts/smoke-through-gateway.sh
```

Then start the member portal against it:

```bash
npm start
```

It serves on http://localhost:4200 and proxies `/v1` to the gateway. See
`apps/member-portal/CLAUDE.md` for how the Samaaj is chosen locally.

Backend tests - the integration ones need Docker for Testcontainers:

```bash
dotnet test services/identity-tenant-service/Sangam.IdentityTenant.sln
```

Frontend tests:

```bash
npm test
```

## Scaffolding

| Task | Use |
|---|---|
| Add a new bounded-context service | `.claude/skills/new-microservice/` |
| Add a command/query to an existing service | `.claude/skills/add-service-feature/` |
| Turn a wireframe screen into a real component | `.claude/skills/wireframe-to-angular/` |

## Repo layout

```
samaajconnect/
├── CLAUDE.md
├── DEVELOPMENT_PLAN.md
├── docker-compose.yml
├── docs/product/          (architecture, data model, dev reference)
│   ├── wireframes/        (clickable HTML screen references)
│   └── requirements/      (narrative requirement docs, .docx)
├── gateway/
├── services/{name}-service/   (10 services — see docs/product/SERVICES.md)
├── apps/member-portal/
├── apps/admin-portal/
├── libs/
└── .claude/skills/
```

See `CLAUDE.md` §2 for the full annotated tree and §10 for a "where do
I find X" index across every doc in this repo.
