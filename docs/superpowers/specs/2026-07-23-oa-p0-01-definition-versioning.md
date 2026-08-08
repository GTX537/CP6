# OA P0-1：定义版本化与运行锁定

日期：2026-07-23  
状态：Frozen（Epic D1/D2 已确认）  
优先级：P0  

## 1. 目标

流程或表单被编辑、保存、发布后：

- 已发布版本不可变。
- 运行中及历史实例永远读取启动时锁定的版本。
- WFS 不再依赖某个 SFS `FormKey` 才能运行。
- 管理员能明确区分“保存草稿”和“发布生效”。

本规格只交付版本地基与最小发布流，不交付 FlowOps 驾驶舱、版本迁移工具或分析报表。

## 2. 根因

当前 `Wf_FlowDef` 和 `Wf_FormDef` 同时承担稳定身份、当前编辑稿、当前运行版本和历史版本四种职责。`SaveDefAsync` 原地覆盖唯一行，`FlowEngine` 又在每次推进时按 `FlowKey` 读取该行，因此 `Version` 只是计数器，不是可读取的版本。

旧版 `2026-07-05-wfs-version-ops-design.md` 建议同一 Def 表存多版本行，但现有流程头还包含租户内唯一的 `FunctionId`、`FlowCode`，表单头包含稳定分类和启停；复制整行会让稳定元数据和不可变版本继续耦合。本规格改为头表和版本表分离。

## 3. 数据模型

### 3.1 稳定头表

`Wf_FlowDef` 保留：

```csharp
public Guid Id { get; set; }
public Guid TenantId { get; set; }
public string FlowKey { get; set; }       // (TenantId, FlowKey) unique
public string FlowName { get; set; }      // 当前展示名
public bool Enable { get; set; }          // 新实例发起开关
public string? FunctionId { get; set; }   // 稳定唯一
public string? FlowCode { get; set; }     // 稳定唯一
```

`Wf_FormDef` 保留：

```csharp
public Guid Id { get; set; }
public Guid TenantId { get; set; }
public string FormKey { get; set; }       // (TenantId, FormKey) unique
public string FormName { get; set; }
public bool Enable { get; set; }
public string? Category { get; set; }
public string? SubCategory { get; set; }
```

现有 `SchemaJson/Version/FormKey(Wf_FlowDef)` 在 P0 迁移中暂留兼容，不再作为新运行路径的真相；删除列另立后续迁移。

### 3.2 不可变版本表

```sql
CREATE TABLE Wf_FlowDefVersion (
    Id               uniqueidentifier NOT NULL PRIMARY KEY,
    TenantId         uniqueidentifier NOT NULL,
    FlowDefId        uniqueidentifier NOT NULL,
    Version          int              NOT NULL,
    Status           int              NOT NULL, -- 0 Draft, 1 Published
    FlowNameSnapshot nvarchar(200)    NOT NULL,
    SchemaJson       nvarchar(max)    NOT NULL,
    PublishedAtUtc   datetime2        NULL,
    PublishedBy      uniqueidentifier NULL,
    RowVersion       rowversion       NOT NULL,
    Creator          nvarchar(100)    NULL,
    CreateDate       datetime2        NOT NULL,
    Modifier         nvarchar(100)    NULL,
    ModifyDate       datetime2        NULL,
    CONSTRAINT FK_Wf_FlowDefVersion_Def FOREIGN KEY (FlowDefId) REFERENCES Wf_FlowDef(Id),
    CONSTRAINT UX_Wf_FlowDefVersion UNIQUE (TenantId, FlowDefId, Version)
);

CREATE UNIQUE INDEX UX_Wf_FlowDefVersion_OneDraft
ON Wf_FlowDefVersion(TenantId, FlowDefId, Status)
WHERE Status = 0;
```

`Wf_FormDefVersion` 同构：

```sql
CREATE TABLE Wf_FormDefVersion (
    Id               uniqueidentifier NOT NULL PRIMARY KEY,
    TenantId         uniqueidentifier NOT NULL,
    FormDefId        uniqueidentifier NOT NULL,
    Version          int              NOT NULL,
    Status           int              NOT NULL,
    FormNameSnapshot nvarchar(200)    NOT NULL,
    SchemaJson       nvarchar(max)    NOT NULL,
    PublishedAtUtc   datetime2        NULL,
    PublishedBy      uniqueidentifier NULL,
    RowVersion       rowversion       NOT NULL,
    Creator          nvarchar(100)    NULL,
    CreateDate       datetime2        NOT NULL,
    Modifier         nvarchar(100)    NULL,
    ModifyDate       datetime2        NULL,
    CONSTRAINT FK_Wf_FormDefVersion_Def FOREIGN KEY (FormDefId) REFERENCES Wf_FormDef(Id),
    CONSTRAINT UX_Wf_FormDefVersion UNIQUE (TenantId, FormDefId, Version)
);
```

