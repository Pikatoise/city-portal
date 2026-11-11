FROM node:26.8.1-bookworm-slim AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --ignore-scripts
COPY . .
RUN npm run build

FROM node:26.8.1-bookworm-slim AS runtime
ENV NODE_ENV=production
ENV PORT=4000
WORKDIR /app
COPY --from=build --chown=node:node /app/dist/city-portal ./dist/city-portal
USER node
EXPOSE 4000
CMD ["node", "dist/city-portal/server/server.mjs"]
