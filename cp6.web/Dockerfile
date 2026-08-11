FROM node:22-alpine AS build
WORKDIR /src/cp6.web
ARG RELEASE_VERSION=0.0.0-dev
ARG GIT_SHA=unknown
ENV CP6_RELEASE_VERSION=${RELEASE_VERSION}
ENV CP6_GIT_SHA=${GIT_SHA}

COPY cp6.web/package.json cp6.web/package-lock.json ./
RUN npm ci

COPY cp6.web/ ./
COPY sdk/typescript/space-design-v1/ /src/sdk/typescript/space-design-v1/
RUN npm run build-only

FROM nginx:alpine AS runtime

COPY --from=build /src/cp6.web/dist /usr/share/nginx/html
COPY cp6.web/nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80

CMD ["nginx", "-g", "daemon off;"]
