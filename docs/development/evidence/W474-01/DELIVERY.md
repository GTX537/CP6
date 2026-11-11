# W474-01 — 含实际 RMA Owner 回执的同源 v11

WP02-02 从远端主线 `88528c6cf2d1f5110ae8e5b00e88567daaa1c4de` 复用 DEV-2。核对 W372 实际草稿与提交原件，从保留的停机 PostgreSQL 目录制作一个私有测试副本。前后核对原控制文件、原路径、系统标识和数据目录。原库始终停机。[W372 实际 Owner 交付](../W372-01/DELIVERY.md) · [归档入口](../../../../eng/wp02-02/rma-tenant-archive/archive.py)

- 首轮 PostgreSQL 18.6 只读采集完整 v11 返回 `privacy-evidence-environment-required`，未生成 manifest。同一源实际导出 v8 通过。观察到环境和所选租户的 Stock 路由各为空。
- 只在私有副本内用原认证栅栏提交模拟环境绑定和一条原生空 Stock 路由。两租户 RMA Owner 原流水的独立原生 COPY 前后逐字节一致。正式 v11 导出通过现有全包校验，使用同一 PostgreSQL holder。清单 SHA-256 `5c658f31c926b6f2f6f58958a52d0a06ddbd8795131002c8c1875562ab21ef86`，含 41 个成员，1 个 RMA head、2 个原 version、1 条共享出库引用和 2 条原 `COMMITTED` Owner 回执。
- 两条实际草稿和提交回执的原 CSV 共 3,443 字节，SHA-256 `a1a516a5a3a7c2293344351d813d82d2ba756c37b57272da94293f57fc5c54ed`，与 W372 实际宿主测试原 v2 CSV 逐字节相同。使用原业务模拟数据，原授权、journal 字节及库布局由现有入口校验。FileContent 原成员为空，Stock 路由无待发消息。回执和原文件留在本机私有证据根，`recoveryReady=false`。

v11 包在 `D:/CP6-local-test/CP6-72H-20261109/W474-01/native-source-02/v11/bundle`。同根保存复制前后摘要、首轮拒绝、配置事务、原生 COPY、导出和停机。两次专属 PostgreSQL 均已停止。GitHub Actions、应用宿主、部署均未运行。

W473 经 #941 采用并释放，实际 10.13–10.19 分钟。接下来用本包验证同一关闭候选的 RMA 两表和原流水导入。正式 PGT-021/022 和切换分别验收。原 375 项为 38 DONE、1 IN_PROGRESS、336 PLANNED，正式 PostgreSQL 16 PASS、11 NOT_RUN。
