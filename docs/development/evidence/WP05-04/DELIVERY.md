# WP05-04 旧版模写入口兼容候选

CP6-EXEC-4 R1 在 `LAPTOP-3QQ44FJS` 的固定 DEV-1 `D:/CP6-worktrees/wave17-dispatch-20261018` 执行。正式准入为 Handoff #433，规定本轮单卡为 WP05-04。候选由总控独立验收。[正式决定](https://github.com/GTX537/CP6.AI-Handoff/blob/63346f1ef08f794211576ac7802aa48cc364c4bb/handoffs/CP6-DEV-20261012/tasks/COORD/replies/20261124T031850Z-coord4-wp05-04-decision/MESSAGE.md)

| 项目 | 精确记录 |
| --- | --- |
| Main 基线 | `a6b4ea9303cab654bf5368e3c1a9666d0ccd1f32` |
| Platform 前置基线 | `f3fbe4be67d40641f7a8a969d74be65a95541819` |
| 测试源码提交及树 | `0c2d5016104ba1ba8a8424c0760acb8547b947db` / `9740a87cdf4a3150a5ca2a7b86e1a1ad50399ab4` |
| 分支与登记 | `codex/wp05-04`，Owner `CP6-EXEC-4`，DEV-1 保持 active 供独立验收 |
| 工时截点 | 正式 START `2026-11-24T03:26:57.329478Z`，至 `2026-11-24T03:58:54.5641870Z` 为 1917.235 秒的本机执行区间。开工前核对另计 |

开工前确认了决定中的 12 项前置记录及对应采用提交的主线祖先关系，涵盖已完成的 WP02-13 与 WP02-13b。共享约束为 `Main:engineering-tool` 与 `Main:erp-plate-mold`。差异从上述 Main 基线比对。报告跟随已验证源码提交，Draft PR 记录含本报告的精确候选提交和树。

## 旧入口

| 旧入口或调用 | 实际路径与结果 |
| --- | --- |
| 创建 | `PlateMoldView.vue` 的显式保存经 `plateMoldApi.create`，`POST /api/plate-molds`，`PlateMoldService.CreateAsync` 接原 `PlateMoldWriteOwnerV1`。使用明确的两项 absent 新草稿前置。 |
| 改版 | 编辑页经 `plateMoldApi.revise`，`PUT /api/plate-molds/{wdPtnNo}/revise`，`PlateMoldService.ReviseAsync` 使用请求中的确切前驱修订。新后继和前驱有效期由原事务提交。 |
| 纠正 | 编辑页经 `plateMoldApi.update`，`PUT /api/plate-molds/{wdPtnNo}/{wdRev}`，`PlateMoldService.UpdateAsync` 仍使用原 Owner。变动生效日起点侵入旧有效区间时返回 `TOOL_EFFECTIVITY_INVALID`。 |
| 删除 | 编辑页经 `plateMoldApi.remove`，`DELETE /api/plate-molds/{wdPtnNo}/{wdRev}`，`PlateMoldService.DeleteVersionedAsync` 接原 Owner。历史保留逻辑删除标志，确切修订仍能打开。旧 `DeleteAsync` 的 PostgreSQL 仅 rowVersion 入口继续返回 `VERSION_REQUIRED`。 |
| 估算导入 | `GET /api/plate-molds/by-estimate-calc/{calcNo}` 读取估算。界面只填对应草稿字段，保留先填的据点、规格输入和操作理由。显式保存仍走创建入口。 |
| 列表、历史、原键 | `PlateMoldListView.vue` 经 `GET /api/plate-molds/list` 读取并传完整编号及修订给编辑页。`GET /api/plate-molds/{wdPtnNo}?rev=` 与 `GET /api/plate-molds/{wdPtnNo}/history` 提供确切读取和历史摘要。两页共享 `GET /api/plate-molds/write-owner-v1/operations/{id}` 的原键查询。编辑页提供原 `POST .../{id}/close`。 |
| 其余旧入口 | `next-seq`、`by-product`、列表 CSV 和标签 CSV 属于读取或输出。Web API 的 WMS 同名控制器、专业规格和商业意图各沿其独立 Owner。当前 ERP 四个写调用均位于旧编辑页。 |

