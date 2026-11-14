# Stage 1 — build Angular apps
FROM node:22-alpine AS builder
WORKDIR /workspace

COPY package.json package-lock.json ./
RUN npm ci

COPY angular.json tsconfig.json tsconfig.app.json* ./
COPY projects/ ./projects/

RUN npx ng build shared-lib
RUN npx ng build web --configuration production

# Stamp the build version into the app's index.html (replaces the __BUILD_VERSION__ placeholder
# baked into src/index.html). Read synchronously by VersionService — no runtime fetch. Falls back
# to "dev" for un-versioned builds (the placeholder is left in place → VersionService treats it as dev).
ARG BUILD_VERSION=dev
RUN sed -i "s/__BUILD_VERSION__/${BUILD_VERSION}/g" \
      dist/web/browser/index.html

# Stage 2 — serve with nginx
FROM nginx:alpine
COPY nginx/nginx.conf        /etc/nginx/nginx.conf
COPY nginx/maintenance.html  /usr/share/nginx/maintenance.html
COPY --from=builder /workspace/dist/web/browser  /usr/share/nginx/web
EXPOSE 80
