# W290 START

原WP12-08：统一准确TaskActionContext（服务器判定SFS/APP、原pin、真实三/双版本、实际/当前/原职责主体、当前动作/委派/字段、APP准确Subject）；统一decisions分型并适配accepted APP五字段，仍在原AppCommand/Recorder事务保存原WFC alias元数据，不另造成功槽、不替换accepted canonical/wire；收件箱APP实际办理及未知原命令恢复、SFS已有页采用。实际PG/HTTP/Chrome正反验收，原卡完整状态另核。

原accepted OA-APP §4明确Guid小写RFC4122，原端口ApprovalTaskDecisionInputV2.CommandId为Guid；新APP WFC分型继承这个业务域，不把SFS 1..100字符串域套成APP新身份。WFC commandId=原accepted commandId原值，五字段与版本无损映射；APP原412/428及queryRef保留。SFS通用字符串合法域不改变。原AppCommand持久保存alias原bytes/digest，完成态仍只有原命令；不增加第二个可独立完成的journal。

W289 Main PR729/Handoff PR266完整远端采用、准确删除与finish已核。0.8–1.6h低置信，DEV1/DEV2复用；单人、PostgreSQL、模拟业务数据、精简验证、一次集中审查。原37528DONE/2IN_PROGRESS/345PLANNED，59局部余55不变。24h目标继续，22h校准。
