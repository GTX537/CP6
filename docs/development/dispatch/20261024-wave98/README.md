# W98 实际库存MOVE页面与原键恢复

COORD本聊天单人，Main `c7324fab629a4d66057d4b370ba522e891f3b680`，Handoff `bd49fe3aef1826037ef68c0efd17cee5ac3d9dda`。W97 Main507/508、Handoff73及Issue503已完整收尾，实际14.62min＋独立前置约5min；两准确分支删除、三个固定席位空闲。W89也已完整收尾，不重复。

已核对最新AGENTS/POLICY、台账、Handoff、官方status/capacity与无重复PR。W96实际ADJ页面和原键恢复可复用，W97 MOVE来源属性保全／异属性目标409已在远端Main采用，原阻塞Issue503已关闭。

- [x] 实际库存行MOVE按钮和同仓目标库位表单；正数decimal(21,8)文本原样HTTP传输，来源业务键冻结，不发送浏览器价格或货主属性。
- [x] 当前move权限和query恢复分别执行，复用当前tenant/actor原键持久化、跨标签独占、UNKNOWN查询／显式close；身份变化／撤权／超时保留原键，不自动重发或换键。
- [x] Vue目标／数量／权限／双腿结果／竞态与页面连通定向验证，实际PostgreSQL HTTP精确文本移动及恢复，一次集中SPEC/QUALITY；不重跑未变W97后端70项。
- [ ] 正常Main/Handoff合并、台账／四状态文件同步、准确分支清理及官方finish席位释放。

预计实际25–35min：入口／校验8–12，定向验证8–12，审查合并收尾8–10。正式START `2026-10-24T04:16:19.155726+00:00`，独立前置约2min，最终按finish计且不叠加Handoff。固定DEV-1 Main、DEV-2 Handoff原仓复用，PostgreSQL55435；无Actions、既有库迁移或生产部署。

此处为已有库存Owner的实际浏览器采用，正式Stock/TRANSFER Root/Claim/Capacity、旧数据修复、Finance下游与现场业务验收仍独立。24/375整卡、125目标0完整、247正式运行NOT_RUN不变，七天全目标ACTIVE。

## W98候选验证

1项原生PostgreSQL实际HTTP＋29项Vue/client定向检查通过，vue-tsc通过，无最终跳过；1项修复前MOVE入口红灯保留。实际MOVE页面及原键恢复候选完成，后端合同和W97属性修复未改；PG停止，正常Main/Handoff采用及finish待完成，七天ACTIVE。详见[单份交付](../../evidence/W98-01/DELIVERY.md)。

## 实际采用与最后收尾

Main [产品PR](https://github.com/GTX537/CP6/pull/509) source `98a4d468c264bcc050e2272ec798c6b3b6ab4072`采用远端main `0b8a3ee014e7bcfb1fe58adf5b2792f4e61a7992`，15任务路径一致；Handoff [PR](https://github.com/GTX537/CP6.AI-Handoff/pull/74)采用`84897920632c79f32c2ad95695c30c1b7b45b049`，准确分支删除、固定树干净detached、finish释放。4产品路径与已测提交逐blob一致，复用1原生HTTP＋29Vue/client和类型检查；最后文档diff和台账生成独立检查。

Main同任务最终状态PR/finish待完成，当前约11.47min，原估25–35min＋前置约2min，最终以`C:\Users\tt\CP6-planning-reports\20261017-foundation\wave98-final-verification.json`为准，不重复Handoff。自有PG停止、所有证据/数据保留，无Actions／迁移／部署。实际MOVE与原键恢复采用，正式业务验收仍独立，七天ACTIVE。
