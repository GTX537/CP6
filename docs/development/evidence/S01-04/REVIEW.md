# S01-04 任务级差异审查

审查基线为 `8569b669fed56ddc7b53b7bb24666c7fb93d544a`，执行时 HEAD 与刷新后的 origin/main 相同。本次集中审查覆盖完整任务源码、测试、使用说明、失败/成功原始日志与 TRX，以及身份清单。未发现阻止交付的正确性、精度或范围问题。

| 项目 | 结论与依据 |
| --- | --- |
| 来源与共享边界 | 3 份包输入及 9 份只读源码/配置/依赖 pin 的 Git 字节哈希匹配；2 份实际原件字节匹配，原生 scalar 定义与派工 selected_profiles 完全相同。未改共用预期或状态、共享 Wire、项目/包/锁、其他测试、数据库及 Owner。 |
| 数量/金额词法 | Decimal18/Qty12 先完整匹配 ASCII 词法和原始小数位数再转 BigInteger；拒绝指数/正号/前导零/空白。整数无新增有限上限；NonNegativeQty12 独立禁负号，包含 -0。 |
| 原文与精度 | OriginalWire 保留负零和尾零；不可变运算值只归一化数值，不改原对象。加减、乘法和比较仅使用 BigInteger；中间精度不钳制，最终目标域显式校验，不舍入、不截断。 |
| 比例 | 分母必须正；有符号分子 gcd 约分，0/1；正负7/3、整除/非整除、>90 位及恒等式实际运行。向零截断和约分后余数单位已说明；不冒称原生 Rational 或 MAVG codec。 |
| JSON | 字段 converter 独立选域，string/null/token 与写回目标限制有测试。复用 production StrictJson，新增 carrier 覆盖 required、转义等价重复键和任意层重复键。解码字段与原 JSON UTF8 字节分开断言，重序列化不当作重放原字节。 |
| 测试发现与独立性 | 固定9 ID/count/去重/operation guard；input 与 expected 同包读取。补充预期为独立常量；全部 ratio 方法位于 InventoryDecimalTests 过滤范围。真实 RED 129失败/3通过，GREEN 132通过，均零跳过。 |
| 工程与身份 | 实际 net8.0 项目成功编译，TRX 记录运行时8.0.29；源码、配置、输入、依赖与 GREEN 产物身份齐备。RED 未留桩源码/产物快照的限制如实记录，不追溯套用 GREEN 哈希。 |
| 证据与隐私 | 日志/完整 case name/outcome/TRX 留在本任务目录；无密钥、客户数据、调试桩残留或机器路径运行时依赖。执行记录中的实际工作区/机器身份是证据上下文。 |
| 工作流 | 13 份候选工作流与 origin/main 相同：12份仅 workflow_dispatch，R2 candidate 仅 v*.*.* tag push；Azure trigger/pr none。本任务没有运行需要取消，没有修改触发器或主动运行 Actions。 |

普通源码/文档差异检查通过。原始 log/TRX 保留生成时的换行表示，不为格式检查改写；存储后的 Git blob 哈希与实际原始文件哈希分别记录。Markdown 本地链接、JSON/XML 格式及全部测试名称/计数检查通过。

没有扩大运行到全仓、数据库、前端或其他任务测试。成功验证后仅补充说明与证据，未变更被验证源码/依赖；提交/合并本身不要求重复相同验证。COORD 在最终合并窗口检查主线漂移与源文件身份后，可复用原结果并保留其执行范围；真实业务及完整对象/存储采用仍由后续任务完成。