`Status=Published` 行的名称快照、`SchemaJson/Version/DefId` 永远不可更新；Published 行不可物理删除。`Enable` 只在头表变更。

### 3.3 版本依赖

父流程 schema 内的 `SubFlowKey` 也是运行定义的一部分。如果只 pin 父版本、子流程到达时再取最新版本，父实例仍会漂移。因此发布时新增依赖快照：

```sql
CREATE TABLE Wf_FlowDefVersionDependency (
    Id                       uniqueidentifier NOT NULL PRIMARY KEY,
    TenantId                 uniqueidentifier NOT NULL,
    FlowDefVersionId         uniqueidentifier NOT NULL,
    NodeId                   nvarchar(100)    NOT NULL,
    DependencyType           nvarchar(30)     NOT NULL, -- P0: SubFlow
    TargetFlowDefVersionId   uniqueidentifier NOT NULL,
    CONSTRAINT UX_Wf_FlowDefVersionDependency
        UNIQUE (TenantId, FlowDefVersionId, NodeId, DependencyType)
);
```

- 发布父流程时，每个 SubFlow 节点必须解析到目标流程 latest Published，记录目标 VersionId。
- 目标流程无 Published 版本或已停用时，父流程发布失败。
- `SubFlowNodeHandler` 按父版本依赖表启动 pinned 子版本，不按 `SubFlowKey` 重新解析最新版本。
- 已在途父实例启动 pinned 子版本时不重新检查子流程头表 Enable，避免停用操作破坏在途；但新建顶层父实例前必须检查其依赖的流程头仍启用，停用子流程会同时阻止依赖它的新父实例。
- 触发器在触发时创建一个全新顶层实例，仍按触发时 latest Published 解析并 pin，语义不变。
- 组织关系、角色成员和连接器属于外部运行数据：本 P0 不冻结组织快照；service job 继续使用进入节点时生成的 `ActionRefJson`。连接器定义版本化列入 P1。

### 3.4 SFS 与 WFS 绑定

```sql
CREATE TABLE Wf_FormFlowBinding (
    Id          uniqueidentifier NOT NULL PRIMARY KEY,
    TenantId    uniqueidentifier NOT NULL,
    FormDefId   uniqueidentifier NOT NULL,
    FlowDefId   uniqueidentifier NOT NULL,
    Enable      bit              NOT NULL,
    RowVersion  rowversion       NOT NULL,
    Creator     nvarchar(100)    NULL,
    CreateDate  datetime2        NOT NULL,
    Modifier    nvarchar(100)    NULL,
    ModifyDate  datetime2        NULL,
    CONSTRAINT FK_Wf_FormFlowBinding_Form FOREIGN KEY (FormDefId) REFERENCES Wf_FormDef(Id),
    CONSTRAINT FK_Wf_FormFlowBinding_Flow FOREIGN KEY (FlowDefId) REFERENCES Wf_FlowDef(Id)
);

CREATE UNIQUE INDEX UX_Wf_FormFlowBinding_Active
ON Wf_FormFlowBinding(TenantId, FormDefId)
WHERE Enable = 1;
```

P0 只允许一个表单最多一个启用流程绑定。没有绑定表示独立 SFS 表单，不是配置错误。

启用绑定和发布任一侧定义时必须运行 `FlowFormCompatibilityValidator`：

- Flow `fieldPerms`、`ApproverFieldName` 和可静态提取的表单变量引用必须存在于绑定表单 latest Published schema。
- 表单发布时必须对它的 active binding 指向的 latest Published flow 反向验证。
- Flow 发布时必须对所有 active FormFlowBinding 的 latest Published form 验证。
- 无法证明兼容时 fail-closed，不允许把不匹配版本投入新实例；业务流程没有 FormFlowBinding，不参与此检查。

### 3.5 运行 pin

`Wf_FlowInstance` 新增：

```csharp
public Guid? FlowDefVersionId { get; set; } // 新建 Running/Suspended 实例必填
public Guid? FormDefVersionId { get; set; } // SFS 实例必填；业务实例为空
public Guid? FormDataId { get; set; }       // SFS 实例必填；业务实例为空
```

