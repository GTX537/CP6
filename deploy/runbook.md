# CP6 数据库部署 / 搬迁 Runbook

> 适用：把 CP6（含 **DB 全部数据**，尤其 `Sys_Lang` 多语言词条等运行期资产）部署到新服务器 / 新环境。
> 背景：生产经 `docker-compose.yml` 跑——SQL Server 2022 容器 `cp6-db`，数据在命名卷 `cp6-db-data:/var/opt/mssql`，应用容器 `cp6-api` 启动时 `db.Database.Migrate()` 自动建表 + 跑幂等种子。

---

## 0. 场景判定（先看你是哪种）

| 场景 | 数据怎么办 | 用哪节 |
|---|---|---|
| **同机重部署**（改代码、升级镜像，同一台服务器） | **卷 `cp6-db-data` 保留 → 数据/词条不丢** | §1 |
| **搬到新服务器 / 新环境**（新机器、新云） | 新卷 = 空库 → **必须把数据搬过去** | §2（带数据）|
| **全新干净环境**（不要旧数据，只要结构 + 基线种子） | 代码优先：迁移 + 种子自动建 | §3 |

> ⚠️ **致命红线**：`docker compose down -v` 的 **`-v` 会删命名卷 → 整库连词条一起抹掉**。同机重部署只用 `docker compose down`（**不带 `-v`**）或 `docker compose up -d --build`。`deploy-to-server.ps1` 里用的是 `down --remove-orphans`（不带 `-v`，安全）。

---

## 1. 同机重部署（数据天然保留）

```bash
cd /opt/cp6
docker compose up -d --build      # 卷 cp6-db-data 保留，数据/词条原样
docker compose ps
```
启动时 `Migrate()` 幂等：库已最新则 no-op，落后则补差。**无需任何数据操作。**

---

## 2. 搬到新服务器 / 新环境（带全部数据）

整库 `.bak` 备份/还原——**数据、结构、schema、存储过程、触发器、词条一锅全带**，零丢失。

### 2.1 源服务器：备份

```bash
# 进项目目录，读 .env 里的 SA 密码到环境
cd /opt/cp6 && export $(grep MSSQL_SA_PASSWORD .env)

docker exec cp6-db mkdir -p /var/opt/mssql/backup
docker exec cp6-db /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C \
  -Q "BACKUP DATABASE CP6DB TO DISK='/var/opt/mssql/backup/CP6DB.bak' WITH COMPRESSION, INIT, FORMAT, STATS=10"

docker cp cp6-db:/var/opt/mssql/backup/CP6DB.bak ./CP6DB.bak
```

### 2.2 传输到新服务器

```bash
scp ./CP6DB.bak  user@NEW_SERVER:/opt/cp6/CP6DB.bak
```

### 2.3 目标服务器：先起 DB 容器，再还原

```bash
cd /opt/cp6 && export $(grep MSSQL_SA_PASSWORD .env)

docker compose up -d cp6-db          # 只起 DB；先别起 cp6-api（避免它边迁移边被覆盖）
# 若 cp6-api 已起：docker compose stop cp6-api

docker exec cp6-db mkdir -p /var/opt/mssql/backup
docker cp ./CP6DB.bak cp6-db:/var/opt/mssql/backup/CP6DB.bak

# ① 查 .bak 里的逻辑文件名（通常是 CP6DB / CP6DB_log）
docker exec cp6-db /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C \
  -Q "RESTORE FILELISTONLY FROM DISK='/var/opt/mssql/backup/CP6DB.bak'"

# ② 还原（逻辑名按 ① 的结果填；REPLACE 覆盖容器首启自建的空 CP6DB）
docker exec cp6-db /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C \
  -Q "RESTORE DATABASE CP6DB FROM DISK='/var/opt/mssql/backup/CP6DB.bak' \
      WITH MOVE 'CP6DB'     TO '/var/opt/mssql/data/CP6DB.mdf', \
           MOVE 'CP6DB_log' TO '/var/opt/mssql/data/CP6DB_log.ldf', \
           REPLACE, RECOVERY, STATS=10"

docker compose up -d cp6-api cp6-web   # 起应用；Migrate() 见库已带数据→幂等补差
docker compose ps
```

### 2.4 跨版本 / 上云（Azure SQL）→ 用 BACPAC

`.bak` 要求目标 SQL Server 版本 ≥ 源。跨版本 / 上托管云改用 BACPAC（结构 + 数据一包）：
```bash
sqlpackage /a:Export /scs:"Server=源;Database=CP6DB;User Id=sa;Password=***;TrustServerCertificate=True" /tf:CP6DB.bacpac
sqlpackage /a:Import /tsn:目标 /tdn:CP6DB /tu:sa /tp:*** /sf:CP6DB.bacpac
```

