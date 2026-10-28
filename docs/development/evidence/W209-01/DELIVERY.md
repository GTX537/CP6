# W209-01 — 库位类型化规则候选与独立入口

COORD 单人；持续目标 CP6-7D-20261022 保持 ACTIVE。预计实际50–85分钟；本次登记截至资料同步 17.52 分钟，最后START→finish实际时间写入本机final-proof，不把Handoff并行时间重复相加。

## 改动和实际行为

[合同](../../location-rule-proposal-v1.md)落实接受规范4.1.2/4.3/7.1及08D/08W。新增 Main.Wms.LocationRuleProposal.v1，规则候选独立权限和字段权，作用域复用真实原生库位/仓库/区域/部门。支持旧库位和隔离草稿；独立页面能新建、严格差分更新、历史查询、撤回及原操作恢复。

LIMITED（含有限0）、UNLIMITED、UNKNOWN 有独立类型，decimal21,8字符串不丢精度；合法外域字符串引用/版本、明确空集合、false及带偏移时刻保留。Selector按Tenant/Location/family/metric/domain稳定归域，域政策版本不产生第二个选择器。候选不可变版本、ETag/CAS、同值NO_CHANGE、撤回对象前驱、Journal/审计同事务；原生触发器、FK和延迟原操作证明保护原件。

字段权限逐项披露，部分隐藏时完整OriginalJson不返回；浏览器仅UUID、当前身份、WebLocks、原键回查/关闭，UNKNOWN不重发。审查修正了“Selector隐藏但Capacity有读取/写入权”时页面应从可读容量metricRef保持身份并只发容量差分的路径。

## 验证及真实失败

74 distinct PG18/Program HTTP; 5 ancillary checks; 44 distinct Vue;types/build PASS;0skip。本机证据 `D:/CP6-local-test/wave209/foundation/final-validation.json` 按用例映射原始来源和最终源文件；本地结果不作为生产/现场正式验收。最终验证无跳过。

首轮13项中6通过、7失败均已保留：5个范围夹具错误地尝试通过旧IAM入口重绑隔离库位，已改为真实创建事务部门票据；1个隐藏字段枚举0不合法，改用既有3；1个时区原文检查未解析JSON转义，改验实际保存偏移。原子创建、版本/撤回竞争、HTTP及隔离回归的实际行为均由后续结果证明。

一次Warehouse_子串筛选额外命中Space等检查；两项Space OpenAPI检查因复用artifacts目录的AppContext相对输入缺失失败。只为测试提供源内容完全相同的一份契约输入，原文件/用例未改，两项独立重验结果另记。不是业务失败被跳过，也未复制源码工作树。未改变的相关成功结果按实际来源复用，不把额外检查数当业务完成比例。

## 剩余范围

当前ReferenceResolution明确UNRESOLVED，实际指标/单位/精度/适用域/动作字典注册、送审、真实批准引用、规则应用/可信时钟和库存/预占Owner保护均未冒称完成。空Selector0/null无许可，不回写旧容量，不释放数量、不清W206/W208隔离、不启用库位；正式MASTER/Stock/RSV/Space/Transfer以及原D2/D3/D5/D6/D7出口保留。原125目标/247正式验收和完整任务状态未改。

## 交付与工作区

Handoff正常合并 [交付PR](https://github.com/GTX537/CP6.AI-Handoff/pull/185)，源 `f97f5fa7c26983f43d237d958e1fa8970c169c76`，目标 `4488c8de2097482ee7bfa970dc9b258abd16633e`，完整树采用已核实，准确任务分支已删除且DEV-2已释放。Main当前为已验证待正常PR/finish的真实快照；最终采用、分支删除、进程及席位释放按官方入口完成并写本机final-proof，下一次真实准入只归档一次，不伪写本提交自己的未来结果。

复用两个固定席位，无新增工作树/跨仓重绑；用户原有文件及依赖缓存保留。本轮无Actions运行或取消、无触发配置变更；推送/PR前重核候选及目标工作流/依赖内容。无既有环境迁移或生产部署。
