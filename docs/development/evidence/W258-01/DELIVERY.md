# W258-01 / Issue684交付

第三期D1修复Issue684：四处待执行PostgreSQL迁移在历史菜单ID达到int.MaxValue时选择正整数空闲ID，保留历史菜单、授权、工程原件及编号。第二期已按最终证明归档；D2/D3接续，原375卡状态不变。

## 实现与兼容

BUG分支codex/bug-684-menu-id从Main 3d8fe1c5f02efc97498ec628419be3dd7da2277c建立。ReturnQualityV1、ReturnDispositionV2、ReturnCorrectionV1、TransferRequestV1四处菜单分配，在已有SHARE ROW EXCLUSIVE表锁内复用现有max-or-gap算法：未到极值继续max+1，到极值选最小可用正整数，过滤极值避免候选加法溢出。不更新原菜单、不重新编号、不追加角色授权。菜单主键/整数合同不变。

修复原待执行Up SQL是必要的：后置迁移无法越过旧迁移溢出；已应用的迁移不会被再次执行，历史migration ID保持，现有数据库未迁移。

## 实际验证

Original EngineeringRevision forward-migration regression reproduced SQLSTATE22003 before fix, then passed with added max/max-1, all8 new menus, original role grant/product/numbering preservation and repeat-migration assertions. Actual owned PostgreSQL18 and EF migrations; simulated business/IAM inputs. No Actions/deployment/existing environment migration.

本轮证据D:/CP6-local-test/wave258/foundation/final-validation.json；RED/日志保留。新建隔离数据库先到EngineeringMappingV1，再插入两个极值菜单及一条真实角色授权，执行全部后续迁移；8个新增菜单ID不同且不碰旧ID。工程产品序列化原件、ENG_REV前缀/长度/计数保持。再次Migrate无待执行项、菜单与动作数量不变。普通ID路径由同一夹具初始化迁移执行。

## 审查与范围

一次作者集中SPEC/QUALITY审查覆盖四处SQL完整diff、锁与极值算术、既有权限保持、回归及计划/台账。无依赖、工作流或冻结业务合同变化。Issue684待正常采用并核对远端后关闭；当前快照不提前声称关闭。原375卡27DONE/2IN_PROGRESS/346PLANNED，原59局部卡余56；W258是阻塞缺陷修复，不以此关闭完整WP02-16。

Main/Handoff正常采用、分支条件删除、席位释放由wave258-final-verification.json核销；此报告为提交前快照。
