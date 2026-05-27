# Stage 1 — build Angular apps
FROM node:22-alpine AS builder
WORKDIR /workspace

COPY package.json package-lock.json ./
RUN npm ci

COPY angular.json tsconfig.json tsconfig.app.json* ./
COPY projects/ ./projects/

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