保留 `FlowKey` 作为检索冗余字段。运行引擎只能调用：

```csharp
Task<FlowSchema> LoadSchemaAsync(Wf_FlowInstance instance);
```

该方法按 `FlowDefVersionId` 读取；运行实例 pin 为空时快速失败，不允许回落 `FlowKey` 最新版。

## 4. 发布状态机

### 4.1 创建和编辑草稿

1. 打开设计器时读取唯一 Draft。
2. 没有 Draft 且存在 Published：复制最新 Published 为 `Version=max+1, Status=Draft`。
3. 全新定义：创建 v1 Draft。
4. 保存只更新 Draft，必须携带 `RowVersion`。
5. 并发冲突返回 HTTP 409，不采用 last-write-wins。

### 4.2 发布

1. 服务端运行完整 schema 校验。
2. 校验失败返回聚合错误，不改变 Draft。
3. 校验通过后解析并写入子流程版本依赖，再把当前 Draft 原子改为 Published，并记录名称快照、UTC 时间和用户 ID。
4. Published 后再次编辑会 copy-on-write 新 Draft。
5. 新实例只解析“头表 Enable=true 且最新 Published”的版本。
6. 关闭头表 `Enable` 后拒绝新实例，不影响已 pin 实例。
7. 全部 EF 写路径通过 `DefinitionImmutabilityInterceptor` 检查：原始状态已是 Published 的版本禁止 Update/Delete；Draft→Published 的单次状态转换允许。

### 4.3 最小 API

```http
GET  /api/oa/flow-defs/{flowKey}/draft
PUT  /api/oa/flow-defs/{flowKey}/draft
POST /api/oa/flow-defs/{flowKey}/publish
GET  /api/oa/flow-defs/{flowKey}/versions
GET  /api/oa/flow-defs/{flowKey}/versions/{version}

GET  /api/oa/form-defs/{formKey}/draft
PUT  /api/oa/form-defs/{formKey}/draft
POST /api/oa/form-defs/{formKey}/publish
GET  /api/oa/form-defs/{formKey}/versions
GET  /api/oa/form-defs/{formKey}/versions/{version}
```

Draft 更新请求：

```json
{
  "schemaJson": "{...}",
  "rowVersion": "base64",
  "name": "display name"
}
```

发布响应：

```json
{
  "definitionId": "guid",
  "versionId": "guid",
  "version": 2,
  "publishedAtUtc": "2026-07-23T15:00:00Z"
}
```

旧 `POST /api/wf/flow/def` 与 `POST /api/wf/form/def` 在 P0 内变为“保存 Draft”的兼容包装，不再直接改变运行版本；主设计器切换后标记 deprecated。旧 GET 返回最新 Published，不返回 Draft。

## 5. 迁移

### 5.1 部署前只读检查

必须输出并人工保存：

1. 每租户重复 `FlowKey/FormKey`。
2. 没有 FlowDef 的实例，按状态分组。
3. 没有 FormDef 的 FormData。
4. `FormData.FormVersion != 当前 FormDef.Version` 的数量。
5. Draft 实例数量及是否误带 task/token/history。

存在“无 FlowDef 的 Running/Suspended 实例”时迁移终止。

### 5.2 回填

1. 每个当前 FlowDef 生成一个 Published FlowDefVersion，SchemaJson 字节不变。
2. 每个当前 FormDef 生成一个 Published FormDefVersion。
3. 所有可关联流程实例 pin 到回填的 FlowDefVersion。因为当前引擎本来就使用该 schema，迁移前后行为一致。
4. `FormData.FormVersion == 当前版本` 时回填准确 FormDefVersionId。
5. 更老的 FormData 无法恢复已丢失 schema，保留版本号、VersionId 为空，并标记读取兼容态 `legacy-fallback`；不得伪造版本。
6. 按旧 FlowDef.FormKey 创建 `Wf_FormFlowBinding`。FlowKey 没有合法 FormDef 时记录异常并不创建绑定。
7. 旧定义列不删除，至少保留一个上线周期。

### 5.3 遗留草稿

遗留 Draft 实例由 P0-3 迁移到 `Wf_FormDraft`。原实例本轮不删除，记录 `LegacyFlowInstanceId` 供回滚与核数，新的列表不再读取旧实例。

## 6. 错误码

