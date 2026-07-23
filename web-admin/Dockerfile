# syntax=docker/dockerfile:1
#
# Kuaforum web-admin (Next.js 15, standalone output). Multi-stage, non-root.
#
# Build context MUST be the `web-admin/` directory:
#   docker build -f web-admin/Dockerfile \
#     --build-arg NEXT_PUBLIC_API_BASE_URL=https://api.kuaforum.com \
#     -t kuaforum-web-admin:local web-admin
#
# IMPORTANT (Next.js gotcha, not a bug): NEXT_PUBLIC_* vars are inlined into the
# client JS bundle at BUILD time, not read at container start. This image is
# only reusable as-is for the environment it was built for (one API base URL
# baked in). MVP has a single production environment, so this is acceptable —
# if a second environment (e.g. staging) needs a different API URL later, either
# build a second image with a different --build-arg, or move to a runtime-config
# pattern (public env injected via a small script tag at container start).
# Not building that now — YAGNI until there's a second environment.

FROM node:20-alpine AS base

# --- deps ----------------------------------------------------------------
FROM base AS deps
WORKDIR /app
RUN corepack enable
COPY package.json pnpm-lock.yaml ./
RUN corepack use pnpm@10 && pnpm install --frozen-lockfile

# --- build -----------------------------------------------------------------
FROM base AS build
WORKDIR /app
RUN corepack enable
COPY --from=deps /app/node_modules ./node_modules
COPY . .

ARG NEXT_PUBLIC_API_BASE_URL
ENV NEXT_PUBLIC_API_BASE_URL=${NEXT_PUBLIC_API_BASE_URL} \
    NEXT_TELEMETRY_DISABLED=1

RUN corepack use pnpm@10 && pnpm build

# --- runtime -----------------------------------------------------------------
FROM base AS runtime
WORKDIR /app

ENV NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1 \
    PORT=3000 \
    HOSTNAME=0.0.0.0

# node:alpine already ships a non-root "node" user (uid 1000).
# .next dir needs to be writable by that user (Next.js ISR/prerender cache).
RUN mkdir -p .next && chown node:node .next

COPY --from=build --chown=node:node /app/.next/standalone ./
COPY --from=build --chown=node:node /app/.next/static ./.next/static
# NOTE: no public/ directory exists in this app yet (verified at Dockerfile
# authoring time). If one is added, also copy it here:
#   COPY --from=build --chown=node:node /app/public ./public

USER node
EXPOSE 3000

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD wget -qO- http://localhost:3000/login >/dev/null || exit 1

CMD ["node", "server.js"]
