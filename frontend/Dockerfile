# Frontend image. Build from the frontend folder:
#   docker build --build-arg NEXT_PUBLIC_API_BASE_URL=http://localhost:8080 frontend

# ---- deps: install exactly what package-lock.json says ----
FROM node:22-alpine AS deps
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci

# ---- build ----
FROM node:22-alpine AS build
WORKDIR /app
# NEXT_PUBLIC_ values are copied into the browser JavaScript during `next build`, so the API
# address must be known now, not when the container starts. It's the address the BROWSER uses
# (localhost:8080 on your machine), not a Docker-internal name like http://api:8080.
ARG NEXT_PUBLIC_API_BASE_URL=http://localhost:8080
ENV NEXT_PUBLIC_API_BASE_URL=$NEXT_PUBLIC_API_BASE_URL
ENV NEXT_TELEMETRY_DISABLED=1
COPY --from=deps /app/node_modules ./node_modules
COPY . .
RUN npm run build

# ---- runner: only the standalone server and the static files ----
FROM node:22-alpine AS runner
WORKDIR /app
ENV NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1 \
    PORT=3000 \
    HOSTNAME=0.0.0.0
COPY --from=build --chown=node:node /app/.next/standalone ./
COPY --from=build --chown=node:node /app/.next/static ./.next/static
# The node image includes a non-root "node" user.
USER node
EXPOSE 3000
CMD ["node", "server.js"]
