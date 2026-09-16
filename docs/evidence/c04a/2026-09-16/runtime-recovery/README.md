# C04A 原运行恢复与 A12 最终验收

2026-09-16 12:23 UTC，原本机 C02 采用的最后运行验收通过。C04A 固定清单 **12/12＝100%**，整体清单 **78.3333% → 80.0000%**；本任务正常合并并核验双仓库远端 main 后，正式里程碑由 3/5 提升至 **4/5＝80%**。C04B 仍为 0%，分母未改。验收原件保留当时的 `formalDeliveryPending=true`，不将后续 Git 交付写成此前已完成。

| 本次验收 | 结果 |
| --- | --- |
| 三个原来源与旧 CRM 表 | 全部 Frozen / generation 1 / 原 RunId；60 张旧表保留且零行 |
| 两个原业务目标 | Written / generation 2；两个日历与配置 Published（物理 Status 10），原版本/组织/发布人/部门匹配 |
| 两组织目录与身份 | Runtime Ready 2/2、身份对账 Ready 2/2、NeedsReconciliation 0 |
| 原宿主与 transport | 宿主及四子进程 PID/启动时间/Owner 匹配，ready；原 Kafka healthy 后启动两个 Dapr |
| 当前 HTTP | CRM live/ready 各 200；两个公开表单 200；publisher Dapr HTTP health 204 |
| 独占控制与原输入 | 旧 API 停止且 restart=no；部署 Agent Stopped/Disabled；SQL Agent 停止；八入口退休标记、原密钥及 CRM 配置摘要保持 |

[验收原件](actual-acceptance.json)、[恢复摘要](runtime-recovery-summary.json)、[原 transport 绑定](original-transport-recovered.json)、[公开文件摘要](evidence-manifest.json)。验收是重启后必要的当前运行核对，未重跑已通过的五项 SQL、六库恢复、A08/A09 或应用全量测试。没有重新初始化、冻结、启用或发布日历，没有构造 Lead。

数据库首写证据比日历发布更早：两个目标 `FirstWriteTable` 均为 `crm_v1.RetentionRun`，分别发生于 10:39:53 和 10:39:55 UTC。日历在 10:50/10:51 UTC 发布。Written 边界已经越过，后续只能前向修复，不能以恢复旧备份或旧 DLL 覆盖当前目标。

## Docker 恢复方式及保留范围

用户已明确授权限定 socket 修复及 Windows 重启。重启后错误仍在；官方签名和校验和核实后，将 Docker Desktop 4.68 升级为 4.91，安装退出码 0，但升级本身仍未解除旧 socket 问题。最终只给 Docker 子进程设置新的运行缓存根，并将其中 Docker 目录通过 junction 指回原目录；原数据盘、容器、镜像和配置位置保留。旧不可访问 socket 原样保留，未改 ACL、加密策略或用户全局环境。见[安装包验证](verified-installer.json)、[安装执行](docker-update-execution.json)。两个记录分别保留验证时未执行和随后执行成功的时点。

停机期间对数据盘建立持久同卷 VSS 保护，保留系统盘小文件与设置副本，核对长度及四段采样摘要；没有计算整盘哈希。这不是异机备份，未恢复任何卷或数据库。既有六库备份/恢复证据沿用。

再次启动 Docker 须使用本机审计目录内 `Start-RecoveredDocker.ps1`；它先核对原目录/数据盘映射和签名，Docker 进程存在时只返回，不重复启动。原 Windows socket 缺陷尚未永久修复，普通启动方式可能重新遇到旧路径。辅助脚本及失败诊断留在受限本机，仓库只记录摘要。保持部署代理停用，待审阅其待部署内容后再决定是否恢复。

publisher 暴露 60958 HTTP 和 60959 gRPC；receiver 没有发布宿主 HTTP 端口。一次将 60959 当 HTTP 的探针返回 ResponseEnded，已记录为探针协议错误，未将它当 receiver 健康结果或产品失败。见[端口核对](dapr-health.json)。

## 下一阶段的准确边界

C04B 的剩余 20% 仍包含 CRM11 正式演练/切换、生产采用门禁、旧表至少一个正式发布周期只读观察，以及旧 EF/运行时依赖退出和前向清理。此次本机空来源采用不替代这些条件，也没有生成生产候选或执行生产部署、Actions。原通过门禁继续复用其对应版本，不因提交 SHA 变化重复测试。

复现时先核对[本机原件哈希](source-evidence-hashes.json)，沿用已批准的 429 文件、请求摘要和原容器完整 ID。读取当前源/目标控制、日历版本、身份状态与进程所有权，再检查当前端点；不要重放数据库变更命令。原始配置、凭据、密钥、数据库备份、安装包及完整日志不进入仓库。
