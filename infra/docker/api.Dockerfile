# syntax=docker/dockerfile:1.7
# KINDLING API + migration runner image. Build from the repository root:
#   docker build -f infra/docker/api.Dockerfile -t kindling-api .
FROM node:24-slim AS base
ENV PNPM_HOME=/pnpm PATH=/pnpm:$PATH
RUN corepack enable
WORKDIR /repo

FROM base AS build
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml tsconfig.base.json ./
COPY apps/api/package.json apps/api/
COPY packages/contracts/package.json packages/contracts/
COPY packages/db/package.json packages/db/
RUN pnpm install --frozen-lockfile --filter @kindling/api...
COPY apps/api apps/api
COPY packages packages
ARG BUILD_VERSION=0.0.0-dev
ARG BUILD_COMMIT=unknown
RUN pnpm --filter @kindling/api build \
 && pnpm --filter @kindling/api deploy --prod --legacy /out

FROM node:24-slim AS runtime
ARG BUILD_VERSION=0.0.0-dev
ARG BUILD_COMMIT=unknown
ENV NODE_ENV=production \
    MIGRATIONS_DIR=/app/migrations \
    BUILD_VERSION=${BUILD_VERSION} \
    BUILD_COMMIT=${BUILD_COMMIT}
WORKDIR /app
COPY --from=build /out/node_modules ./node_modules
COPY --from=build /out/package.json ./package.json
COPY --from=build /repo/apps/api/dist ./dist
COPY --from=build /repo/packages/db/migrations ./migrations
USER node
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=3s --start-period=10s --retries=3 \
  CMD ["node", "-e", "fetch('http://127.0.0.1:'+(process.env.API_PORT||8080)+'/health/live').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"]
# Run migrations as a one-off before rollout:  docker run --rm --env-file … kindling-api node dist/migrate.js
CMD ["node", "--enable-source-maps", "dist/main.js"]
