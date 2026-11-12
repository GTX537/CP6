# W489-01 · 配置预览与固定必填公开字段

按用户接受的[12h执行](../../planning/20261111-codex-12h-business-chain.md)及W488实际出口登记，COORD单对话串行，无子代理。官方status/start完成，两仓`codex/w489-01`复用Main DEV-1/CRM DEV-2，集成席位空闲。最新基线Main `66ba654e79b4c21fcf1f37125e5522465147ecfe`、CRM `65993e5e48c9b7a2d3a4b7d2ae11fea9fe1fefdd`。

[W488](../W488-01/DELIVERY.md)已正常采用Main #956 / CRM #179；准确任务分支删除、固定工作区干净detached、两席官方释放。两主目录安全快进，Main原14,932未跟踪资料路径保留。

## 限定范围与出口

核合法政策组合下配置预览与公开页面固定必填字段的一致性，实际React/BFF/PG先重现再最小修；保存/预览/生效继续保持原政策列表，不暗中扩字段或缩合法域。

W486已使原Schema/Owner接受的政策组合可提交固定必填输入，W485/W487实际公开页面始终要求姓名/需求/同意。当前配置预览直接只渲染政策列表，先用真实组件→BFF→原生Owner/PG核这两种合法组合是否遗漏必填输入；只有实际失败才修呈现与有意义的政策说明。原政策checkbox选择、原保存合同/数据、预览proof/生效及权限保持。

相关浏览器场景覆盖保存/预览/生效、固定必填可见与政策外optional不可见、只读预览不提交业务和存储列表不被扩展；普通配置相关行为与一次Next构建按实际源码变化选择，不跑无关全仓。实际React/BFF浏览器夹具不冒Next SSR/生产TLS。一次集中SPEC/QUALITY审查、正常两仓采用/远端核对、进程/准确分支/工作区/官方席位收尾。11h后集中当前任务与12h结算。

唯一报告本文件，原始输出`D:/CP6-local-test/CP6-12H-20261111/W489-01`；两个开发席位本任务独占、尚未释放。原AC37/60、修订2/247、375及PG16/11保持；无Actions或部署，12h继续ACTIVE。

## 实际缺陷与最小修复

[CRM Issue #180](https://github.com/GTX537/CP6.CRM/issues/180)，P2。`preview-red-01/results.trx`两项真实失败（23秒、0跳过）：原Owner接受并保存/生成预览的两种4字段政策，分别在实际预览中缺少同意告知或姓名/需求，浏览器报必填控件不存在。不是静态字段数推断或夹具SQL失败。原失败与修前组件bundle保留。

`ConfigurationPage.tsx`只修呈现：预览展示固定姓名/需求/同意及政策允许的可选字段，保留disabled只读、原Previewed/dirty/权限门；原checkbox和保存体完全不变。政策说明明确固定必填、可选显示与导出字段的不同作用，管理员可据此判断实际采集内容。没有把三个固定字段暗中加回原政策列表，没有缩小Owner/Schema接受域或改原接口/权限。

## 实际验证与复用

- `preview-green-02/results.trx`：2 PASS/0跳过，17秒。实际React组件→Node BFF→原生HTTP/PG完成保存/预览/生效：固定必填可见，政策外optional不可见、显示控件均只读，预览没有提交业务按钮；最终原配置政策仍精确原四项，配置/生命周期计数符合原协议，原会话不变。未来生效保持Scheduled，不冒当前公开环境切换。
- `default-lifecycle-03/results.trx`：1 PASS/0跳过，9秒。按原DisplayName只选既有默认lifecycle，核默认六字段、三次保存/预览/生效和页面重读；桌面/移动实际检查正常。没有运行其余未改的10个浏览器场景。
- `ui-build-red-01.log`：配置相关9项API/editor/BFF测试实际成功，限定类型编译及组件bundle成功。UI修正未改这些测试的源码/依赖/输入，绿色bundle只重编变化组件，9项有效结果直接复用，日志明确没有重复执行。
- `next-build-01.log`：因客户端源码改变，实际Next16.3.3 `next build --webpack`成功，包含完整应用TypeScript检查；未用修前构建冒修复后证据。

实际.NET SDK8.0.424/Node22.22.0/Chromium/PostgreSQL18，隔离端口55570，明确模拟IAM/业务输入；应用组件、签名BFF、Owner和数据库实际运行。浏览器夹具只模拟页面递送，不冒Next SSR/生产TLS；Next构建另列，不把构建成功冒页面业务执行。已实际查看`ui-green-02/required-fields-preview.png`，固定必填与原四项政策选择同时可见。原W485/W487实际公开Next链按未变调用范围复用，不重跑无关业务。

## 一次集中审查与剩余范围

执行者对完整产品/测试diff集中SPEC/QUALITY审查一次：静态字段呈现与原公共必填一致，optional/导出政策、保存参数/排序和精确值域保持；只读预览/失权清理/Previewed状态门未改变，原操作/proof/当前权限仍由原入口处理。两个新场景及一个既有默认流程均实际通过，无阻止正常合并的问题。文档仅查diff/必要引用与台账结构，不额外启动审查代理或全仓测试。

本窗口修配置预览的一致性，不扩大为整卡或全量配置行为验收。原询盘37 PASS/23 NOT_RUN、修订2 PASS/245 NOT_RUN、正式PG16 PASS/11 NOT_RUN和375项38 DONE/1 IN_PROGRESS/336 PLANNED保持；WP03-01原DONE范围不重开，活动BUG由Issue #180记录。

自有PG已按settings/Owner/目录/无外来连接核对后smart stop，数据/失败日志保留；HTTP/Node/Chromium子进程均退出。Next可再用缓存核绝对边界后可逆移动至private `retained-generated/web-next`，固定DEV-2保留node_modules。正常采用按下列实际事实核销。

## 远端采用与生命周期

CRM [PR #181](https://github.com/GTX537/CP6.CRM/pull/181)已将源`411fa365ba6ab2eb1ccebd65f2d57c7c3ffa5014`正常合入远端main`21239ca957b54f41b7cc988164e99a92b7c1bfe0`，fetch核祖先及完整树`21a4155da19c79cd2d7ead3bffb38787087d97fe`一致。[Issue #180](https://github.com/GTX537/CP6.CRM/issues/180)在远端采用及官方生命周期完成后关闭。准确本地/远端任务分支删除，DEV-2干净detached供复用、席位已释放，private `crm-finish.json`保留证明。

两仓工作流树与受保护main一致、相关依赖未变：CRM `0c2d74f9fdc63f5178f97baf33950fb3d4dad751`、Main `3355235636fd45116f5445fca32fc65e49f13003`，复用已审手动入口/生产tag过滤；本次普通分支路径不命中生产触发。CRM任务Actions列表为空，无取消项、触发配置更改、远程运行或部署。

Main [PR #957](https://github.com/GTX537/CP6/pull/957)已将源`9b8b3e63ce5dd2fa3da088af87ea61470be70f5d`正常合入远端main`f196c8092231b720b6a13861267238645ad4f197`，fetch核祖先及完整树`aed1205a85b91c1f91c3df31e8787a5deec19b65`一致。官方finish及private `main-finish.json`、实时登记均确认准确本地/远端任务分支删除，DEV-1干净detached供复用、席位已释放。两主目录安全快进，Main原14,932未跟踪资料路径完整保留；任务Actions列表为空。12h原生目标继续ACTIVE，由W490集中本轮结算及下一实现编排。
