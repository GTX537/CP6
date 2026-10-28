# 原生工程输出关系 v1

W201 / WP05-10、WP05-11。入口 `/plm/engineering-output-relations`、`/api/plm-engineering-output-relations/owner-v1`；独立资源 `plm-engineering-output-relations`。PostgreSQL `cp6_output_relations_v1`；Scope 为实际认证环境、租户；REAL/DEMO 沿用本机专业来源配置，并在每个原件中固定。部署前必须明确本环境证据模式，不隐式赋予 REAL。

## 原件与完整关系

CREATE/REVISE 只接收准确制造版本及全部 MBOM 输入规格、每个输出的自身规格、关系文档和必要的无输入文档。输入、输出位置集合必须与原制造视图完全一致。服务读取实际制造、EBOM、MBOM、Routing、配置、工程产品、单位、厂区、独立质量规格和受控文档；客户端不能提交关系 wire、批准状态或专业身份。

保存的原件包含完整原制造内容（包括数量、配比、全部映射和原因）、全部实际来源原件/水位、新关系 wire 的原始 UTF-8 内容和独立摘要。数量始终保留原始十进制字符串，不经过 JavaScript Number 或浮点重写。

当前原件存储有 32 MiB 资源上限，超限完整失败并回滚，不截断内容或精度。大规模、多份大型规格的完整组合容量与流式保存尚未作生产容量验收；不据本轮小型夹具认定该范围已完成。

封闭 wire 使用既有 [ENG-OUTPUT-RELATIONS/1 schema](../design-handbook/remediation/assets/shared-consumers/sidecar-1.0.0.schema.json) 的 OutputRelations 定义。摘要域为 `ENG-OUTPUT-RELATIONS/1\n`，只排除本对象 bodyDigest，使用确定性属性排序、原始 Unicode 标量和不损失整数的本地编码。输入/工序/输出及每个输出的输入集合均按完整 occurrence 标识排序；数组下标和产品名称均不是身份。版本为十进制字符串。

原始输入位置使用 `mbom/<UUID>`；中间产物及输出位置使用 `output/<UUID>`。中间产物再次作为输入时保留同一个输出位置标识、准确产品及自身规格，并校验原生工序依赖和物料流向。相同产品在不同工序或位置的输出分别保存。实际根产品必须具有 PRIMARY 输出；缺失工序、孤立位置、环、异配置或没有原生无输入依据的生成不能获得可用资格。

NativeReference 使用明确版本的本地映射，保留 provider 的 Owner/id/version/digest。`Main.Plm.OutputRelations.native-reference/1` 包装原生引用；`routing-occurrence/1` 包装准确 Routing 引用与 occurrenceId；`evidence-mapping/1` 包装准确制造引用、输出 occurrenceId 和受控文档引用。三者合同 ID 前缀均为 `Main.Plm.OutputRelations.`，version 为 `1`，schemaSha256 来自各自固定封闭 schema 原文；canonicalReferenceBase64、rawSha256、byteLength 来自实际映射 bytes。它们不冒充外部提供者的签名或新增引用。合同实现见 `EngineeringOutputRelationsValuesV1`。

productIdentity 也保留完整 NativeReference，不能改写为产品编码或拼接字符串。当前本地产品 Owner 将准确工程产品版本同时作为产品身份和技术版本来源；外部产品身份与 ItemRevision 的映射不能据此推定已实施。无输入关系使用真实受控依据加独立确认，可覆盖旧制造草稿的文字缺项；只有完整无物料输入图且实际依据合格时，才覆盖空 BOM 的位置占位缺项。

目前真实 ItemRevisionRef 来源为 Main 原生工程产品版本，SpecRef 为 Main 原生独立质量规格。只有商业物料身份、缺少技术版本的输入明确拒绝；外部 Item/Spec/工程证据提供者仍需各自准确适配及企业验收。

## 独立专业确认

内容版本与控制版本分开、均不可变。POLICY 需要独立 `review-policy` 权限，选择实际启用角色、有效时间与专业依据。QUALIFY 需要实际 `qualify` 权限及政策规定角色；关系创建人、当前作者、当前制造/结构/工艺作者不能自确认。签署准确绑定内容版本、全部关系和数量、完整来源水位、当前政策以及实际人的权限/组织版本。政策创建人的当前权限和签署人的当前资格在使用及最终提交时重查。

REVISE 或 POLICY 产生新控制并清除当前资格；WITHDRAW 使用独立权限和准确 ETag 撤回当前确认。旧原件及操作结果永久保留。当前版本、配置、规格批准、受控文档/文件、来源授权或资格变化均阻止新的采用；历史查询不会把旧确认重写为新的确认。

关系确认覆盖准确制造映射及数量依据，不授予机器/资源/工装实际资格、替代料或条件适用审批、Routing 冻结、REL、VER、库存或工单许可。页面和来源检查保留具体下游缺项。

## 共同事务、来源检查和恢复

所有写入都在 Main 原操作日记、独立原件/控制/专业记录及审计共同 PostgreSQL 事务内完成。存储检查不可变、完整性、原操作外键、准确来源/政策链接与头版本护栏。租户、角色范围和父工程修订的可信部门映射保持原规则，不推测作者部门。

`HoldExact` 是同一连接与事务内的内部参与者；匹配内容与控制，重查所有来源和实际专业身份，并注册最终检查。查询页面返回的 CURRENT/QUALIFIED 观察不构成后续事务凭证。最终检查使用独立观察树，不能边枚举提交回调边增加根回调。

来源检查命令新增可选准确 `outputRelations` 引用；为空时省略 JSON 成员，旧原始命令及技术 Manifest 算法不变。内部制造必须提供真实关系；提供后匹配当前候选产品、配置、原工程修订及准确 EBOM/MBOM/Routing 组成，保存完整关系原件和当前控制/确认水位。只有准确匹配的制造映射、制造视图和数量专业缺项由该确认覆盖；其他缺项继续阻断。完整 ITER 检查包原件会自然保留这份来源身份。工程 Baseline、OutputBinding 与冻结取消仍由 WP05-12 后续实现。

来源原件采用明确的 `Main.Plm.EngineeringOutputRelationsSource/1` 观察包装，内含准确 versionReference/versionOriginalJson、控制引用及原文、政策引用及原文、专业确认引用及原文。水位变化或实际检查 findings 变化分别标记失效，不能只保留无法解读的资格摘要。

操作查询和重放均核当前来源读权。同键异体拒绝；未知提交保留原键，查询/显式关闭后恢复，不自动换键重发。浏览器只持久化按环境/租户/用户隔离的 operationId/objectId/action，并使用 Web Locks 协调页面。失去授权或切换身份清除页面私有原件，拒绝迟到响应。分开的 query/add/edit/review-policy/qualify/withdraw 菜单动作不自动授权任何角色。

本轮本地 PostgreSQL/HTTP/页面证据仅覆盖原生 Owner 和明确夹具来源，不表示企业正式验收、生产候选、既有环境迁移或部署。
