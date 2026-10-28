# W201-01：原生完整输出关系与来源检查采用

COORD 单人；Main 基线 `1cfa32b20e295cba9dc61ccdda488d07cebfdafb`，固定 DEV-1/DEV-2 复用。预计150–240分钟；本报告写入时从 Main 官方 START 已登记 73.09 分钟，最终耗时以本机 `wave201-final-verification.json` 的 START→finish 为准，Handoff 不重复计时。

## 实际行为

- 增加独立 `/plm/engineering-output-relations` API/菜单/页面、原操作恢复，以及 PostgreSQL 原件、控制、政策和专业确认记录。父修订的可信部门范围、租户及来源读权共同核对；不自动授权菜单动作。
- 从实际制造/EBOM/MBOM/Routing/配置、工程产品、质量规格与受控文档构造完整 `ENG-OUTPUT-RELATIONS/1`。保留各个输入/工序/输出位置、同产品在不同工序的多个输出、合法有依据的无输入生成和中间产物流向；拒绝不完整位置、错产品规格、异配置和伪无输入依据。原数量/配比字符串及完整原制造内容保持准确。
- 内容与当前控制分开保存。独立专业角色、政策有效期及真实当前权限共同约束 QUALIFY；作者不能自确认。REVISE/POLICY 使原资格失效，WITHDRAW 按 CAS 撤回。准确内容/政策/签署人/来源原件在新来源检查中完整保留，最终同事务检查阻止来源或授权变化后提交。
- 来源检查绑定准确关系、当前产品/配置及 EBOM/MBOM/Routing；新关系摘要与原技术 Manifest 独立。只覆盖实际已确认的映射/数量/制造视图或有依据空输入缺项。设备、工装、条件/替代批准、工艺文档和冻结仍保留真实阻断，不能凭局部关系资格获得生产用途。
- 页面实际选择完整制造版本及每个位置的准确规格/批准文档；专业政策/确认/撤回、历史/差异、身份隔离及原键查询/关闭均可操作。浏览器只保存恢复身份，不存私有业务正文、不自动换键重发。

合同：[原生工程输出关系 v1](../../engineering-output-relations-v1.md)。迁移只在隔离 PostgreSQL18 验证；未运行既有环境迁移。

## 实际验证与集中审查

19 PG18/Program HTTP; 14 Vue;types/build PASS;actual wire/shared schema PASS;0skip。证据目录 `D:/CP6-local-test/wave201/foundation`，总索引 `final-validation.json`。

覆盖真实原生 Owner/FLOW 的质量规格和受控文档前置、多位置/准确数量、独立专业签署、规格撤回/配置变更、来源当前授权、同事务来源变化回滚、双连接 CAS、租户隔离、历史/差异、未知关闭/同键恢复、存储护栏以及实际 Program HTTP/CSRF。前端覆盖实际来源选择、完整位置保留、CAS 签署、恢复身份冲突、UNKNOWN/CLOSED、拒权/身份切换及旧响应隔离，类型与构建通过。

实际 PostgreSQL 生成的 wire 另以原共享 JSON Schema、独立摘要及原生 bytes/hash/长度检查；合法重复产品的多个位置保持区分，额外成员/重复输出/缺失自身规格的否定边界均拒绝。第一次共享 schema 校验发现 productIdentity 必须携带完整 NativeReference，已修正；实际产物及失败记录保留。首轮夹具重复权限范围、后续共享部门夹具硬编码菜单 ID 冲突的失败也保留；修正夹具后，新关系与原制造部门范围共6项通过，未把失败重标成功。

一次任务级 SPEC/QUALITY 集中审查覆盖完整 diff；后续修正仅回归有影响范围。审查补强了当前配置失效、完整专业确认/政策原文、来源检查 findings 的当前性和有真实依据的无输入合法范围。证据复用来源、适用代码及未验证范围见总索引；没有把静态断言数当作交付量。

## 剩余范围与原目标

当前原件存储有32 MiB整体资源上限，超限明确拒绝并回滚，不截断原文；更大组合的容量与流式处理尚未验收。

本轮为 WP05-10/WP05-11 的真实原生限定交付；完整任务和企业正式验收不关闭。外部 Item/Spec/工程证据提供者的准确注册适配、完整制造资格与正式验收、WP05-12 Baseline/OutputBinding/冻结取消以及 REL/VER/WO/SCHED 仍待对应窗口。原七天 D1/D4 完成；D2/D3/D5/D6/D7 仍按原出口推进，125目标、247正式验收保持真实状态。本轮不以测试通过或轮次数推算整体百分比。

## 分支、工作区与席位

Handoff 正常 PR [https://github.com/GTX537/CP6.AI-Handoff/pull/177](https://github.com/GTX537/CP6.AI-Handoff/pull/177)，源 `c8db134b7ef054b73bba70ef4094e04fa98f0463` 已完整采用为 `10c34b0355f505eafb506f03f21e709cc36a5051`；本任务分支已精确删除、DEV-2 已 finish 释放。Main 在本报告提交时仍是 `codex/w201-01`，待正常 PR、远端完整采用核对及官方 finish；最终 PR/准确分支删除/DEV-1 释放证明由本机 `wave201-final-verification.json` 固定，下一准入仅归档本轮一次。

固定物理目录保留，未新增工作树；Main/CRM 依赖缓存和根目录用户文件保留。普通 Actions 内容/依赖沿用已核对手动入口且本轮无触发、无须取消任何归属运行；不修改保护、不部署生产。
