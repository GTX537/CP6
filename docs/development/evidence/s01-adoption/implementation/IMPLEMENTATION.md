# S01-12 比较器实现与局部验证

本目录记录本任务自己的证据解析和比较器检查。没有重新编译、执行既有 C#/TS/CRM/Platform 库套件，也没有数据库、HTTP、真实 Owner 或页面验收。

`eng/contracts/s01-12/verify_adoption.py` 从明确指定的三个仓库读取固定 Git commit，通过每仓一个只读 `git cat-file --batch` 进程解析原始 TRX、Vitest JSON、TAP 和历史汇总。所有 Git 调用带 `core.longpaths=true`。默认只读比较三个机器文档；显式 `--write-manifests` 才生成本任务的 manifest、vectors 和 revision-coverage。

输入不依赖工作树 HEAD：Main `47c9b12c796620effe41c62d16992ccd629ee949`、CRM `1e4eea61c31d7547c3b3495d392eefbed99b7339`、Platform `9259a45d39bae7e98ca804a74778a588c1ad4619`。固定包保留16 profiles、33 sources、122 IDs及独立原expected。33个selector与来源/派生hash等元数据逐项核对；没有重新提取外部原始归档。当前采用文件的实际Git字节另行重算，不把固定历史hash当作新提取。

矩阵有352个适用历史断言和502个NOT_APPLICABLE单元；所有实际标量仍为空，因为这些原结果报告记录的是断言结果。S01-05的34项是明确标注的SUITE_BACKED_ASSERTION：原202/202汇总、原逐ID摘要、确切it.each源码和分配集合共同证明执行；原逐测试reporter输出未捕获。19项修订为18 PARTIAL、1 NOT_COVERED；完整247场景仍NOT_RUN。

执行身份按原命令、时间、环境、退出观察与产物的准确JSON指针记录；缺失项明确声明。S01-07原DLL hash继续ORIGINAL_DLL_SHA256_MISSING。源字节检查区分原raw字节、仅原Git pin、CRLF表示、未跟踪本机产物和执行后变化。Windows原记录NuGet.Config显式映射到唯一tracked NuGet.config后再核hash，不吞掉配置依赖。

S01-10的56个固定ID使用最终57项回归中的原TRX结果。此前210项的TestInputs和Boundaries变化由最终57原运行闭环；prepare_sources的后续修改只增加42个原生字段来源审计。每条变化均记录具体依据，不把210+57说成267个独立测试。正式映射与原PROPOSED执行按语义hash相同复用，没有正式identity下的新PG运行。S01-11原C#/design宽快照中的TS变动不属于这两个命令执行输入；最终TS428独立绑定修正代码并含12项Buffer回归。

消费者文档为必需输入：检查51个来源的SHA256/Git blob/长度、源码引用闭包、56个任务与固定roadmap的逐字段快照及真实任务卡锚、六个交接slice。不会读取后来变动状态来重写快照。

## 本次执行

实际Python为 `C:/Users/tt/AppData/Roaming/uv/python/cpython-3.13-windows-x86_64-none/python.exe -X utf8`。在任务worktree中执行：

```powershell
& C:/Users/tt/AppData/Roaming/uv/python/cpython-3.13-windows-x86_64-none/python.exe -X utf8 eng/contracts/s01-12/verify_adoption.py --main-repo D:/CP6-worktrees/s01-12-20261013 --crm-repo D:/CP6.CRM-worktrees/s01-08-coord-20261012 --platform-repo D:/CP6/CP6.Platform --output docs/development/evidence/s01-adoption/implementation/final-verification.json
& C:/Users/tt/AppData/Roaming/uv/python/cpython-3.13-windows-x86_64-none/python.exe -X utf8 eng/contracts/s01-12/test_verify_adoption.py --main-repo D:/CP6-worktrees/s01-12-20261013 --crm-repo D:/CP6.CRM-worktrees/s01-08-coord-20261012 --platform-repo D:/CP6/CP6.Platform --output docs/development/evidence/s01-adoption/implementation/comparator-tests.json
```

比较器测试实际发现30项、执行30项、通过30项、失败0、错误0、跳过0。包括产物篡改拒绝和真正源侧负例：TRX/Vitest/TAP单项失败但计数仍宣称全过、重复/缺失向量定位、消费者缺失/空sources/错误WP快照/悬空source、错误输入hash以及PG56/57/210范围。这里只是比较器负例，不计入原库测试量。

初次阅读器因对重叠快照反复启动Git子进程而手动停止，保留 `first-attempt-stopped.json`（无PASS声明）。批读取版的逐次真实成功记录保留在second/third/fourth-attempt；最终可复现结果见 `final-verification.json`。未重新运行旧失败，也未改写旧记录。任务私有`.gitignore`只防Python缓存进入提交，不影响受测源码；该文件在30项测试后添加，无需重复相同业务或比较器检查。

提交、审查、共享状态、保护合并与远端包含性由父代理/COORD处理；实现子代理未提交、推送或操作Actions。
