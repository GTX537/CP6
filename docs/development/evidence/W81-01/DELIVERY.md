# W81-01：库龄与效期当前读取Owner

Issue [464](https://github.com/GTX537/CP6/issues/464)，COORD本聊天单人。Main起点`c6de50305c82fb4e03664f92c4e5ab432cc60af0`，任务`BUG-464-INVENTORY-REPORT-OWNER`、固定DEV-1；Handoff固定DEV-2，未新增工作树/代理。START见[本轮计划](../../dispatch/20261023-wave81/README.md)。W37–W80不重复。

## 行为与边界

StockDwell/Expiry的PG旧读和新版观察读均须当前原会话、各自view以及源wms-stock:query；空结果也不跳过。自身与源Stock范围/仓库区位交集先于聚合/分页，scope1实际Creator、2–4实际stock UUID部门绑定和健康祖先、5仍可信租户/Wms授权。复用OperationsOwner原生权限/业务围栏、当前最终重核、禁止外层事务及预跟踪业务；库龄所读BusinessPartner同租户且在读表锁内。字段沿用已接受数值MIN，库龄Amount/CustomerName与源UnitPrice、效期Amount/UnitPrice均投影，隐藏不造零或通过乘积反推隐藏单价。新增view只登记目录，不自动给角色授予。

`INVENTORY-OBSERVATION-OWNER/1`提供具名read DTO和`observation-v1`入口。数量decimal(21,8)、单价decimal(18,4)按实际存储语义转成精确字符串；复用InventoryDecimalValue的BigInteger中间值做乘法、合计和排序，不经decimal乘法舍入或JavaScript Number。实测两个最大数量相加`19999999999999.99999998`，最大单笔乘积`999999999999999998999000000.000000000001`。

这些是旧存储单价乘数量的参考算术，Currency=null、ValuationStatus=UNKNOWN，不宣称Finance损失/正式估值。缺价使所在参考合计null，隐藏价格也隐藏参考值/缺价计数；客户名隐藏时保对象代码。旧库龄非空decimal字段无法表示未知或精确中间值时409 OWNER_READ_VERSION_REQUIRED；旧效期LossAmount为null，不能冒财务损失。非PG历史服务/用例范围保留，不增加SQL Server开发或验收。

新版必须明确观察日期，days0–365、page>=1、pageSize1–500，按日含边界，同权限范围中先count再稳定expiry/id分页，可读取第500条以后；无到期日期及数量非正仍不属于本观察。原旧接口保机器日期、500条、负天数和原午夜阈值语义；不将旧接口冒完整候选。观察日期不是批准仓库时区、不可变Basis或处置授权；完整未知到期/损坏来源仍待DISP。

PG旧Dispose在任何SQL/序号/审计/库存调用前503 DISPOSITION_OWNER_REQUIRED，包括空/非空请求，原行仍完整。HTTP统一Owner结果/no-store，不吞成400或成功；不排队自动重试，不伪造内部库存能力。正式Review/独立批准/准确Part、NCR/货权/用途/OA/IAM共同最终门和Finance原结果恢复均未实现，WP10-24/WMS-DISP-01不关闭。

两WMS页面采用版本读、精确文本，参考算术标明未核币种、未知/隐藏为“—”，效期真实服务端分页与已应用筛选分离，不提供旧处置命令。身份切换、迟到响应、失败和当前界面权限缺失清数据；禁止无选定日期查询。图宽和带≈百分比仅近似视觉比例，文本保全部精度。

类型检查发现首页Dashboard的库龄小组件直接消费同一API。COORD在同任务串行窗口补其精确显示、具名响应解析和AUTH_EPOCH失效处理，保其他数据域。新增这四个直接消费文件到任务范围；没有单独新Owner或无关重构。

## 实际验证与集中审查

23独立.NET用例（17原生PostgreSQL＋6既有InMemory）及27Vue用例通过，无跳过；相关构建与最终vue-tsc类型检查通过，去重共50项。一次作者完整SPEC/QUALITY：实际原会话/独立动作、租户/本人/部门与Wms范围、字段MIN/源价格、精确全域、缺价、客户名、日期/分页、早期零效果、原生锁和最终授权、实际Program/login/HTTP、页面及直接看板消费；无剩余阻断。修正只复验受影响路径，未变成功结果保原来源。

- `owner-red-20261023T213556Z.json`保留3条真实缺陷红灯；`core-green-20261023T214059Z.json`3条转绿，不重复计数。
- `owner-boundaries-20261023T214250Z.json`22/22，其中16新PG＋6历史InMemory（库龄4/效期2）。真实双连接原生撤权先于围栏则拒绝，围栏内等待整个读完成；原生Stock写55P03；最终授权来源失败不返回半途结果。非空Dispose实测0SQL及0下游调用。
- `owner-reviewed-20261023T214435Z.json`4/4，复验旧负天数/午夜边界与新版按日窗口、库龄30/31/60/61/90/91及未来日期、过滤、极值、分页、实际HTTP；与上一批去重23，其中17PG。
- `ui-first-runtime.json`10/10实际两个SFC及精确文本行为；`ui-dashboard.json`17/17直接消费组件/解析器相关行为，包含新增高精度文本和同用户名epoch切换用例。不是现场浏览器E2E。
- 第一次前端脚本用不存在Node路径，未执行测试；改为本机既有Node20.19.0后运行。`type-check-runtime.log`真实失败指出Dashboard旧number消费，修正后`type-check-fixed.log`空、exit0。所有失败保留，不写成初始全绿。
- 普通构建的既有警告保留；没有全仓回归/漏洞扫描/镜像或生产候选检查。4个自有PG夹具串行55435，ownership/stopped存在且无PID；数据、日志、TRX保留，无Actions、既有环境迁移或部署。

## 收尾与剩余范围

候选进入Main产品PR后交换Handoff交付，核对远端采用，再同任务Main状态PR和finish；在完成之前不提前宣称分支删除或席位释放。原估60–90min＋独立前置约3min，实际以Main start/finish，不重复累计Handoff。

本窗是D2/WP02-03部分采用。24/375整卡、125目标0完整交付、247正式运行NOT_RUN不提升，七天目标ACTIVE。完整DISP/Finance、CRM实际Site/Form最终Owner/投递和公开受理继续待接。开工核对并据真实W79最终证据校正旧assignment未释放标志，未重复W79工作。W51指定停止data删除曾被自动审批以blocked by policy拒绝，仍保留不重试。
