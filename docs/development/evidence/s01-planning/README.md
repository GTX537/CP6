# S01 规划与台账工具检查

本目录只保存本次任务拆分、PostgreSQL 规则和台账工具的实际本地验证，不是业务测试证据。业务代码、数据库、schema 隔离和正式验收尚未执行。

- `tool-tests-red.txt`：新增任务完整性断言后、实现检查逻辑前的失败记录；26项中7失败、1错误，旧18项通过。
- `tool-tests-green.txt`：实现任务检查后的26项工具回归原始输出。其内存合成元数据只测试登记规则，从未写入业务台账。
- `ledger-validation.json`：当前台账与生成视图一致性，125目标交付仍0，247运行场景仍NOT_RUN。
- `plan-validation.json`：所选来源/派生SHA、原台账状态不变、任务卡及本次Markdown链接核对；完整原验收目录未变，复用WP01来源审计，不重复运行业务检查。

可从仓库根执行 `python -m unittest discover -s docs/development/tools -p "test_*.py"` 和 `python docs/development/tools/manage_roadmap.py` 复现工具检查。来源/链接检查程序另存本目录 `verify-plan.py`，默认从文件位置推导当前仓库，`--root` 可指定另一工作树，`--out` 必须指定报告目录；例如 `python docs/development/evidence/s01-planning/verify-plan.py --out "$env:TEMP/cp6-s01-check"`。这个脚本核对本次13项全PLANNED的固定快照，需本机原归档/派生文件；后续任务状态实际推进后应复用本次证据或为新范围编写检查，不能为让旧快照脚本通过而回退台账状态。

各报告保留实际源码输入和范围；提交/合并SHA变化不被称为重新运行业务测试。交付由正常受保护PR与远端main包含性核对；无Actions启动/取消、触发修改、分支保护修改或部署。

本机裸 `python` 是不兼容的旧解释器；本次26项回归和台账检查实际使用 `C:/Users/tt/AppData/Roaming/uv/python/cpython-3.13-windows-x86_64-none/python.exe`，准确命令见 `tool-regressions.json`。文档中的 `python` 是 Python 3.10+ 解释器的代称，不能据裸命令推断当前PATH已满足版本。审查时误用旧解释器的失败另保留为 `review-python-incompatible.txt`，不改称业务或台账通过。

集中审查结论见 [review-s01-planning.md](review-s01-planning.md)：无阻断发现；核对准确消费者源码、独立金额profile和PostgreSQL/租户边界。26项工具结果按准确源码hash复用，未重复执行；审查的台账结构检查使用实际CPython 3.13。
