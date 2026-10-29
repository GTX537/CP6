# 原生库位本域控制 v1

对应 MASTER §6.2.1、§8.1/8.4，原生适配 `Main.Wms.LocationOperationalControl.v1`，API `/api/wms/locations/{location}/local-controls-v1`。本适配不冒充完整MASTER v1 wire或08E；复用真实Location、Warehouse、当前会话权限、Main原结果日志及PostgreSQL事务。

## 决定和权限

GET当前控制/分页历史需要独立 `wms-location:query` 及当前Warehouse/Area/创建人或部门范围。POST `blocks` 需要独立 `block`，封闭输入OperationId/Reason/BlockScope，Scope仅LOCAL_OPERATIONAL。策略含义只增加此Owner的本域限制，不证明其他Owner已停。

POST `release-decisions` 需要独立 `unblock`，由当前认证操作者明确记录准确BlockId和理由。本域决定绑定Tenant/Location/Block/Actor、当前控制修订和强ETag，持久不可变，返回实际正文摘要Ref。它是当前授权操作者的本域处置，不是外部批准、实物验收、OA决定或质量解禁。没有默授角色权限；迁移只登记操作点。POST `releases` 再核同一操作者当前unblock/query、准确决定Ref、Block、控制前驱和ETag才关闭指定本域Block；一个决定不能用于别的封锁或旧版本。解除后的可用性仍由当前全部Owner裁决。

写入If-Match必需。ETag组合实际原生rowversion和本控制修订，已Blocked=true时增加第二封锁也改变控制版本。所有命令按原OperationId与完整请求（含目标/ETag/动作/正文）一致性恢复；重放用当前读权返回原快照，不伪装当前读。UNKNOWN保留原键；abandon仅为未提交原操作创建持久墓碑。实际错误后按原键查回，不自动新键重做。

## PostgreSQL原子性与兼容列

单一OperationsOwner事务取得真实业务/主数据表锁、当前授权及来源最终校验。events保存BLOCK/RELEASE_DECISION/RELEASE原件；controls保存单一控制修订与其他来源限制贡献。封锁/解除事件、修订、初始化元数据控制版本、IsBlocked兼容列、审计和原结果同事务；DB约束要求不可变事件与同事务原操作，拒绝裸控制头或缺失原结果。原事件不能UPDATE/DELETE/TRUNCATE，身份锚不重键。

IsBlocked = 本域仍开封锁 OR 已有/其他来源限制 OR 初始化隔离。多个Block逐项解除；其他写入者即使重复SET IsBlocked=true也保留为独立限制贡献。本域解除不清该贡献；合法原来源后续清除需走其原控制流程，仍不能绕过本域未解除Block。旧读取镜像继续可见，不能据此声称所有Stock/Reservation/MES数量入口已采用完整保护。初始化Location保持QUARANTINED/IsPickable=false，容量/预占不动。数据库会拒绝在本域Block仍开时写false。

## 页面与边界

`/wms/location/{id}/controls` 从真实库位列表、草稿和规则页进入。显示当前本域数量、独立限制及逐条历史；操作者记录解除决定后选择准确原件应用。身份/权限/库位改变清显示，晚响应丢弃；浏览器锁串行同身份库位操作，持久恢复只保存原OperationId，不落理由正文。分页不隐藏刚保存的待应用决定，过期决定按钮禁用且后端仍最终复核。

Qualification固定NATIVE_LOCAL_CONTROL_ONLY，AdmissionGrant=false。没有Location激活、StopPlan/全Owner停止、源审批解除、Stock数量过账、容量证明、08E应用、生产部署或真实企业验收。正式Transfer/Stock/REP/RMA及原七天剩余出口保持。