### 2.5 备选：直接拷数据卷（同 SQL Server 版本、想连库文件一起搬时）
```bash
# 源：停容器 → tar 卷
docker compose stop cp6-db
docker run --rm -v cp6-db-data:/data -v "$PWD":/backup alpine tar czf /backup/cp6-db-data.tar.gz -C /data .
# 目标：解到新卷（新机先 docker volume create cp6-db-data）
docker run --rm -v cp6-db-data:/data -v "$PWD":/backup alpine sh -c "cd /data && tar xzf /backup/cp6-db-data.tar.gz"
```
> 卷拷更"原始"，但要求两端 SQL Server 镜像版本一致、且容器停机操作。**首选仍是 §2.1–2.3 的 `.bak`。**

---

## 3. 全新干净环境（不带旧数据）

```bash
cd /opt/cp6 && docker compose up -d --build   # 空库 → Migrate 建全表 → 跑种子
```
- 结构 + **基线词条**（各 `I18n*ScreenSeed` 种子）+ 默认租户/权限/记账规则等自动就位。
- ⚠️ **但运行期在多语管理 UI 改/加的词条，种子里没有 → 干净环境会缺这部分**。要补：跑 §5 的 `import-langs.sql` 把仓库里的词条灌进去。

---

## 4. 还原后必查清单（搬数据尤其这几条）

- [ ] **🔴 DataProtection 密钥环**：SSO 把租户 `ClientSecret` 用 DataProtection 加密。若密钥环存在容器本地文件系统（未持久化到卷/DB）→ **新环境/容器重建后解不开，SSO 配置失效**。搬迁前确认 keyring 持久化位置，并随迁移带过去。（**这也是个潜在既有隐患，值得单独排查一次。**）
- [ ] **登录/授权**：当前应用用 `sa` 连库（docker-compose 注入）→ **无孤儿用户问题**。若日后改成最小权限专用登录，则还原后要在新实例 `CREATE LOGIN` + `ALTER USER … WITH LOGIN` 重映射 + 按 schema `GRANT`（见 §6 分 schema 后）。
- [ ] **`.env` 密钥**：新服务器要有同套 `.env`（`MSSQL_SA_PASSWORD` / `JWT_SECRET` / `RABBITMQ_PASSWORD` …）。`JWT_SECRET` 换了会使旧 token 全失效（一般可接受）。
- [ ] **collation**：`.bak` 还原保留源库 collation；CJK 词条列是 `nvarchar`（Unicode），存储不受 collation 影响，安全。
- [ ] **迁移校验**：起 `cp6-api` 后看日志，`Migrate()` 应无报错；`SELECT * FROM __EFMigrationsHistory` 与代码迁移链一致。
- [ ] **生产开关**（dev 默认值上生产前逐项核）：`Security:Csrf:Enabled=true`、CORS 收紧到真实前端域、Cookie `Secure`、HTTPS。
- [ ] **词条目检**：登录后界面多语言正常显示（缺词条立刻可见）。

---

## 5. 词条资产化（关键保险，别只靠一份活库）

> `Sys_Lang` 几千条 × 5 语言是项目最值钱的资产之一，目前只活在一个 DB 里。**把它导出进仓库**（`deploy/seed-data/sys_lang.json`），做部署 fallback + 灾备 + 可 diff 的版本历史。

- **导出**（源库 → 仓库）：见 [`export-langs.sql`](./export-langs.sql)。
  ```bash
  docker exec cp6-db /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C \
    -y 0 -h -1 -i /var/opt/mssql/backup/export-langs.sql -o /var/opt/mssql/backup/sys_lang.json
  # 把 sys_lang.json 拷回仓库 deploy/seed-data/ 提交
  ```
- **导入**（仓库 → 任意环境，幂等 upsert）：见 [`import-langs.sql`](./import-langs.sql)。把 `sys_lang.json` 拷进容器后执行，按 `(LangKey, TenantId)` upsert，不重复、不覆盖错。

> 长期理想态：词条**以 DB 为编辑入口、定期导出回仓库**（半自动），让"代码优先部署"（§3）也能产出满词条环境，不被单一活库绑死。

---

## 6. （规划）按模块 SQL schema —— 与搬库正交

分模块 schema（`fin.` / `wms.` / `wf.` …）**完全包含在库内**，`.bak` / BACPAC / 迁移自动整体带走，**不增加搬库复杂度**。唯一要单独管的是 server 级登录 + 按 schema 的 `GRANT`（沉淀为 `deploy/provision-login.sql`）。落地见 WFS / schema 规划（先 `wf` 试点）。
