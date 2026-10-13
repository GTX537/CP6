# S01-12 候选交付

本任务交付合同验证证据的可复现对照和消费者缺口，不新增产品能力。精确候选 SHA、PR 和最终交付消息由本任务 Handoff 固定提交绑定；本文件不以自己的提交哈希自证。COORD 复核、正常保护合并、共享状态同步及远端 Main 包含核对完成后，才可登记 DONE。

## 交付范围

- `eng/contracts/s01-12/verify_adoption.py` 和比较器负例测试；只读取固定三仓 Git 对象。
- 本目录 manifest、122 ID 向量矩阵、19 修订 case 覆盖、消费者与六组交接、原/新验证记录、审查和发布前触发检查。
- 没有改产品源码、原 expected/profile/Schema、映射、依赖、共享台账、生成页、四份项目记忆或其他 S01 报告。

来源 Main `47c9b12c796620effe41c62d16992ccd629ee949`、CRM `1e4eea61c31d7547c3b3495d392eefbed99b7339`、Platform `9259a45d39bae7e98ca804a74778a588c1ad4619`。开工的前置与来源核对见 [entry.json](entry.json)，START 固定运输回执见 [start-receipt.json](start-receipt.json)。运输成功不是 COORD 已读取或业务完成。

## 验证与证据含义

[validation.json](validation.json) 记录本次 compare-only 核验及负例、实际命令/退出码、源码和产物哈希；[review.md](review.md) 记录任务级 SPEC 后 QUALITY 审查。只读 [roadmap 检查](roadmap-check.json) 已通过，原运行日志仍使用原身份。manifest 中 352 个适用断言是历史结果连接，不是352个新测试；502个单元不适用。19个修订 case 的18项部分覆盖、1项未覆盖不改变完整场景 NOT_RUN。

仅 CREDIT25、INV9、ATP22 的56个词法向量适合 C#/TS/PG 对照。PG 逐 ID 使用后续57项回归中的56项；210项原运行及生命周期1保持各自范围，不相加伪称267项独立验证。记录原执行与当前字节的换行、Git pin、后续针对性回归和非运行输入差异，不笼统宣称整个当前 Main 被原套件测试过。

## 必须保留的限制

- S01-07 `ORIGINAL_DLL_SHA256_MISSING`；没有重建伪造原 DLL。S01-05 只有套件级原日志，逐项摘要和测试源码提供有界依据，未捕获的 scalar 不从 expected 填充。
- S01-08 使用终态 projection R1 的 native/overlay/effective 三个身份；原 ADOPTION_PENDING 文件不改。Platform 仅 native locator，未宣称有效终态解码或 NuGet 已发布。
- S01-11 使用 BUG196 修复后的 TS428/12个 Buffer 回归；原 C#417、设计5准确复用。BYTES 不是 native schema 验证；F09/通用C06根未补齐，实际 Owner/Store/issuer/current AR/GL/member/finite 和容量门仍未接。
- S01-10 正式映射消费语义与原 PROPOSED 运行一致；正式元数据未新跑。66个INV映射/13源表、42个CREDIT字段、5个ATP列不是已迁移的生产模型。
- 当前授信、库存桥、供应接口、CreditNote写者、QA HTTP/DTO、CRM公共页面等仍有接入缺口；详见 [consumers.json](consumers.json) 的准确源码和56个现有责任任务。不得从旧舍入值恢复不存在的精确位数。
- 没有数据库资源窗口；没有数据库操作、旧套件重跑、产品构建、发包、迁移、环境替换或部署。没有运行、重跑或取消 Actions，没有修改其触发条件；发布操作仅任务分支且不推标签。

## 给 COORD 的共享状态同步建议

1. 核对本候选、本次检查和审查证据，以及下游 S02/S03/S05/S07/S08/S09 六份交接；运输 ACK 不能替代复核。
2. 正常保护合并后以实际 PR/合并提交/远端 Main 为证据，将 S01-12 从实际领取状态登记 DONE；保留实际 START/交付时间，不回填虚构执行。
3. 同一交付窗口更新 `docs/development/roadmap.json`、生成进度视图及 `docs/project-memory/PROJECT_STATE.md`、`05-Completed.md`、`06-Todo.md`、`CHANGELOG-AI.md`。记录 S01 切片12/12只代表合同任务交付；业务0/125、247 NOT_RUN、41 REFERENCE_ONLY、原件盘点123/125、ITER/REL缺件、WP05 PROPOSED保持。
4. 将消费者清单列明的 Owner、HTTP、写者、存储、页面、包信任与恢复门交给相应 WP 任务重新准入；没有因此授权它们并行开工或改变 TenantId/权限/schema方案。
5. 合并后只做与实际集成变化相关的比较/冒烟或共享状态检查。源码/依赖/环境未变的原结果继续按真实来源复用；不因 SHA 改变重跑所有旧套件。核对远端包含后再发布 DONE 回执。
