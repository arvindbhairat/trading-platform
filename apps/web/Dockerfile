FROM node:24-alpine AS build
WORKDIR /app

# Monorepo dependency install
COPY package.json package-lock.json ./
COPY apps/web/package.json apps/web/package.json
RUN npm ci

# Copy source
COPY apps/web/ apps/web/

# Build Next.js app
RUN npm run web:build

# ─────────────────────────────────────────────────────────────────────────────
FROM node:24-alpine AS runtime
WORKDIR /app

COPY --from=build /app/apps/web/.next .next
COPY --from=build /app/apps/web/public public
COPY --from=build /app/apps/web/package.json .
COPY --from=build /app/apps/web/next.config.ts .
COPY --from=build /app/node_modules node_modules

EXPOSE 3000

ENV NODE_ENV=production
ENV PORT=3000

CMD ["npx", "next", "start"]
