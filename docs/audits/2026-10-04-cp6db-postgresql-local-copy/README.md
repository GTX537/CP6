# 本机真实 CP6DB → PostgreSQL 副本

用户在本对话明确要求把本机 SQL Server CP6DB 搬迁到 PostgreSQL，以真实数据本地测试。独立分支从确认的 main 272e92cfc1c6c177d0227aa19815d603eef1a99e 创建，根工作区已有改动及连接不动。

## 已执行结果

- 原源331表、67,774行。COPY_ONLY/CHECKSUM、VERIFYONLY、全新独立SQL恢复通过，原备份SHA256见`snapshot.json`。仅SQL副本前向初始化到当前版本：354表、67,810行。
- 全新PostgreSQL库的当前四条history初始化通过。实际提交330应用表、67,620行，以及354份全部列原值归档、67,810行；684映射行数及规范化每行SHA多重集一致，0失败。SQL迁移历史仅归档。
- 181个FK在导入结束立即检查，恢复原延迟属性；用户触发器恢复。6个identity序列按实际最大ID重置。实际重新连接观察到688表、354归档表、0禁用用户触发器、0未验证FK/CHECK。
- 复制并逐文件核对261份原API发布文件，保留在本次私有运行目录；原真实构建源码e940825fc7511b41a1a80b4b73897dcc69e51bbb，2,010相关Gitblob与当前main相同。本次不重建API，不把旧构建改称本次发布。
- 最后独立API PID/listener验证后，三个health端点、Swagger页面与JSON五次HTTP200，现有`GET /api/auth/profile`匿名请求HTTP401。API仅属于本次新库/端口并已停止。原用户密码登录、全部业务流程、后台投递/外部服务不是本次六项HTTP冒烟范围。
- 导入工具定向构建通过，14项身份/保护原库/精度/NULL/Unicode/byte/decimal/大端token规范化控制通过。真实已有数据库CREATE及已完成目标重复COPY分别拒绝，目标数据保留。

## 失败及限制

初始化尚未完成时提前导入造成目标锁等待，已停止本次COPY并原生验证全部归档/禁用触发器均为0，回滚后严格等待初始化成功。第一轮正式导入在10,000行Space receipt处遇到COPY默认读取超时，原日志/私有异常保留、回滚确认；只把本地bulk等待改为10分钟，未移除检查。成功的完整导入是新的第三次执行，不覆盖失败结果。

首次冒烟脚本选了不存在的GET me路由，第二次选POST改密遭请求保护边界；均不是CP6数据库兼容故障。原停止/失败原件保留，最终根据现有控制器改用GET profile成功。全量业务登录/功能回归不冒称通过。

原历史事件有两条OccurredAtUtc空值，SQL副本初始化暂按UTC解读原CreateDate。用户的原历史时区尚未确认；原始日期完整留存，确认后再校正派生字段。所有datetime2原100ns精度/原datetimeoffset偏移均保存在`legacy_sql_*`文本归档，业务列依PG 1us与现有UTC约定转换。完整SQL旧版本备份和新SQL副本暂留。

原源仍331表、Core最新历史20260811030108_CrmFoundation、Space最新历史20260827053057_SpaceV1UnifiedDraftCreation；根用户lock文件SHA256仍71D6B6B9EC4446E92D25842310633AA9E49E994EC6211A353B5140251B1870A5。本次代码只涉及迁移工具/本机启动/文档；无普通远程Actions、保护/工作流改变或生产部署。

公共归档只包含本目录manifest列出的原始安全摘要；连接、原始业务字段、异常详情、备份和API日志在忽略目录保留。配置/实际查看入口见[本地运行手册](../../devops/CP6DB-POSTGRESQL-LOCAL-COPY.md)。

补充：已实际校正4个provider rowversion序列，读取导入8字节大端token后保证实际nextval大于各namespace最大导入token；原业务行及token未修改。该helper已接入后续COPY提交前；本次完整导入不覆盖重跑，见token-sequence-verification.json。
