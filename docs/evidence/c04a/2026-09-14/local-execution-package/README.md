# 本机执行包准备与独立旧入口停用

本轮交付独立 `retire-deployment-local`，供已审阅原配置和完整组织集合单独退休旧 Windows 入口；不再要求先进入数据采用流程。15 项新增检查通过，全部无跳过，SQL 未访问；原排流控制器、初始化器和 API 源码未改，已通过旧测试不重跑。代码提交 `e065e00b9ae433e9672202463473651c31b36101`。

当前主分支 API `8d549909e8972b7fd74b8e5142e3e4f0759995ae` 发布 206 文件，退休操作工具发布 178 文件，原独立恢复宿主 5 文件原样复用。[完整发布清单](publication-manifest.json)逐角色固定源码、路径、大小及 SHA，不把旧 API 准备包当成当前门禁版本。新增私有候选运行清单仅变更 CRM DLL 和源码追踪两字段；原运行清单/控制/配置不变。

已对候选新 API 执行宿主只读输入检查（18 项），对实际 3 个活动和 5 个未运行入口生成退休清单。六个预览进程和五个相关容器没有停启，测试临时文件全部清除。Windows 关键词观察还识别到部署 Agent；没有额外匹配的计划任务或启动项，不等于完整管理员隔离。

[限定验证与未完成项](local-validation.json)、[输入/数据库历史身份引用](input-bindings.json)、[生命周期观察](lifecycle-observation.json)、[候选恢复请求](proposed-host-request.json)、[待批准入口清单](proposed-drain-inventory.json)、[审查记录](review.json)、[原始构建与测试](local-execution.zip)、[操作与恢复顺序](https://github.com/GTX537/CP6.CRM/blob/codex/c04a-local-execution-package-20260914/docs/decisions/C04A-LOCAL-EXECUTION-PACKAGE.md)。

这些清单是审批准备材料，尚未执行退休、初始化、来源冻结或目标启用。Core #114 / CRM #75 的五项 SQL 门禁、六个实际数据库的备份/恢复保障、独占特权控制、实际日历初始版本和 C02 前向采用/读取冒烟仍须完成；实际批准、正式 CRM11 和 C04B 观察保持开放。447 文件边界通过不提升签收范围。总体 **60%（3/5）**。
