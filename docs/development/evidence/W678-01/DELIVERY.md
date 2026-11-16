# W678-01 · 当前字段用途清单与订单辅助读取修复

2026-11-16 UTC，COORD；从已确认 Main `c84aeea9b922dec8e847786cb1401e821e51b843` 复用 DEV-1，任务分支 `codex/w678-01`。官方 START 为 `2026-11-16T07:58:04.234144+00:00`。本任务接续 W677 的字段/用途核销并修复直接发现的缺口，同一任务分支不另开临时修复分支。

## 当前结果

封存目录和实际 MVC 路由已成功采集。核对发现并修复 [P0 #1201](https://github.com/GTX537/CP6/issues/1201)：订单信用额度余额及预存销售数量检查直接查询订单明细，绕过已采用读取 Owner 的字段/对象范围。四个新增回归在修复前失败、修复后通过。产品及工具源码保存为 `1cc2a93302895906dd184a5923d50ddcb1ebadb4`；后续提交仅同步清单、报告与状态。

两处服务现进入 `OrderReadOwnerV1` 的原身份、当前 query、可信对象范围、事务及最终门。内部映射升为 `Main.Order.Read.Owner.v1.1`，信用额度的余额、判断、消息均要求 Amount 及 UnitPrice 依赖；同客户任一行越范围则整体拒绝，不能用局部小计冒充完整余额。数量判断保留合法新行不存在的原结果。原生大小写/尾空格比较、完整 decimal 精度及跨租户边界保持。

用户本轮决定 **简体中文为标准语言，不再以日文为基准**，已写入 `AGENTS.md`；本轮涉及的订单检查提示、说明及新增注释同步采用简体中文。技术标识、业务键与历史执行原文保持。此交付没有声称全库旧文案已经翻译或全部客户端默认语言已切换。

资源、字段域、用途边界和真实入口统一见 [当前 Main 核销入口](../../authorization/current-main/README.md)。它保留未核对项及已采用文件 v1 的当前读取模型，不以源码搜索或注册数作为所有入口通过的证明。WP02-02 保持 DONE；WP02-03 保持 IN_PROGRESS，PGT 27/27、询盘 60/60、375 卡 39 DONE / 1 IN_PROGRESS / 335 PLANNED、125 完整目标 0 DONE 保持。

## 执行记录

- 元数据 probe 首次采用 W673 的 Main 程序集 `e2e0e4ea8216596d01f639dcedaaa1f9148c7410`，其目录/路由输入与开工基线相同。收尾在本次最终 `build/bin/CP6.WebApi/debug` 重新实际采集，probe 构建 0 warning / 0 error、执行退出 0；最终清单来自 `final-metadata`。采集于 HostBuilt 后中止，未启动数据库/HTTP/后台任务。
- `CP6.Tests` 原生模式首次构建成功，161 项既有 warning、0 error。`red/red.trx`：4 失败 / 0 通过 / 0 跳过，包含隐藏金额实际 HTTP 返回 200 和越范围判断未拒绝。不是夹具加载失败。
- 私有工件根 `D:/CP6-test-artifacts/W678-01`；原始失败、实际命令和成功结果均保留。当前运行仅使用隔离 PostgreSQL、模拟用户及业务数据。

| 实际运行 | 结果及适用范围 |
| --- | --- |
| `green/green.trx` | 51 通过 / 3 失败 / 0 跳过。新增四个原生/HTTP 回归均通过；普通订单读取、独立导出/下载、撤权顺序、最终源故障、计算与字段决定继续通过。三个失败均在旧 ERP 属性扫描：固定控制器数、未纳入已采用服务 Owner、新增前已存在的原操作 query。没有把整次运行称为成功。 |
| ERP 扫描修正 | 逐项阅读 CommercialProductUnit、CurrencyPolicy、UomProfile、UomTimeBasis、ToolOrderLink、PlateMold 及价格修正原命令的 Controller → Owner → 现有权限边界。只修正静态扫描的已知适用范围；未给运行入口添加权限豁免，也未按类型名自动放行未来路由。改用实际入口存在性和明确集合核对，去掉过时固定数量。 |
| `final/final.trx` | 目标四项通过：三个修正后的 ERP 扫描及简体中文版本的实际 Main HTTP。由于筛选未限定类名，另命中两个未改动模块的历史扫描并失败，整次为 4 通过 / 2 失败 / 0 跳过。这两个后续项登记 [P2 #1202](https://github.com/GTX537/CP6/issues/1202)，没有修改、禁用或跳过原测试。 |
| `final-contracts/contracts.trx` | 限定完整 ERP 类名后的三项扫描 3/3 通过，零跳过。 |
| 复用与构建 | `validation-summary.json` 按测试身份及最后适用结果核销：本次相关 54 项最终均有成功证据。不是一次 54/54 的运行，也不将未变的 51 项重复执行；原生许可行为复用 green，语言提示与扫描修正用 final / final-contracts。最终 `chinese-build.log`：161 既有 warning、0 error；最终源码与随后保存提交的产品文件一致。 |

实际命令为 `dotnet build CP6.Tests/CP6.Tests.csproj --artifacts-path <本轮>/build -p:Cp6PostgreSqlDevelopment=true -p:UseSharedCompilation=false --disable-build-servers -m:1`；随后 `dotnet vstest` 选择 `Ordinary_order_`、`ErpPermissionAttributeTests`、`OrderCalcTests` 和 `FieldPermissionDecisionV1Tests`。原生夹具使用 `CP6_PERMISSION_PG_BIN=C:/Program Files/PostgreSQL/18/bin`、任务独有的 `permission-consumption-main` 根和端口 57365；实际 HTTP 使用原 Program、真实登录 cookie 和当前权限。收尾仅重查修改的静态规则与 HTTP 提示路径，未运行整个产品测试集。

## 集中审查与交付

一次作者 SPEC/QUALITY 审查覆盖完整任务差异：派生字段、完整余额、对象范围、最终门、现有模拟额度不被改写、原生键/精度、历史非 PostgreSQL 路径、实际 HTTP、简体中文规则、扫描边界及清单不夸大覆盖。审查补齐映射版本，三项陈旧 ERP 扫描修正后仅作局部复查。产品权限没有未解决的本轮失败；#1202 是此次额外命中的未改动模块扫描，继续保持 Open 并纳入下一轮消费者核销。

台账、两个生成进度页、协作入口和四份项目状态文件在同任务同步。最终只检查文档差异、引用、台账与生成页一致性，不因提交 SHA 改变重复业务验证。分支及受保护 main 的工作流/依赖与已分析输入一致：普通工作流手动，R2 仅 `v*.*.*` Tag；本次分支 push / PR 不命中自动触发。

## 剩余范围与生命周期

不新增正式信用额度政策或外部系统集成；原接口的临时额度/计算语义仍保留，不把本次权限修复称为正式信用业务验收。个人表单/附件、Operations 派生输出以及文件/受控文档已采用 v1 与独立用途模型仍须逐消费者核销。产品主数据查入、其他追踪/未出货入口未因普通订单通过而自动完成。

本轮没有运行或取消 GitHub Actions，没有修改工作流触发、分支保护或部署。只做受影响行为验证和一次集中审查；远端核对与准确任务分支清理后由官方 finish 释放固定席位。根目录的用户内容保持原状，最终处置回执单独保存，不在候选报告预报完成。
