# The admin panel: a static single-page app served by nginx, which also proxies
# /v1 to the gateway so the app is same-origin with its own API.
#
# Build context is the repo root (see docker-compose.yml) - this is an npm
# workspace, and `npm ci` needs the lockfile and every workspace manifest.
#
# Unlike the member portal this is *not* behind the gateway. See nginx.conf for
# why: both apps share sessionStorage keys, so one origin would mean one
# session.

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
COPY apps/admin-portal/ apps/admin-portal/

RUN npx ng build admin-portal

# ---------------------------------------------------------------------------

FROM nginx:1.27-alpine AS runtime

# 8080 rather than nginx's default 80, to match every other container in this
# stack - the compose port mapping and any future ingress then read the same
# everywhere.
EXPOSE 8080

# Replaces the stock server block, which listens on 80 and serves nginx's
# welcome page.
COPY apps/admin-portal/nginx.conf /etc/nginx/conf.d/default.conf

COPY --from=build /src/dist/admin-portal/browser /usr/share/nginx/html

# Deliberately no `nginx -t` here. It resolves the upstream host in
# `proxy_pass`, and `gateway` only exists on the compose network at run time -
# so a syntax check at build time fails on a configuration that is correct.
