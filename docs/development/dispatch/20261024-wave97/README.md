# W97 / BUG-503 库存 MOVE 保管属性修复

COORD本聊天单人，Main `800691f802a87dbafc3aa9b504df1267e12e8b42`，Handoff `99389e2adbba3a7cd31d8d0d92405e1711655bdf`。W89已完成Main506、CRM124/125、Handoff72及Issue123关闭；实际44.03min（含BUG123，排除等待与W90–96），四条准确任务分支删除、所有席位释放，不重复。

## 前置和任务

已核对最新AGENTS/POLICY、总台账、Handoff、Issue503（Open/P1）与无重复PR，官方status/capacity通过。W44原操作／W50 Outbox／W96实际ADJ页面已采用。W96原生诊断确定CUSTOMER→SELF；本轮修复真实Owner内MOVE，新MOVE页面另行采用。

- [ ] 同一Owner事务内从真实来源读取货主、单位、单价、收货／到期日、卷号、QC和召回状态，新目标原样继承，两腿流水保留其已有属性列。
- [ ] 已有目标只允许同属性合并；不混合货主、计量、质量或计价，不改写目标属性、不引入加权计价。不在错误／公共原结果披露价格。
- [ ] 保留既有允许负库存规则（缺失来源仍按旧默认属性处理并检查目标兼容）、精确数量、v1命令规范输入／原键重放、权限与原子回滚。
- [ ] 原生PostgreSQL定向属性／兼容／回滚／未知提交／权限／HTTP验证，必要共享核心单元回归，一次集中审查，Main/Handoff合并与准确分支／席位收尾。

预计本轮实际25–40min：实现与定向回归15–25，审查／交付10–15；正式START `2026-10-24T03:59:18.158173+00:00`，独立前置约5min，finish据实记录。固定DEV-1 Main、DEV-2 Handoff原仓复用，无bind，隔离PG55435，无Actions／既有库迁移／生产部署。

这是既有兼容库存的有界缺陷修复；不宣称正式Stock Root/Claim/Capacity/QuantityKernel或TRANSFER采用。24/375整卡、125目标0完整、247正式运行NOT_RUN不变；七天持续目标ACTIVE，后续推进MOVE页面与实际Site/Form Owner前置。

## W97候选验证

70项去重相关本地验证通过（44项原生PG、26项既有逻辑回归），无跳过；修复前2项CUSTOMER→SELF失败保留。同事务MOVE属性保全／目标兼容有界修复，既有v1输入和原键恢复不变。PG停止，正常Main/Handoff采用、Issue503关闭和finish待完成；新MOVE页面后续采用。详见[单份交付](../../evidence/BUG-503/DELIVERY.md)。
