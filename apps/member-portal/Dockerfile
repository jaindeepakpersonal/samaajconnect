# The member portal, server-side rendered.
#
# Build context is the repo root (see docker-compose.yml): this is an npm
# workspace, so the lockfile and every workspace manifest live above this
# directory and `npm ci` needs all of them to resolve the same tree the host
# resolves.
#
# The app is reached through the gateway, not on a port of its own - see the
# `member-portal` route in gateway/src/Sangam.Gateway/appsettings.json. Its
# production ApiConfig has an empty gatewayUrl, meaning "same origin", so `/v1`
# has to be served by whatever serves the app. Putting it behind the gateway is
# what makes that true.

FROM node:22-alpine AS build
WORKDIR /src

# Manifests first, so a source-only change reuses the install layer.
COPY package.json package-lock.json ./
COPY apps/member-portal/package.json apps/member-portal/
COPY apps/admin-portal/package.json apps/admin-portal/
COPY libs/shared/package.json libs/shared/

RUN npm ci

COPY angular.json ./
COPY libs/ libs/
COPY apps/member-portal/ apps/member-portal/

RUN npx ng build member-portal

# ---------------------------------------------------------------------------

FROM node:22-alpine AS runtime
WORKDIR /app

ENV NODE_ENV=production
ENV PORT=4000
EXPOSE 4000

# A second, production-only install rather than copying node_modules from the
# build stage: that tree carries the Angular compiler and the test runner, which
# have no business in a running image.
COPY package.json package-lock.json ./
COPY apps/member-portal/package.json apps/member-portal/
COPY apps/admin-portal/package.json apps/admin-portal/
COPY libs/shared/package.json libs/shared/

RUN npm ci --omit=dev --ignore-scripts

COPY --from=build /src/dist/member-portal ./dist/member-portal

# Non-root: the SSR server reads its own bundle and writes nothing.
USER node

CMD ["node", "dist/member-portal/server/server.mjs"]
