# W132-01 原生操作面与有界观测采用

原生CRM HTTP宿主和后台身份基线周期采用既有正式0.10.3 ObservabilityV1/HealthV1。操作端点默认拒绝访问，启用后需冻结token hash、实际peer和HTTPS或真实回环；操作身份不替代业务会话/权限。live独立，startup检查实际传播注册，ready采用原数据库/Owner/OIDC检查并有超时/并发上限。旧公开health保持原语义。

有界内存导出仅接受有限技术维度，不输出正文、query、租户/用户、原消息、会话、凭据或任意异常；范围仅限该注册的导出，不接管其他已有日志。技术HTTP尝试结果不改写业务原操作/Inbox/提交或ACK事实。后台scope归属、多个宿主隔离和停止传播释放实际验证；没有外部目的地、完整采样或生产SLO承诺。

运行身份保持Unproven。正式owned-loader的另一个加载上下文只能证明其受控模块，不能用另载一份DLL冒充实际进程身份。依赖通过可支持清单内读取，但新效果ready=false/503；本轮能力清单只覆盖configuration/context读与configuration写，不泛化到全部CRM入口。声明格式通过不是独立发布资格，实际业务仍由既有Owner门执行。

本地核对7个正式缓存nupkg完整hash与既有接受清单一致，真实宿主加载的5个Platform模块磁盘文件匹配准确归档DLL。NuGet版本0.10.3的AssemblyVersion真实为0.8.0.0；初次错误假设保留为失败记录并改用字节证明。文件匹配不扩大为已加载内存、完整宿主镜像、签名或正式生产接受。没有修改公共合同、包版本、Platform代码或数据库迁移。

17项唯一相关CRM本地检查通过，必需0跳过：真实原生HTTP/PG、操作权限隔离、有界探针及取消、多宿主传播、Owner重放、未知COMMIT消息恢复、后台采集和最终持锁撤权。 一次任务级SPEC/QUALITY审查通过；IPv4-mapped通配peer被拒绝并有对应回归。已成功且未受后续输入影响的证据保留来源，不因文档/提交或SHA变化重新跑产品验证。Main仅协调文档，不运行产品编译/测试。记录来自D:/CP6-local-test/wave132/foundation/final-validation.json；原TRX/失败日志及formal-package-check、loaded-platform-modules保留。操作凭据、网络地址、故障注入及HTTP来源均为明确隔离夹具。

CRM [PR156](https://github.com/GTX537/CP6.CRM/pull/156)：634d127181bfe1d11f51b58df0a03d2c6d144ef3 → de00251a50e50e9a4750c34efb526d172569a9ab；[单份产品报告与配置](https://github.com/GTX537/CP6.CRM/blob/634d127181bfe1d11f51b58df0a03d2c6d144ef3/docs/development/evidence/W132-02/DELIVERY.md)。Handoff [PR108](https://github.com/GTX537/CP6.AI-Handoff/pull/108)：4117345f8f349236843c438c4973997edb88b265 → f39750c25ba2b7d2ca7c6b769b334d72f190309f。两仓完整树采用、准确本地/远端分支删除及官方finish均完成；DEV-2已重绑Handoff留作复用。Main正常PR与最后finish继续同轮完成，以wave132-final-verification.json核销，下一次准入统一回写最终快照，不另开本轮分支。

本轮自有PG18端口55460经身份/guard/无客户端会话核实后smart停止，测试/HTTP进程退出；数据、失败记录和复用缓存保留，无新工作树/junction。实际工具权限Full access、审批never，继续既有授权而未重复询问；未再次尝试上轮被自动审批拒绝的Docker socket移除。当前broker仍NOT_RUN。没有Actions运行/取消/触发变动、保护绕过、既有环境切换或生产部署。

原估70–110分钟，截至准备26.57分钟；最终以Main START到最后finish计一次，跨仓不叠加。D5实际宿主镜像/加载来源的适用验证器及独立资格仍待；当前Dapr/Kafka、企业TLS/ACL/凭据/登记和生产SLO未证。D2/D3写入口前置、D6原生Outbox发布/恢复及必要公共支撑、D7最终集成继续原出口，完整阶段/正式验收不提升。 下一窗口优先核对并推进原D6原生Outbox发布/恢复，不扩大完整隐私导出或125目标。

七天持续目标ACTIVE。D1/D4闭合，2/7=28.6%仅指整阶段出口数，不是工时/代码完成率。正式375卡24DONE、2IN_PROGRESS、349PLANNED，完整125目标0关闭、247NOT_RUN+41REFERENCE_ONLY不变。W129剩余42–70小时是当时低置信估计，未在W130–W132后重新精算；外部真实前置等待另列。
