# ===== Vue Frontend Multi-Stage Dockerfile =====
#
# 面试要点：
# 1. 构建阶段用 Node 镜像编译 Vue 项目
# 2. 运行阶段用 Nginx 镜像（仅 ~25MB）托管静态文件
# 3. 生产环境前端不需要 Node，Nginx 就够了

# ===== Stage 1: Build =====
FROM node:22-alpine AS build
WORKDIR /app

# 先复制 package.json 安装依赖（利用层缓存）
COPY package.json package-lock.json* ./
RUN npm install

# 复制源码，构建
COPY . .
RUN npm run build-only

# ===== Stage 2: Nginx =====
FROM nginx:alpine AS runtime

# 复制构建产物到 Nginx 静态目录
COPY --from=build /app/dist /usr/share/nginx/html

# 复制自定义 Nginx 配置（API 代理 + SPA 路由）
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80

CMD ["nginx", "-g", "daemon off;"]
