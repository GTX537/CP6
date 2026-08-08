# CP6 Space AI Proposal Patch Policy v1

`Modify` 决策只允许 RFC 6902 的 `replace` 操作。禁止 `add`、`remove`、`move`、`copy` 和 `test`，禁止修改来源、置信度、证据、Provider、几何、逻辑身份和状态。

允许路径按 ProposalType 固定：

| ProposalType | 允许路径 |
|---|---|
| Floor | `/attributes/name` |
| Zone | `/attributes/name`、`/attributes/zonePurpose` |
| Aisle | `/attributes/name`、`/attributes/direction` |
| Rack | `/attributes/name`、`/attributes/rackType`、`/relations/zoneSourceKey`、`/relations/aisleSourceKey` |
| Wall | `/attributes/name`、`/attributes/wallType` |
| Column | `/attributes/name`、`/attributes/columnType` |
| Door | `/attributes/name`、`/attributes/doorType`、`/relations/wallSourceKey` |
| Dock | `/attributes/name`、`/attributes/dockType`、`/relations/zoneSourceKey` |
| StaticEquipment | `/attributes/name`、`/attributes/equipmentType`、`/relations/zoneSourceKey` |

约束：

1. `name` 去除首尾空白后 1～128 字符，禁止控制字符。
2. 枚举值必须来自 Provider Output Schema 或对应领域枚举。
3. 关系目标必须是同一 Run 的有效 `SourceKey`，不能形成循环父子关系。
4. 坐标、尺寸、Rotation、货架层数、格口数、库位编码和 `LogicalId` 只能由确定性编辑命令修改，不属于 Proposal Patch。
5. 服务端按 ProposalType 选择白名单；客户端传入其他路径返回 `422 SPACE_AI_PATCH_PATH_DENIED`。
6. Patch 成功后保存 AI 原值、人工终值、操作者、时间、理由和锁定字段。

枚举值集合固定如下；大小写必须完全一致：

- `zonePurpose`：`Receiving`、`Storage`、`Picking`、`Packing`、`Shipping`、`Staging`、`Passage`、`Unknown`
- `rackType`：`Selective`、`DriveIn`、`Cantilever`、`Flow`、`Mobile`、`Unknown`
- `doorType`：`Personnel`、`Rolling`、`Fire`、`Dock`、`Unknown`
- `dockType`：`Inbound`、`Outbound`、`Shared`、`Unknown`
- `equipmentType`：`Conveyor`、`Agv`、`Forklift`、`Workstation`、`Scale`、`Charger`、`Unknown`
- `direction`：`OneWay`、`TwoWay`、`Bidirectional`、`Unknown`
- `wallType`：`Exterior`、`Interior`、`Partition`、`Fire`、`Unknown`
- `columnType`：`Structural`、`Guard`、`Unknown`

跨 Run 继承规则：同一 `SourceHash` 只按 `SourceKey + ProposalType + FieldPath` 自动生成不可变锁定事实，并保留原 Proposal/Decision 身份。`SourceHash` 改变时本策略禁止自动继承；几何匹配结果只能作为待人工确认的建议。