原结果日志、原订单 Owner、版模观察和独立规格草稿复用既有实现。原命令在默认值下继续使用相同的输入序列化。W158 #578 的四写者、字段精度和订单受理，以及 W159 #579 的独立草稿控制和原结果兼容，已逐路径核对。W159 至本基线的相关源码差异是在 `ToolSpecificationOwnerV1` 增加准确来源读取；旧写入口及两页可复用，受影响回归已在本基线重跑。[W158 证据](../W158-01/DELIVERY.md) · [W159 证据](../W159-01/DELIVERY.md)

## 本机验证

| 原断言的本卡范围 | 执行结果 |
| --- | --- |
| AC-E01、AC-E04 旧入口和历史 | **PASS**。原来源保留 20 字符 BaseCd、40 字符 No、原 Rev、decimal(15,4) 及原内容。两条共享旧短码的来源仍是两个独立原件。实际 HTTP 读取 1、2 和缺失的 999，原历史列出逻辑删除的 Rev 2。Chrome 从旧列表准确进入旧编辑页，实际估算 GET 时写入口调用为零，显式保存发原创建命令。 |
| AC-E03 和有效区间 | **PASS**。四个实际 HTTP 写入口分别校验缺失的 native 与技术控制，创建拒绝错误的新草稿前置，三个已有对象入口各自拒绝陈旧的 native 与技术控制。两个 PostgreSQL DbContext 竞争同前驱仅一个合法后继。旧 Rev 1 截至 2026-05-31，Rev 2 从 2026-06-01 生效。纠正覆盖早期有效区间时拒绝且计数不变。Chrome 已证冲突留住输入和两项原版本，删除选项在历史响应未到达时禁用。 |
| AC-E16 的版模原键 | **PASS**。改版提交确认故障前，原键查得 `UNKNOWN`，原生、订单、原命令计数为 `1/2/1`。丢失提交确认后，原键查得同一 Rev 2 与原回执，计数 `2/3/2`，同键回放后仍为 `2/3/2`。Chrome 中实际删除上游返回 200，本机故障点丢弃成功应答，浏览器收到 503。旧列表按同键查得 `COMMITTED`，打开确切已删除 Rev 2，查询和导航后的新增写请求为 0。两页均实际呈现未提交键的 `UNKNOWN`。 |
| AC-E13 的旧入口副作用 | **PASS**。实际纠正与删除从原 Order Owner 得到 `CHANGE_REQUEST/CANCEL_REQUEST`，商业状态为 `REQUESTED`。已确认订单和行的原 RowVersion 保持，合成实物记录的身份、状态和使用计数保持。技术草稿、原版模状态、实物和 PE 的状态分别呈现。 |

PostgreSQL 18.6 运行在本机独立实例，Main Kestrel 与浏览器代理只绑定回环地址，使用合成角色、估算、版模、订单和实物记录。Chrome `157.0.8103.78` 使用 Vite 产物和实际登录、API，没有以组件桩替代产品请求。隔离实例按测试夹具结束，业务外部服务没有参与。合成夹具采用语言包缺省状态，截图的旧标签显示翻译键。

