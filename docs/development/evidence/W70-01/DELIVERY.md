# W70-01 / Issue445 RMA当前Owner与未采用写入阻断

COORD本聊天单人，2026-10-23T16:53:12.535263+00:00。基线Main `56ccc3c7e3e7087ed921426d656108ce1fbf5b24`，DEV-1任务`BUG-445-RMA-OWNER`、分支`codex/bug-445-rma-owner`；DEV-2仅Handoff。唯一产品交付报告；[Issue445](https://github.com/GTX537/CP6/issues/445)。

## 缺陷与当前行为

真实隔离PostgreSQL复现：未传Owner的独立RmaService能读受保护退货，亦能用调用方伪造用户名取消退货。HTTP GET原仅检查登录。旧Create直接变Authorized，Receive拼接仓库RMA-HOLD直调Stock，Judge执行MOVE/ADJ，Close保存后调用贷项桥，均未证明原RMA事实、正式收货或终端Effect Owner采用。

PostgreSQL全部公共入口现在要求同一DbContext的当前原会话OperationsOwner。查询动作、租户、当前角色/字段/组织版本在原生事务中复核；不可用为401/403/503，不能伪装成空列表。RMA头/行与实际来源图保持原生读锁直到最后复核，拒绝外层事务、脏工作和预跟踪业务对象；本轮只为RMA资源扩展RMA表锁，其他Owner锁集合不变。

查询先范围后分页。本人范围核现存头及所有关联行Creator；部门/子部门/自定义范围只沿存储的OriginalShippingNo→存活Shipping Outbound→同客户→实际WebOrderNo→显式OrderDepartment映射。不由仓库、用户部门或自由文本猜归属，没有该来源的部门对象不可见。有来源引用时即便全域也要求实际存活同客户发货。普通角色按当前仓库范围；仓区权限以所有非删除行的真实DestLocation和当前Location关联证明，可合并多个区域授权，未知地点不可见。既有Role1仓库范围语义保留，但不跳过动作、对象、字段或身份。

列表固定不下发CustomerName、OperatorCd、ReturnReason、Remarks、Creator/Modifier；详情四个注册字段按现有当前字段MIN规则投影，明细备注受Remarks控制，源行不修改。现有字段默认/只读/隐藏语义保持，不宣称正式R3 PII用途、完整字段或来源cut映射已采用。

六个旧命令Create/Receive/StartInspection/Judge/Close/Cancel先核当前动作及既有对象（Create没有既有对象），然后503 RMA_OWNER_ADOPTION_UNPROVEN；不取号、不写库存/业务/审计、不调用ERP。正式RMA申请与授权、收货适配器WMS-IN-RMA-ADAPTER-1、质量与商业事实、五类终端Effect Owner/SOD/版本cut未采用，不能保留旧命令绕过。历史非PG服务效果路径保留并用既有InMemory测试检查兼容，不代表双库验收。

实际Program注册当前scope/字段；query目录seed及前向PG迁移只补缺动作，不授角色。HTTP使用当前query权限、Owner错误状态和no-store。能力接口在当前Owner内返回只读。页面默认禁用命令，明确尚未开通授权/收货/处置；查询失败清旧数据并报错，身份变化、乱序/延迟结果被丢弃，所有命令再次核UI权限和意图，未知结果不自动重发。两条文案沿原五语seed，列表去掉无权返回的客户名称/原因列。服务Owner仍是最终门。

## 验证与集中审查

- `D:\CP6-local-test\wave70\foundation/rma-red-20261023T160922Z.json`：两项真实PG红灯，未传Owner读/取消均未抛异常。保留原失败。
- `D:\CP6-local-test\wave70\foundation/rma-first-20261023T162206Z.json`：测试编译失败，错误引用Sys_AuditLogs；改为实际Sys_FieldAuditLogs。未执行测试，不能计为产品行为失败。
- `D:\CP6-local-test\wave70\foundation/rma-native-20261023T163206Z.json`：13项PG全部通过，覆盖当前撤权/最终字段来源故障、租户/本人/部门/仓区、字段隐藏、六命令零副作用、目录迁移无自动授权、实际Program/login/HTTP。
- `D:\CP6-local-test\wave70\foundation/rma-final-20261023T164217Z.json`：20项19通过1失败；新增原生并发夹具确实收到55P03锁超时，但Npgsql将它包装为InvalidOperationException，原断言要求直接PostgresException而失败。只修正该断言，`D:\CP6-local-test\wave70\foundation/rma-fence-fix-20261023T164418Z.json`单项通过。第二连接在读Owner复核前不能改Creator；读事务结束后修改成功，下一读隐藏对象。其他19成功结果原样复用；合计20独立.NET（15真实PG，5既有InMemory RMA服务用例），不相加重复运行次数。包含既有Machine独立写入与完整停机事务回归，限定检查公共Owner改动。
- `D:\CP6-local-test\wave70\foundation/rma-ui-red.json`：第1项确认旧收货按钮仍可用；其余5项因缺少ElTable/ElTableColumn替身造成夹具渲染失败，不算五个产品缺陷。补齐替身后首轮6通过，最终`rma-ui-final.json`7通过，另覆盖真实已在途详情、身份切换、乱序否决、缺数据/能力失败、命令禁用、重复与未知结果不重发。真实SFC加API替身，非生产端到端。
- `rma-typecheck-final.log`为空且进程exit0；Vue类型检查通过。相关.NET项目及必要依赖构建通过，历史警告保留。最后接口注释变更不重跑成功业务验证。
- 一次作者完整SPEC/QUALITY检查实际原RMA SPEC01–04约束、全部公共服务/HTTP入口、租户/范围/字段/锁/当前身份、无写入效果、目录不授权、页面身份与错误状态及完整diff；发现的异常断言只定向复验，无剩余阻断项。无新依赖/工作流变更。

四个本轮自有PG簇均有ownership与stopped记录、无postmaster.pid；数据/日志和所有失败保留。没有Actions、既有库迁移、生产部署、真实RMA全流程/扫码/ERP/财务验收或全仓回归。迁移仅在新隔离测试库验证。

## 范围及收尾

这是旧入口受保护查询与安全拒绝的采用，不是完整RMA业务交付。WP02-03整卡状态、24/375整卡、125目标0交付、247运行NOT_RUN均不提升。D2正式RMA与REP前置、D5真实CRM Owner共同门及D6完整SiteFormIntakeBundle等仍保留；CRM Issue109未改。

预计90–140min＋前置约6min，最终以Main start/finish记实际，Handoff不重复累计。候选通过后执行两仓正常PR采用、准确分支清理和finish释放；未提前声称席位释放。七天目标保持ACTIVE并继续。W51指定停止data删除曾被自动审批拒绝，原因blocked by policy；原目录保留，不重试或绕过。

## 实际采用与最后收尾

Main [PR446](https://github.com/GTX537/CP6/pull/446) source `748477dfa7a67d8b5cb8f4b6ba688b07585b2d8e`采用远端main `8080bd7dda999bc2f8593866458a534f469b9631`，24任务路径核对通过。Handoff [PR46](https://github.com/GTX537/CP6.AI-Handoff/pull/46)采用 `8a83015c13b7abc3305928f9ed3200ddc17551a1`，准确任务分支删除、固定树干净detached、finish释放。十四个产品路径与已测提交逐blob相同，复用有效成功结果；最后文档diff和台账生成单独检查。

Main同一任务分支最后状态PR及finish待完成；Issue445在最终远端和生命周期核对后关闭。当前约58.00min，原估90–140min，最终以finish为准，前置只读约6min、子任务不重复累计。最终证明`C:\Users\tt\CP6-planning-reports\20261017-foundation\wave70-final-verification.json`。无Actions、既有库迁移或部署。七天目标ACTIVE，继续REP独立Owner与正式Transfer等前置；D5/D6其余范围保留。
