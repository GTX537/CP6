# PLT-CTX-01 目标核对

结论：**目标未关闭，3 项缺口已归到现有未完成卡**。覆盖卡 WP02-01/WP02-02 完整 DONE 不等于实际所有消费者完成。逐项核对 28 条原 AC，必需修订运行场景为 0 条。保留原 AC 状态，不把库测试批量提升为全部宿主验收。

| 原 AC | 证据/准确剩余 | 核对结论 |
|---|---|---|
| CTX01-A1：可信resolver成功时应用观察精确快照且结束Current为空 | [WP02-01 原库验收](https://github.com/GTX537/CP6.Platform/blob/d5ab56de99a3c9a3db0b58d7ae6e2540d05c72c6/docs/development/evidence/WP02-01/ACCEPTANCE.md)；[W132](../W132-01/DELIVERY.md)、[W253](../W253-01/DELIVERY.md)：实际 HTTP/后台上下文及双组织旧会话失效，未泛称全部宿主入口 | 已有相应证据；整目标仍待3项缺口 |
| CTX01-A2：null/Empty tenant拒绝且业务未进入 | [WP02-01 原库验收](https://github.com/GTX537/CP6.Platform/blob/d5ab56de99a3c9a3db0b58d7ae6e2540d05c72c6/docs/development/evidence/WP02-01/ACCEPTANCE.md)；[W132](../W132-01/DELIVERY.md)、[W253](../W253-01/DELIVERY.md)：实际 HTTP/后台上下文及双组织旧会话失效，未泛称全部宿主入口 | 已有相应证据；整目标仍待3项缺口 |
| CTX01-A3：异常和取消清理，不串下个请求 | [WP02-01 原库验收](https://github.com/GTX537/CP6.Platform/blob/d5ab56de99a3c9a3db0b58d7ae6e2540d05c72c6/docs/development/evidence/WP02-01/ACCEPTANCE.md)；[W132](../W132-01/DELIVERY.md)、[W253](../W253-01/DELIVERY.md)：实际 HTTP/后台上下文及双组织旧会话失效，未泛称全部宿主入口 | 已有相应证据；整目标仍待3项缺口 |
| CTX01-A4：浏览器tenant/actor字段不能覆盖可信绑定 | [WP02-01 原库验收](https://github.com/GTX537/CP6.Platform/blob/d5ab56de99a3c9a3db0b58d7ae6e2540d05c72c6/docs/development/evidence/WP02-01/ACCEPTANCE.md)；[W132](../W132-01/DELIVERY.md)、[W253](../W253-01/DELIVERY.md)：实际 HTTP/后台上下文及双组织旧会话失效，未泛称全部宿主入口 | 已有相应证据；整目标仍待3项缺口 |
| CTX01-A5：worker独立scope与当前service身份，历史requester不冒执行身份 | [WP02-01 原库验收](https://github.com/GTX537/CP6.Platform/blob/d5ab56de99a3c9a3db0b58d7ae6e2540d05c72c6/docs/development/evidence/WP02-01/ACCEPTANCE.md)；[W132](../W132-01/DELIVERY.md)、[W253](../W253-01/DELIVERY.md)：实际 HTTP/后台上下文及双组织旧会话失效，未泛称全部宿主入口 | 已有相应证据；整目标仍待3项缺口 |
| CTX01-A6：tenantless健康路径不构造假租户也不访问业务 | [WP02-01 原库验收](https://github.com/GTX537/CP6.Platform/blob/d5ab56de99a3c9a3db0b58d7ae6e2540d05c72c6/docs/development/evidence/WP02-01/ACCEPTANCE.md)；[W132](../W132-01/DELIVERY.md)、[W253](../W253-01/DELIVERY.md)：实际 HTTP/后台上下文及双组织旧会话失效，未泛称全部宿主入口 | 已有相应证据；整目标仍待3项缺口 |
| CTX01-A7：401/403/503分类和旧Host兼容准确，UI切组织后不回显旧资料 | [WP02-01 原库验收](https://github.com/GTX537/CP6.Platform/blob/d5ab56de99a3c9a3db0b58d7ae6e2540d05c72c6/docs/development/evidence/WP02-01/ACCEPTANCE.md)；[W132](../W132-01/DELIVERY.md)、[W253](../W253-01/DELIVERY.md)：实际 HTTP/后台上下文及双组织旧会话失效，未泛称全部宿主入口 | 已有相应证据；整目标仍待3项缺口 |
| CTX02-A1：tenant GUID稳定且organizationId等值 | [WP02-01 原库验收](https://github.com/GTX537/CP6.Platform/blob/d5ab56de99a3c9a3db0b58d7ae6e2540d05c72c6/docs/development/evidence/WP02-01/ACCEPTANCE.md)；[WP02-02 收口](../W677-01/DELIVERY.md)、[PGT 27/27 收口](../W677-01/DELIVERY.md)：真实 PG 映射/会话/事务上下文与隔离；不把 schema 自身当授权 | 已有相应证据；整目标仍待3项缺口 |
| CTX02-A2：未注册region或issuer不路由其他库 | [WP02-01 原库验收](https://github.com/GTX537/CP6.Platform/blob/d5ab56de99a3c9a3db0b58d7ae6e2540d05c72c6/docs/development/evidence/WP02-01/ACCEPTANCE.md)；[WP02-02 收口](../W677-01/DELIVERY.md)、[PGT 27/27 收口](../W677-01/DELIVERY.md)：真实 PG 映射/会话/事务上下文与隔离；不把 schema 自身当授权 | 已有相应证据；整目标仍待3项缺口 |
| CTX02-A3：snapshot与数据库选择同登记版本可追溯 | [WP02-01 原库验收](https://github.com/GTX537/CP6.Platform/blob/d5ab56de99a3c9a3db0b58d7ae6e2540d05c72c6/docs/development/evidence/WP02-01/ACCEPTANCE.md)；[WP02-02 收口](../W677-01/DELIVERY.md)、[PGT 27/27 收口](../W677-01/DELIVERY.md)：真实 PG 映射/会话/事务上下文与隔离；不把 schema 自身当授权 | 已有相应证据；整目标仍待3项缺口 |
| CTX02-A4：暂停不改历史tenant，read与新作用分门 | [WP02-01 原库验收](https://github.com/GTX537/CP6.Platform/blob/d5ab56de99a3c9a3db0b58d7ae6e2540d05c72c6/docs/development/evidence/WP02-01/ACCEPTANCE.md)；[WP02-02 收口](../W677-01/DELIVERY.md)、[PGT 27/27 收口](../W677-01/DELIVERY.md)：真实 PG 映射/会话/事务上下文与隔离；不把 schema 自身当授权 | 已有相应证据；整目标仍待3项缺口 |
| CTX02-A5：改slug不重建身份，改region不自动迁数据 | 批准 slug/身份来源映射变更的正向链：稳定租户 GUID、准确新来源和完整后继目录，不自动迁移 region 数据。冻结配置/错误来源拒绝不能替代批准变更。 归 WP02-05b | 待归属卡完成 |
| CTX02-A6：context snapshot保持原格式，旧消费者兼容缺evidence时不冒新版保证 | [WP02-01 原库验收](https://github.com/GTX537/CP6.Platform/blob/d5ab56de99a3c9a3db0b58d7ae6e2540d05c72c6/docs/development/evidence/WP02-01/ACCEPTANCE.md)；[WP02-02 收口](../W677-01/DELIVERY.md)、[PGT 27/27 收口](../W677-01/DELIVERY.md)：真实 PG 映射/会话/事务上下文与隔离；不把 schema 自身当授权 | 已有相应证据；整目标仍待3项缺口 |
| CTX02-A7：映射变化在提交门被识别，无同对象双库写 | [WP02-01 原库验收](https://github.com/GTX537/CP6.Platform/blob/d5ab56de99a3c9a3db0b58d7ae6e2540d05c72c6/docs/development/evidence/WP02-01/ACCEPTANCE.md)；[WP02-02 收口](../W677-01/DELIVERY.md)、[PGT 27/27 收口](../W677-01/DELIVERY.md)：真实 PG 映射/会话/事务上下文与隔离；不把 schema 自身当授权 | 已有相应证据；整目标仍待3项缺口 |
| CTX03-A1：合法配置下有效service token经应用登记才获得相应范围 | [WP02-01 原库验收](https://github.com/GTX537/CP6.Platform/blob/d5ab56de99a3c9a3db0b58d7ae6e2540d05c72c6/docs/development/evidence/WP02-01/ACCEPTANCE.md)；[W131](../W131-01/DELIVERY.md)、[W246](../W246-01/DELIVERY.md)、[W253](../W253-01/DELIVERY.md)、[W645](../W645-01/DELIVERY.md)：真实身份边界、早退前门、最终撤权竞争和旧会话 | 已有相应证据；整目标仍待3项缺口 |
| CTX03-A2：人类/服务audience分离且未知必需claim拒绝 | [WP02-01 原库验收](https://github.com/GTX537/CP6.Platform/blob/d5ab56de99a3c9a3db0b58d7ae6e2540d05c72c6/docs/development/evidence/WP02-01/ACCEPTANCE.md)；[W131](../W131-01/DELIVERY.md)、[W246](../W246-01/DELIVERY.md)、[W253](../W253-01/DELIVERY.md)、[W645](../W645-01/DELIVERY.md)：真实身份边界、早退前门、最终撤权竞争和旧会话 | 已有相应证据；整目标仍待3项缺口 |
| CTX03-A3：过期/无有效key/不可用配置不扩大权限 | [WP02-01 原库验收](https://github.com/GTX537/CP6.Platform/blob/d5ab56de99a3c9a3db0b58d7ae6e2540d05c72c6/docs/development/evidence/WP02-01/ACCEPTANCE.md)；[W131](../W131-01/DELIVERY.md)、[W246](../W246-01/DELIVERY.md)、[W253](../W253-01/DELIVERY.md)、[W645](../W645-01/DELIVERY.md)：真实身份边界、早退前门、最终撤权竞争和旧会话 | 已有相应证据；整目标仍待3项缺口 |
| CTX03-A4：token signature有效但已撤销/tenant停用仍按当前应用门拒绝新作用 | [WP02-01 原库验收](https://github.com/GTX537/CP6.Platform/blob/d5ab56de99a3c9a3db0b58d7ae6e2540d05c72c6/docs/development/evidence/WP02-01/ACCEPTANCE.md)；[W131](../W131-01/DELIVERY.md)、[W246](../W246-01/DELIVERY.md)、[W253](../W253-01/DELIVERY.md)、[W645](../W645-01/DELIVERY.md)：真实身份边界、早退前门、最终撤权竞争和旧会话 | 已有相应证据；整目标仍待3项缺口 |
| CTX03-A5：known rotation只在固定issuer/profile与有界缓存规则内 | [WP02-01 原库验收](https://github.com/GTX537/CP6.Platform/blob/d5ab56de99a3c9a3db0b58d7ae6e2540d05c72c6/docs/development/evidence/WP02-01/ACCEPTANCE.md)；[W131](../W131-01/DELIVERY.md)、[W246](../W246-01/DELIVERY.md)、[W253](../W253-01/DELIVERY.md)、[W645](../W645-01/DELIVERY.md)：真实身份边界、早退前门、最终撤权竞争和旧会话 | 已有相应证据；整目标仍待3项缺口 |
| CTX03-A6：响应/日志无token/secret/PII泄露 | [WP02-01 原库验收](https://github.com/GTX537/CP6.Platform/blob/d5ab56de99a3c9a3db0b58d7ae6e2540d05c72c6/docs/development/evidence/WP02-01/ACCEPTANCE.md)；[W131](../W131-01/DELIVERY.md)、[W246](../W246-01/DELIVERY.md)、[W253](../W253-01/DELIVERY.md)、[W645](../W645-01/DELIVERY.md)：真实身份边界、早退前门、最终撤权竞争和旧会话 | 已有相应证据；整目标仍待3项缺口 |
| CTX03-A7：原CRM browser与Main service profile没有被通用库误替换 | [WP02-01 原库验收](https://github.com/GTX537/CP6.Platform/blob/d5ab56de99a3c9a3db0b58d7ae6e2540d05c72c6/docs/development/evidence/WP02-01/ACCEPTANCE.md)；[W131](../W131-01/DELIVERY.md)、[W246](../W246-01/DELIVERY.md)、[W253](../W253-01/DELIVERY.md)、[W645](../W645-01/DELIVERY.md)：真实身份边界、早退前门、最终撤权竞争和旧会话 | 已有相应证据；整目标仍待3项缺口 |
| CTX04-A1：同应用每个HTTP/worker/consumer入口各有精确adoption记录 | CRM 实际 HTTP/worker/消息入口的平台包、准确配置、加载身份和原生 Owner 边界采用未整卡闭合；已有 W132/W143 部分采用保留，不把模块文件匹配或受控加载器当完整实际宿主资格。 归 WP02-29 | 待归属卡完成 |
| CTX04-A2：包/注册/启用/运行分层，RecordingStub不算生产 | CRM 实际 HTTP/worker/消息入口的平台包、准确配置、加载身份和原生 Owner 边界采用未整卡闭合；已有 W132/W143 部分采用保留，不把模块文件匹配或受控加载器当完整实际宿主资格。 归 WP02-29 | 待归属卡完成 |
| CTX04-A3：CRM浏览器、sidecar、Main service身份职责不混 | CRM 实际 HTTP/worker/消息入口的平台包、准确配置、加载身份和原生 Owner 边界采用未整卡闭合；已有 W132/W143 部分采用保留，不把模块文件匹配或受控加载器当完整实际宿主资格。 归 WP02-29 | 待归属卡完成 |
| CTX04-A4：S6前置内容门之前无Duplicate/IgnoredOutOfOrder成功早退 | [WP02-01 原库验收](https://github.com/GTX537/CP6.Platform/blob/d5ab56de99a3c9a3db0b58d7ae6e2540d05c72c6/docs/development/evidence/WP02-01/ACCEPTANCE.md)；[W131](../W131-01/DELIVERY.md)、[W246](../W246-01/DELIVERY.md)、[W253](../W253-01/DELIVERY.md)、[W645](../W645-01/DELIVERY.md)：真实身份边界、早退前门、最终撤权竞争和旧会话 | 已有相应证据；整目标仍待3项缺口 |
| CTX04-A5：缺共同最终门只关闭相关新效果，合法历史读取保留 | [WP02-01 原库验收](https://github.com/GTX537/CP6.Platform/blob/d5ab56de99a3c9a3db0b58d7ae6e2540d05c72c6/docs/development/evidence/WP02-01/ACCEPTANCE.md)；[W131](../W131-01/DELIVERY.md)、[W246](../W246-01/DELIVERY.md)、[W253](../W253-01/DELIVERY.md)、[W645](../W645-01/DELIVERY.md)：真实身份边界、早退前门、最终撤权竞争和旧会话 | 已有相应证据；整目标仍待3项缺口 |
| CTX04-A6：旧新单writer可追溯，不停旧consumer或自动发布包 | [WP02-01 原库验收](https://github.com/GTX537/CP6.Platform/blob/d5ab56de99a3c9a3db0b58d7ae6e2540d05c72c6/docs/development/evidence/WP02-01/ACCEPTANCE.md)；[W131](../W131-01/DELIVERY.md)、[W246](../W246-01/DELIVERY.md)、[W253](../W253-01/DELIVERY.md)、[W645](../W645-01/DELIVERY.md)：真实身份边界、早退前门、最终撤权竞争和旧会话 | 已有相应证据；整目标仍待3项缺口 |
| CTX04-A7：typed replay三动作经应用Owner处理，Platform Context不能直接允许重排 | CRM 三种 typed replay action 的 native type/digest/队列版本、当前读取与新调度权限分流，以及实际 Owner 原业务/技术结果仍缺完整采用；身份 Inbox 恢复不是三种动作全覆盖。 归 WP02-24 | 待归属卡完成 |

## 缺口与归属

1. **WP02-05b**：批准 slug/身份来源映射变更的正向链：稳定租户 GUID、准确新来源和完整后继目录，不自动迁移 region 数据。冻结配置/错误来源拒绝不能替代批准变更。
2. **WP02-29**：CRM 实际 HTTP/worker/消息入口的平台包、准确配置、加载身份和原生 Owner 边界采用未整卡闭合；已有 W132/W143 部分采用保留，不把模块文件匹配或受控加载器当完整实际宿主资格。
3. **WP02-24**：CRM 三种 typed replay action 的 native type/digest/队列版本、当前读取与新调度权限分流，以及实际 Owner 原业务/技术结果仍缺完整采用；身份 Inbox 恢复不是三种动作全覆盖。

三项均落在对应卡既有 scope/acceptance 内；无新建卡、无依赖分级、无扩大实际加载核验为额外生产部署。后续归属卡 DONE 的 PR 必须重新核对；只关闭卡而目标仍开时，台账工具会拒绝继续引用已 DONE 的缺口归属卡。

## 远端采用与证据边界

Main `1a1f2021c8975bd3555d579dd7312eb9d67fdfcd`；Platform `f3fbe4be67d40641f7a8a969d74be65a95541819`；CRM `5f09533b9168ee614a6df1f75629b39635585f86` 均经 fetch 核对。

- Platform WP02-01：源码 `d5ab56de99a3c9a3db0b58d7ae6e2540d05c72c6` → `9259a45d39bae7e98ca804a74778a588c1ad4619`，[PR59](https://github.com/GTX537/CP6.Platform/pull/59)，均是当前远端祖先。502 项库检查和 98 项架构检查仅作该层证据。
- CRM W132：`634d127181bfe1d11f51b58df0a03d2c6d144ef3` → `de00251a50e50e9a4750c34efb526d172569a9ab`，[PR156](https://github.com/GTX537/CP6.CRM/pull/156)；W143：`fb224f2b71a9d0148b2b87a257a5607768e7fec2` → `2e159bda59d7464a6f3fcdbb1c227cd30a17e7b2`，[PR160](https://github.com/GTX537/CP6.CRM/pull/160)，均是当前远端祖先。
- W253 双组织 OIDC 使用隔离模拟 IdP 和真实 CRM HTTP/PG；W131/W246 的最终门及冲突原键恢复不是 typed replay 三动作完成证明。原运行日志、失败记录和来源不改写。

本次未新跑产品测试，未操作生产或变更公共合同。SYS-TENANT-01 的 AC-TN04-04 同时归 WP02-05b（Main 发布）和 WP02-06（CRM 实际采用），每段独立登记；WP02-06 DONE PR 重新核对整个目标原 AC 后关闭，不能提前关闭。