- **PASS**　PostgreSQL 与实际 HTTP 29 项。`backend-functional.trx` 中 26 个成功用例的产品输入未变。首次 HTTP 用例的实物夹具追踪项触发原事务入口，清理测试追踪器后，`backend-http-browser.trx` 的同一 HTTP 用例通过。`backend-identity.trx` 有 2 项完整身份和原订单关联回归。
- **PASS**　Chrome 1 项完成全场景，见 `backend-browser-proof.trx` 和浏览器原件。此前浏览器原件保留了删除加载状态的产品修正及本机故障点的测试夹具调整。
- **PASS**　Vitest 50 项，涵盖两页、原来源页和路由。`vue-tsc --build`、Vite、`git diff --check` 通过。
- **NOT_RUN**　SQL Server、生产、真实授权、真实邮件或外部 PE、完整 LCM/ENG 退役和影响、DOC、WMS 实物报废、原订单请求的实际应用、其它 Owner 的关联轮次、旧 CSV/标签业务全量回归及 OA。原验收目录由总控按各自范围裁决。

## 原件定位

原件根 `C:/Users/tt/CP6-test-artifacts/WP05-04/20261124T032657Z-exec4-r1`，包含以下文件与失败重试原件。原始合成数据库的凭据文件独立存于夹具私有目录。

| 相对路径 | SHA-256 |
| --- | --- |
| `start.json` | `d5867941196159761fc341df9ac718de59b4b14cf2e54a9f276e9706766af196` |
| `backend-functional.trx` | `e5f7e091758246fa741c51da52117c2a360357c3e94cf674e5a6a1b39f8fac77` |
| `backend-http-browser.trx` | `9534b8bd7f4417e034b448fc9db1f6c3a18a387c4b151eabb9db8891f16895f7` |
| `backend-identity.trx` | `461f2b26b6ab154a1be324fcbb9982e10316e42c4b4c6bf1aae947741df657b9` |
| `backend-browser-proof.trx` | `072869df94a3e9973645fec0da7b871baac4bcf04ed572ca2725c4221b5c0620` |
| `web-components-final.log` | `7b46fe990f8cbed028f3bb81174880e2e9cc2c8029552f481d5649baa59bb96e` |
| `web-types-final.log` | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |
| `web-build-final.log` | `a4891abdc9dfb6c73fcde684cbc8fb04ca53ef3f5657673ee6047fe92b5bc6da` |
| `permission-consumption-wp0504-functional/wp05-04-lost-commit-ack.json` | `440ce5485b6e7ff14269a53cf973a6bd81ccc78453156de5e4c4ec1397c91635` |
| `permission-consumption-wp0504-browser-proof/wp05-04-browser-proof.json` | `75396799c474090f3a1e16fabeef5445d520534388d8a6a47cd96bc90fbcd89a` |
| `permission-consumption-wp0504-browser-proof/plate-mold-conflict.png` | `b024e392934fb37a7b22e445aced6ba6eed5925b8ed85e4b5a68e5309509617c` |
| `permission-consumption-wp0504-browser-proof/plate-mold-list-recovery.png` | `c0f018d0bd5efd2863076d4e2afcdc732ebc81cfa88797ee12fe26a113fa4f4a` |
| `permission-consumption-wp0504-browser-proof/plate-mold-deleted-history.png` | `42213fd0071da13d7ab1206a61681ebc715ecc799a108da5a3cd4df7ea2d6c26` |

## 工时结转

按 Handoff #433，对精确 Main 的 `docs/development/roadmap.json` 分别查找原 DELIVERY 的完整 SHA `1c9466cfba8b365ab4361b9aeea945f02a9e4ec5` 与路径 `handoffs/CP6-DEV-20261012/tasks/WP12-20/messages/20261124T014830Z-cp6-exec-3-delivery-closeout/MESSAGE.md`，均为零。WP12-20 的 1175.322 秒待合资格 Main 台账 PR 再次查重后单次结转。当前 Draft 待独立验收，台账尚未入账。既有 WP12-15 的 714.019 秒及 WP12-14 的 754.861 秒沿用。[原 DELIVERY](https://github.com/GTX537/CP6.AI-Handoff/blob/1c9466cfba8b365ab4361b9aeea945f02a9e4ec5/handoffs/CP6-DEV-20261012/tasks/WP12-20/messages/20261124T014830Z-cp6-exec-3-delivery-closeout/MESSAGE.md)
