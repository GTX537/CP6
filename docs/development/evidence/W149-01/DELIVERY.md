# W149-01 原生币种政策 Owner 与准确来源

Main基线8e5eb3bc29f5ce19fc2c13ac83b17350a8ee081a，COORD本对话单人复用DEV-1/DEV-2，无新工作树。预计120–180分钟；Main START至当前登记26.08分钟，Handoff不叠加，最终耗时以官方finish及wave149-final-verification.json为准。本轮只WP04-33有限前置，不关闭完整任务或原七天目标。

## 范围与实现

WP04-33原产品主账的准确经营来源包含币种；ERP-EST-01原主册要求明确code/policyVersion、金额小数0–4位、单价0–6位和HALF_EVEN。现有FxRate/GenericCode不能证明原生政策版本。新增Main.Erp.CurrencyPolicy.v1原生身份，Tenant+UUID+原样code/creator不可替换，追加原始UTF8/digest/版本，CAS修订与停用，历史保持。代码不默认JPY、不用旧名称推断；原ProductMaster没有币种字段，因此不制造产品币种Binding，原经营身份的currency missing仍保留。

新增PostgreSQL独立schema/heads/versions、不可变原文与递增触发器，原文身份/摘要及延迟外键约束。当前同DbContext事务内真实表锁防并发改写；存储读拒绝没有实际持锁的参与者。内部准确来源同时核Owner/id/version/digest、当前启用、资源query及对象范围，最终复核授权；旧版本/停用后再启用不恢复旧引用。迁移只定义菜单/能力，不给角色授权；保存历史币种代码，保留既有映射类型与MenuId极值兼容。Down拒绝逆向清除历史。

独立query/add/edit，scope1创建者UUID、2/3/4真实部门及5租户。创建显式当前授权版本/可信部门，一次性票据绑定同事务/actor/tenant；政策、部门映射、审计和原操作同事务。映射由IAM查找真实原生UUID，不能从标签/其他对象推导。原键同体返回原结果，异体冲突，恢复仍需当前读取权限，close挡迟到；前端只存最小公开恢复引用，不保存业务草稿，不自动重发UNKNOWN。

金额/单价量化作为当前政策观察，选择准确引用和类型，使用BigInteger与十进制字符串处理正负中点、大数和进位，不经JS Number/CLR decimal。输入最多1024整数位和1024小数位、无指数/前导正号/负零，政策位数遵循上述范围。页面提供目录/显式新建、CAS修订停用、历史、原操作查询/关闭与舍入核对；身份/权限改变清除私有数据，迟到响应不能回填。不是FX或财务审批，不替代后续EST同根Binding/工程/制造/SCHED消费。

## 实际验证与审查

- red-20261026T170837Z：真实Program/Kestrel冷入口期望200得到404，证明缺Owner；first阶段真实HTTP创建/重放/当前撤权通过。
- scoped-20261026T171533Z编译失败：测试生成时误改DatabaseMigrationProfile类型，修正后继续；scoped-fixed-20261026T171600Z为10/11，停用确实拒绝但错误码仍为PROFILE，统一为POLICY。原失败日志全部保留。
- final-20261026T172011Z：实际PostgreSQL18/HTTP及相关UOM/IAM29/29，0跳过；覆盖原文不可变、CAS竞争、隔离/创建者/部门2–4、撤权恢复、close阻迟到、真实提交失败共同回滚、前向迁移保留旧数据/极值菜单/无授权、精确量化及准确源。审查后去掉CurrentExact未用的可选停用绕过参数，exact-final-20261026T172521Z对应1项通过；其余28复用，不计为30。
- ui-first.json29/29中的币种页面5和恢复4保留；iam-final.json21/21替换原IAM20，新增真实界面选择currency-policy UUID行为。最终有效30项，0失败/跳过。组件和恢复采用生产代码，API/Web Locks边界受控，无W149真实浏览器或完整E2E宣称。
- build-execution.json记录vue-tsc --build和Vite build均exit0；前端运行时之后未变化，只补IAM测试。既有体积警告保留，web为dev/unknown测试资产，不是发布制品。

原始证据D:/CP6-local-test/wave149/foundation；final-validation.json绑定实际运行时/测试blob与上述复用范围。本COORD一次完整SPEC/QUALITY审查：版本/原文/精度、权限/租户/实际事务、部门创建、回滚/恢复、迁移兼容、UI身份/迟到、合同范围及完整diff/链接。收紧内部停用检查后只做对应回归，无未解决实质合并阻塞。未触发Actions、Docker、既有环境迁移或部署；本地隔离验证不冒充正式业务验收。

## 收尾快照和剩余

Handoff正常PR https://github.com/GTX537/CP6.AI-Handoff/pull/125，source c449b4a52a1494431033124aac0c89af3e572049，远端目标f448f7bab034058ee48a13543ca8c8bd491e00b3；准确分支已删除，DEV-2已官方finish释放。Main本提交仍待正常PR、远端准确采用、分支清理和finish，保留真实未收尾快照；最终proof由下轮准入归档一次，不另在main作自证提交。

仅新增WP04-33.wave149_currency_policy；W148按最终proof归档一次。375卡仍24 DONE/2 IN_PROGRESS/349 PLANNED、125完整目标0完成、247运行验收NOT_RUN，41历史静态记录不冒充运行验收。七天ACTIVE，仅D1/D4闭合；2/7约28.6%仅是完整阶段比例，不是工作量百分比。后继双Owner工程映射/冻结、正式WO五域与SCHED同UoW/UI，以及D2/D5/D6/D7原出口均保留。
