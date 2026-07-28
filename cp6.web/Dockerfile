FROM node:22-alpine AS build
WORKDIR /app
ARG RELEASE_VERSION=0.0.0-dev
ARG GIT_SHA=unknown
ENV CP6_RELEASE_VERSION=${RELEASE_VERSION}
ENV CP6_GIT_SHA=${GIT_SHA}

COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run build-only

FROM nginx:alpine AS runtime

COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80

CMD ["nginx", "-g", "daemon off;"]
