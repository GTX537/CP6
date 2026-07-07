# Task 2 报告：BizException 迁 Core + I18nSpaceScreenSeed（零行为变化基建步）

## Status
DONE。分支 `feat/space-wave4-crosscutting`，commit `fff117e`（单提交）。
build 0 err；全量测试 1559 passed / 5 skipped（与迁移前完全一致）。

## Implemented
1. **BizException 零涟漪迁移**：`git mv CP6.WebApi/Localization/BizException.cs → CP6.Core/Localization/BizException.cs`。
   - namespace 保留 `CP6.WebApi.Localization` 不变 → Core 服务层可抛、30+ 既有 using / 测试零改动。
   - 文件头加注释「定义于 Core 供服务层抛出；namespace 保留历史值…以零涟漪迁移（2026-07-07 波4）」。
   - SDK 风格 csproj 自动包含，未改任何 csproj。WebApi 项目引用 Core 拿到唯一一份类型定义（已确认 WebApi 侧无残留同名文件、无重复类型；Core→Entity 单向引用，无循环）。
2. **I18nSpaceScreenSeed.cs 新建**：24 码五语（ZhCN/ZhTW/En/Ja/Ko 全非空），照 I18nFinScreenSeed 逐字格式。
3. **Program.cs Concat 链**追加 `.Concat(CP6.WebApi.Seed.I18nSpaceScreenSeed.Items)`（OaServiceTask 行之后）。种子在启动期幂等合并，不进单测。

## 24 码消息来源对照表（码 → 中文 → 来源）
| 码 | 中文 | 来源 |
|----|------|------|
| E-SPACE-001 | 编码已存在 | ITemplateService.cs:11「编码租户内唯一」+ 多 throw 站点（唯一性族·概括） |
| E-SPACE-002 | 参数校验失败 | SpaceMasterService/SceneService 多 throw（参数校验族·概括） |
| E-SPACE-003 | 货架下仍有库位，不能删除 | ISceneService.cs:11「Rack 有库位→E-SPACE-003」 |
| E-SPACE-004 | 库位不存在或已发布码不可修改 | 契约§11 line398 + throw「库位不存在/落位不可变更」（多义·概括） |
| E-SPACE-006 | 多边形至少需要 3 个顶点 | SpaceMasterService.cs:621 `pts.Count<3` / ValidatePolygon |
| E-SPACE-007 | 存在下级节点，不能删除 | ISpaceMasterService 护栏「有 Floor/Zone/Aisle 子」 |
| E-SPACE-009 | 数据已被他人修改，请刷新重试 | 契约§11 line403 + LocationPublishController.cs:50 内联 |
| E-SPACE-301 | 未找到可用的编码规则 | CodeEngineService.cs:397「无任何规则」 |
| E-SPACE-302 | 存在多条编码规则但未指定默认 | CodeEngineService.cs:403「仍多条且无默认」 |
| E-SPACE-303 | 编码规则缺少可区分库区的字段段 | CodePrecheck.cs:10 |
| E-SPACE-304 | 编码重复（批内重复或与既有编码冲突） | ICodeEngineService.cs:29 + CodeEngineService.cs:200/211 |
| E-SPACE-305 | 巷道字段段未标记为可选 | CodePrecheck.cs:11 |
| E-SPACE-306 | 编码规则缺少库位粒度字段段 | CodePrecheck.cs:12 |
| E-SPACE-307 | 存在空码或重复码，无法发布 | 契约§11 line397 + LocationPublishService.cs:58 |
| E-SPACE-401 | 库位仍有库存，不能停用 | 契约§11 line394 + LocationPublishService.cs:117 |
| E-SPACE-402 | 该巷道下有已发布库位，不能直接删除（可用 mode=deactivate\|rehome） | 契约§11 line407 + SpaceMasterService.cs:325 |
| E-SPACE-403 | 该货架下有已发布库位，请先停用（或 mode=deactivate\|rehome） | 契约§11 line408 + SpaceMasterService.cs:492 |
| E-SPACE-405 | 站点编码超过 10 字符且未配置 WarehouseCd 映射，无法发布/停用 | 契约§11 line404 + LocationPublishService.cs:309 |
| E-SPACE-407 | 目标巷道不存在，或与货架不在同一库区 | 契约§11 line406 + SpaceMasterService.cs:282/284 |
| E-SPACE-408 | 已发布库位不可删除，请先停用 | 契约§11 line405 + SceneService.cs:284 |
| E-SPACE-501 | 连接器编码已存在 | ConnectorService.cs:39 |
| E-SPACE-502 | 连接器不存在 | ConnectorService.cs:56/91 |
| E-SPACE-601 | 未找到该编码对应的库位 | SpaceLocateController.cs:22/27 + SpaceLocateService.cs:10 |
| W-SPACE-404 | 停用未生效：WMS 侧仍有库存 | 契约§11 line400 + LocationPublishService.cs:132 |

多义码处理：E-SPACE-001（唯一性族：模板/库区/货架编码撞名）、E-SPACE-002（参数校验族：未知 mode/落点超格/尺寸不变量/rehome 前置）、E-SPACE-004（库位不存在 + 已发布码冻结）均采概括文案。
未采信总纲 §16.3 的 402/403/405/407/408 语义（与实装不同），一律以代码/契约为准。

## Files changed
- `CP6.Core/Localization/BizException.cs`（新位置，git rename R；+1 行头注释）
- `CP6.WebApi/Seed/I18nSpaceScreenSeed.cs`（新建，24 词条）
- `CP6.WebApi/Program.cs`（+1 Concat 行）

## Self-review
- 零行为变化：迁移后 build 0 err、测试数 1559/5 与迁移前逐字一致，30+ using 无改动 → 零涟漪达成。
- 24 码齐全、五语全非空（人工双检逐行核对）。码集＝brief 清单：001/002/003/004/006/007/009 + 301~307 + 401/402/403/405/407/408 + 501/502 + 601 + W-404。
- 术语对齐 Fin 种子系（库位=ロケーション、库区=ゾーン、巷道=通路、货架=ラック、库存=在庫），Ja 为主用户语言。
- 种子幂等：Program.cs 现有 `existingKeys` 去重，重复 key 自动跳过，无覆盖风险。
