# W272-01：FORM与子流程精确依赖

W272：发布预览冻结准确FORM/子FLOW版本，确认及激活递归复核当前资格，保留历史pin；实际PG/HTTP、审批绑定/受控提交及页面采用。24h持续目标第五窗口，继续W273唯一FORM-FLOW绑定。

[24小时持续计划](../../planning/20261102-codex-24h-oa.md)，基线`09ce18b5a829877509763418d7d2162ad06035d0`。COORD单人、固定DEV1/DEV2。

## 产品行为和合同

Managed FLOW草稿仅校验结构及可执行边界，不再拿legacy可变head证明子流程。发布预览从真实FORM/FLOW Owner解析当前启用配置的准确Published GUID、当前源资源/字段读权限和数据库写入保护。表单兼容按该准确schema判定。预览持久化版本清单；确认发布及最终决定点复用其准确pin，原版本仍合法时允许更新版已启用，不能静默换新源。当前停用、隐藏、不可见/不存在或兼容失败给明确诊断；基础保护缺失立即503，不伪装普通未找到。

子流程沿自身不可变DependencyRefs递归，保存每条直接引用即可追溯完整版本图。按定义身份判循环（旧版本也不可绕过），深度上限8；单次读取缓存来源和已验证子图，缓存仍保祖先重叠和剩余深度判断。服务依赖复用既有注册类型/MVID合同，递归核当前注册，不运行服务。发布/启用均核准确依赖当前资格；停用自己不因依赖失效被阻止。Published快照由内部Owner及PG不可变约束保证，下游不重复摘要/身份证明。

ApprovalBindingOwner按当前admin源资源/字段权限采用；ApprovalIntake受控服务TxS沿既有服务注册+Binding/Scope合同读取，内部明确服务模式不冒用设计器管理员。OA-APP已接受§16.1规定RESOLVE_PUBLISHED_ON_SUBMIT解析latest Published一次，并核head.Enabled和依赖，EXACT沿准确历史pin，本任务保留两种原合同。它们与定义预览的准确版本冻结不同，不把APP动态模式改成activation指定版本。

FLOW发布面板显示依赖Key、位置、准确版本和资格；历史面板显示发布时固定引用。409不自动重新预览或改新意图，复用原命令恢复、身份与权限清理。没有新schema迁移，没有引擎运行采用或运行中按最新版本重绑。

## 实际验证与一次审查

本地49项唯一PG18行为通过、0跳过，含真实Main登录/HTTP、精确递归依赖与受影响发布/激活/APP绑定及提交；7项Vue行为通过，显式vue-tsc --noEmit -p tsconfig.app.json --extendedDiagnostics通过（3328文件）。政策/业务数据模拟；实际隔离PG/Main。组件API模拟，无浏览器到PG整链、Actions、既有环境迁移或生产部署。

证据D:/CP6-local-test/wave272/foundation/final-validation.json及其中逐行为复用来源。依赖场景覆盖准确FORM升级后仍用预览版本、深层停用/恢复、旧子版本链、循环/8层、当前字段撤权最终回滚、APP动态/准确双模式、准确schema兼容、FORM保护缺失、跨租户来源隔离、深层服务注册消失和Main HTTP。

保留真实失败记录：首次编译暴露Binding/Intake方法适配缺失，修复调用链；source-adoption旧UNPROVEN占位断言随真实源查询改为明确不可用。dependency-chain 27中26通过，APP动态测试失败促使回查accepted原合同并撤回误改的active选择，原测试未弱化。contract-review中新保护故障注入曾写错PG触发器名，定向修正；完整失败轮次不冒称通过，只按最新有效行为汇总。

一次作者SPEC/QUALITY集中审查：完整分支、原DEF与accepted APP合同、固定pin与最终资格、租户/字段披露、祖先循环/缓存深度、当前注册及服务授权边界、原子回滚/原件与页面恢复、台账范围。仅相关本地验证，无子代理、全仓回归或重复SHA验证。

## 余项与生命周期

WP12-02仍余唯一FORM-FLOW绑定及其并发/CAS、实际运行根与子流程/任务pin、旧运行入口同门和完整原AC；现APP Binding采用不是FORM-FLOW绑定或运行实例交付。375仍27DONE/2IN_PROGRESS/346PLANNED，59局部3闭环/56余项，125/247正式状态不变。

W271最终累计8712秒；本窗口完整耗时/累计按wave272-final-verification.json核销。下一工时校准累计4小时，目标仍IN_PROGRESS，不将本窗口当作24h完成。合并前快照：Main/Handoff正常远端采用、精确分支删除、进程停止及官方finish/席位释放以最终证明核销；主目录原资料保留。
