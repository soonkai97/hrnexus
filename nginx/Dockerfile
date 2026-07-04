# Stage 1 — build Angular apps
FROM node:22-alpine AS builder
WORKDIR /workspace

COPY package.json package-lock.json ./
RUN npm ci

COPY angular.json tsconfig.json tsconfig.app.json* ./
COPY projects/ ./projects/

# Stamp the build version into each app's public/ (served at /version.json) BEFORE
# ng build so Angular copies it into dist. Falls back to "dev" for un-versioned builds.
ARG BUILD_VERSION=dev
ARG BUILD_SHA=local
ARG BUILT_AT=
RUN printf '{ "version": "%s", "commit": "%s", "builtAt": "%s" }\n' "$BUILD_VERSION" "$BUILD_SHA" "$BUILT_AT" > projects/admin-app/public/version.json \
 && printf '{ "version": "%s", "commit": "%s", "builtAt": "%s" }\n' "$BUILD_VERSION" "$BUILD_SHA" "$BUILT_AT" > projects/staff-app/public/version.json

RUN npx ng build shared-lib
RUN npx ng build admin-app  --configuration production
RUN npx ng build staff-app  --configuration production

# Stage 2 — serve with nginx
FROM nginx:alpine
COPY nginx/nginx.conf        /etc/nginx/nginx.conf
COPY nginx/maintenance.html  /usr/share/nginx/maintenance.html
COPY --from=builder /workspace/dist/admin-app/browser  /usr/share/nginx/admin
COPY --from=builder /workspace/dist/staff-app/browser  /usr/share/nginx/staff
EXPOSE 80