| 码 | 场景 |
|---|---|
| E-WF-029 | 流程无可用 Published 版本或头表停用 |
| E-WF-030 | 流程发布失败 |
| E-WF-036 | 表单无可用 Published 版本或头表停用 |
| E-WF-037 | 尝试修改/删除 Published 版本 |
| E-WF-045 | 定义草稿并发冲突 |
| E-WF-046 | 运行实例缺失或引用不存在的 pinned 版本 |

## 7. 验收标准

1. Flow v1 实例在 v2 发布后仍从 v1 schema 读取所有节点、边和审批人。
2. 新实例在 v2 发布后 pin v2。
3. Form v1 数据在 v2 发布后仍能读取 v1 schema；无法恢复的存量老数据明确返回 `legacy-fallback`。
4. 修改或删除任一 Published schema 被服务端拒绝。
5. 保存 Draft 不影响新提交；只有 Publish 后新版本才生效。
6. 同一用户或两个管理员并发保存，后写者收到 409，不覆盖先写者。
7. `Enable=false` 后新提交失败，在途实例继续运行。
8. WFS 能创建没有 FormDefVersion/FormData 的业务流程实例。
9. SFS 表单没有流程绑定时仍可作为独立表单存在。
10. 迁移后现有 Running 实例的行为与迁移前一致。
11. 父流程 v1 pin 的 SubFlow 节点在子流程 v2 发布后仍启动 pinned 子流程 v1。
12. 启用 FormFlowBinding 或发布不兼容的 Form/Flow 版本时 fail-closed。

## 8. 测试

| 层 | 覆盖 |
|---|---|
| 单元 | latest published 解析、不可变守卫、copy-on-write、并发 RowVersion、绑定唯一性、子流程依赖解析 |
| 集成 | EF 索引和迁移回填、v1 在途 + v2 发布、pinned 子流程、业务实例无 Form pin、FormData legacy fallback |
| 前端 | 保存与发布按钮分离、并发冲突、版本历史只读、停用提示 |
| E2E | 发布 v1→起单→发布 v2→完成旧单→起新单并核对两个 pin |

## 9. 回滚

- 功能开关关闭新版本 API，旧 GET 继续读头表兼容列。
- 新版本表和 pin 列不删除；回滚应用可忽略它们。
- 因 P0 不删除旧定义列和旧 Draft 实例，可在一个发布周期内恢复旧读取。
- 已创建的新实例必须保留 `FlowKey` 冗余值，旧应用可继续检索；回滚前不得发布旧应用无法解析的新 schema 节点类型。

## 10. 文件影响

| 文件/区域 | 变化 |
|---|---|
| `CP6.Entity/DomainModels/Wf/Wf_FlowDef.cs` | 收敛为稳定头；兼容列标记 deprecated |
| `CP6.Entity/DomainModels/Wf/Wf_FormDef.cs` | 收敛为稳定头 |
| `CP6.Entity/DomainModels/Wf/Wf_FlowInstance.cs` | 新增三项 pin |
| `CP6.Entity/DomainModels/Wf/` | 新增两个 Version 实体和 FormFlowBinding |
| `CP6.Core/EFDbContext/CP6Context.cs` | DbSet、索引、关系和过滤 |
| `CP6.Core/Services/Wf/FlowDefService.cs` | Draft/Publish/Version API |
| `CP6.Core/Services/Wf/FormService.cs` | Draft/Publish/Version API |
| `CP6.Core/Services/Wf/FlowEngine*.cs` | 所有 schema 读取改为按实例 pin |
| `CP6.Core/Services/Wf/FlowEngine.SubFlow.cs` | 子流程按父版本依赖启动 pinned 子版本 |
| `CP6.WebApi/Controllers/{Wf,Oa}` | 新定义 API 和兼容包装 |
| `cp6.web/src/views/oa/designer` | 保存与发布分离、版本历史 |
| `cp6.web/src/views/wf/designer/FormDesigner.vue` | 表单发布与历史 |
| `CP6.Tests` | 版本、迁移、并发和回归测试 |

## 11. 范围外

- 跨版本迁移运行实例。
- 版本 diff UI。
- FlowOps 驾驶舱及强制干预。
- 自动清理历史版本。
- 连接器配置版本化和组织关系快照。

## 12. 相对工作量

- 数据模型与迁移：大。
- 引擎 pin 收敛：大，风险最高。
- 发布 API 与设计器：中。
- 回归与迁移验证：大。

详细任务、工时与并行波次在 Epic 确认后进入执行计划。
