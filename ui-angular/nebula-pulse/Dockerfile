# syntax=docker/dockerfile:1.7

FROM node:20-alpine AS deps
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

FROM node:20-alpine AS build
WORKDIR /app

COPY --from=deps /app/node_modules ./node_modules
COPY . .
RUN npm run build -- --configuration production

FROM nginx:1.27-alpine AS final
COPY --from=build /app/dist/nebula-pulse/browser /usr/share/nginx/html
EXPOSE 80

ENTRYPOINT ["nginx", "-g", "daemon off;"]
