# W286 START

OA-DEF原AC-D01-03/D03-04/D04-03/D04-04：真实FLOW双作者同token与下一草稿竞争；全部旧定义writer/helper/设计器入口适配或明确拒绝；旧只读数据明确来源可信度，无版本/当前head重建不得冒充历史发布。

原OA-DEF-01 R1全文及W279逐AC核对已读。FLOW/FORM v2权威草稿/发布/克隆/原结果直接复用。旧FlowDefService.SaveDef可绕过发布、FormService.GetDraft默认写库，三个设计器仍有独立旧write路径；旧FlowDraftOwner也须处置新创建并保留已有原结果。

不迁移或回填既有业务数据，不制造历史publisher/time/pin，不替换严格旧DTO字段；旧在途运行和读取按已有事实保留。单人串行，PostgreSQL模拟业务实际验收、定向验证与一次集中审查；不重复W280比较/W281历史/W285文件，不把限定窗口算原整卡完成。

W285 PR723/262已完整采用、准确任务分支删除与官方finish；固定DEV1/DEV2复用，单人执行。预计2–4h低置信度，20h累计校准继续。
