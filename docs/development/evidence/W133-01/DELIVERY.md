# W133-01 原生询盘事件发布与原请求恢复采用

原生configuration/submission/decision/consent/retention五个既有Owner的原队列采用正式Platform0.10.3 OutboxV2、独立有限发布权限和实际原生API后台宿主。旧原文/ID/Owner/租户/route及epoch=1保持；原业务入队继续同事务/七锁，不等待sidecar。明确管理员安装/首次Blocked接入检查旧历史与原Owner证明，不在启动DDL、不直接迁移既有环境。

私有后台invocation、当前完整身份publication、Publisher Authority及实际State/Current/Authority/EnqueueGate行fence授权短Serializable事务；网络在事务外。原run先保存，claim/prepare-send/complete依正式协议及提交回执。UNKNOWN保留原请求/send attempt，不改新键强发；已提交completion但run收尾失败可按原收据恢复。默认关闭，冻结受控Dapr HTTP目标/凭据及app/component，单条预算与后台取消有界。只将204视作sidecar接受，不能当消费者业务验收、全局topic顺序或exactly-once。

额外保护核对函数正文、具体表/事件/函数绑定及无UPDATE OF限制，拒绝角色越权、跨租户、撤权前旧快照及保护漂移。首次接入事务使用RegisteredTransaction，修正EF保存点子事务xmin不能等同顶层xid的初次实现假设。其他失败与真实来源保留，无清理失败记录或弱化原数据库门。

20项唯一相关CRM本地检查通过、必需0跳过：真实原生Owner/HTTP/PG、五类原事件、原run/提交收据恢复、并发/当前权限及角色隔离、取消停止、保护漂移，含真实60分钟匿名化及最终代码原文复核。 长检查使用开始时隔离二进制快照；之后强化的guard/source校验用最终代码对实际原文/Effects及已安装数据库做定向检查，准确列明适用范围，没有伪称最终代码全量重跑。其余未受影响成功直接引用，本地原TRX和失败日志保留在D:/CP6-local-test/wave133/foundation与wave133-elapsed。一次任务级SPEC/QUALITY审查；Main仅协调文档，不跑产品编译/全仓测试。

CRM [已合并PR](https://github.com/GTX537/CP6.CRM/pull/158)：4d376804f86c218e467cdc5e87fa6e1cf5fba486 → f0ff45691b9c27d91dd5c53b9f67d00761b475f4；[单份产品报告、配置与权限](https://github.com/GTX537/CP6.CRM/blob/4d376804f86c218e467cdc5e87fa6e1cf5fba486/docs/development/evidence/W133-02/DELIVERY.md)。Handoff [已合并PR](https://github.com/GTX537/CP6.AI-Handoff/pull/109)：a83b90dcd29728776b2d4dcaedfd2faf69fd40ac → adc984694b7b97d9b436cd77b834b914b665f07d。两仓完整树采用、准确本地/远端分支删除、官方finish完成；DEV-2回到Handoff固定席位待复用。Main正常PR与finish继续同轮完成，最终以wave133-final-verification.json核销，下次窗口统一回写最终快照。

本轮PG18端口55461/55462、测试/HTTP进程按实际资源身份停止，数据/证据/cache保留。没有新增工作树/junction，客户端无需新增Worktree。Full access/approval never继续有效，未再次询问。Docker旧socket未触碰；真实broker仍NOT_RUN。仅一条csproj嵌入式SQL资源声明，经工作流/事件内容复核；包版本、Actions入口与保护不变，无远程运行、既有环境切换或生产。

原估120–180分钟，截至准备101.11分钟，最终按Main START至最后finish记一次、跨仓不叠加。下一独立任务先处理[CRM #157](https://github.com/GTX537/CP6.CRM/issues/157)，再核D6 NUM/FILE/PRIV在冻结公开提交/H01路径的准确适用、版本接口和真实政策/日历来源继续核对；当前真实Dapr/Kafka/企业TLS/ACL/凭据/登记/消费者/SLO仍未证。D2新对象归属及REP/RMA写Owner共同门、D3计划写入口、D5实际宿主独立资格和D7最终集成保留，不扩展全部隐私导出或125目标。

七天ACTIVE。D1/D4闭合，2/7=28.6%仅整阶段出口数，不是工时/代码/业务比例。正式375卡24DONE、2IN_PROGRESS、349PLANNED，完整125目标0关闭、247NOT_RUN+41REFERENCE_ONLY不变。W129剩余42–70小时是当时低置信估计，本轮不把它当实时精算或无条件完成日。
