# WP02-02 Main正式包采用实施窗口

2026-10-15，COORD依据用户在两项批准交付后的“好的，继续推进”登记并执行。
本窗口完成实际Main依赖采用，不执行迁移、部署或新的Actions/发布。

## 基线与目标

Main baseline5816d04eaff210db7a3b788390e4f560a67603f7。
正式Platform0.10.3 source1277da5e0a20c6d96f0a95d0e9b4f20e0008332a，
publication SHA256 fae24b7cc239d79f12f7e2b0dc627789423f3aa6275ad38fb54a5f9ebc05bc36。
原发布与批准见[正式交付](../../authorized-adoption-release-20261015/README.md)。
S02-00及WP02-01已DONE，当前无其他实现对话占用Main依赖/EF/宿主窗口。
本任务分支codex/wp02-02-main-adoption-20261015，由COORD独占集成。

## 顺序与输出

- 更新Core两处、WebApi一处、DatabaseCompatibility.Probe一处直接引用至精确[0.10.3]。
- 用真实GitHub feed及原NuGet.config的必需签名/source mapping恢复对应项目，生成受影响的16份锁。
  不手改contentHash、不改无关依赖、不改独立历史tools/p10 Release0.10.1及其锁。
- 实际构建WebApi与DatabaseCompatibility.Probe及其依赖，对实际闭包执行适用行为验证；
  MSB3277为错误，不用禁用审计掩盖恢复失败。保留真实失败，原有效资料按输入范围复用。
- 形成空capabilities/compatibility的FormalPublished0.10.3 matrix及Unknown consumer lock，
  固定实际七包/publication/policy来源。没有真实Main上下文采用测量，不填写Allowed或虚构ctx能力行。
  COORD对实施方的准确输入及测量作一次独立审查后固定批准pin，不从待验文档自行生成信任。
- 一份DELIVERY.md列实际变化、命令/结果、复用来源和剩余范围；COORD同步台账/状态，正常PR合并。

## 写入范围与验证

只允许上述三个csproj、提案列出的16份实际受影响packages.lock.json，
本目录及任务专属eng/wp02-02/main-package-adoption/工具（确有需要才新增）。
集中审查发现探针成功提示硬编码旧版本，允许仅修正 PlatformExperiments.cs 的该条提示；不改变实验行为。
COORD独占roadmap、生成页、四份项目状态、当前交接入口。
实现方不改Program/认证/EF映射、生产配置、NuGet信任、工作流或其他任务文件。
包升级是依赖配置变更，不为版本文本另写镜像断言测试；用真实恢复/构建与适用现有行为检查。
集中审查一次，修正后只复查影响范围。

## 后续实际租户存储

本交付是WP02-02的可独立集成步骤，完整任务不记DONE。
随后依cp6-pg-tenancy-v1实现可信binding、租户专用LOGIN/DataSource、RLS/EF缓存、逐租户初始化。
原17PGT及真实Owner路径仍NOT_RUN；对应实现阶段使用本任务独占隔离PG资源，不能接现有数据库。
历史30/48缺件、125业务状态和新依据独立信任配置均不由包采用改变。
