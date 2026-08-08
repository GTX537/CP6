using CP6.Entity.DomainModels;

namespace CP6.WebApi.Seed;

/// <summary>
/// 3D Space（库位建模/编码/发布/连接器/定位）业务错误码 E-SPACE-*/W-SPACE-* 词条（波4）。
/// 服务层抛 BizException("E-SPACE-xxx")（波4 Task3 起），BizExceptionMiddleware 按请求 culture 翻译为友好文案。
/// 中文来源：throw 站点内联消息 > docs/space/04-publish-contract.md §11 消息表（语义以代码/契约为准）。
/// 经 Program.cs 幂等合并（已存在的 key 跳过）。
/// </summary>
public static class I18nSpaceScreenSeed
{
    public static readonly Sys_Lang[] Items = new[]
    {
        // ── 主数据护栏（00 章 / 主数据服务）──
        new Sys_Lang { LangKey = "E-SPACE-001", ZhCN = "编码已存在", ZhTW = "編碼已存在", En = "Code already exists", Ja = "コードは既に存在します", Ko = "코드가 이미 존재합니다" },
        new Sys_Lang { LangKey = "E-SPACE-002", ZhCN = "参数校验失败", ZhTW = "參數校驗失敗", En = "Invalid parameters", Ja = "パラメータが不正です", Ko = "매개변수 검증에 실패했습니다" },
        new Sys_Lang { LangKey = "E-SPACE-003", ZhCN = "货架下仍有库位，不能删除", ZhTW = "貨架下仍有庫位，不能刪除", En = "Rack still has locations; cannot delete", Ja = "ラックにロケーションが残っており削除できません", Ko = "랙에 로케이션이 남아 있어 삭제할 수 없습니다" },
        new Sys_Lang { LangKey = "E-SPACE-004", ZhCN = "库位不存在或已发布码不可修改", ZhTW = "庫位不存在或已發布碼不可修改", En = "Location not found, or published code is immutable", Ja = "ロケーションが存在しないか、公開済みコードは変更できません", Ko = "로케이션이 없거나 게시된 코드는 변경할 수 없습니다" },
        new Sys_Lang { LangKey = "E-SPACE-006", ZhCN = "多边形至少需要 3 个顶点", ZhTW = "多邊形至少需要 3 個頂點", En = "Polygon needs at least 3 vertices", Ja = "ポリゴンには頂点が3つ以上必要です", Ko = "폴리곤은 정점이 3개 이상 필요합니다" },
        new Sys_Lang { LangKey = "E-SPACE-007", ZhCN = "存在下级节点，不能删除", ZhTW = "存在下級節點，不能刪除", En = "Child nodes exist; cannot delete", Ja = "下位ノードが存在するため削除できません", Ko = "하위 노드가 있어 삭제할 수 없습니다" },
        new Sys_Lang { LangKey = "E-SPACE-009", ZhCN = "数据已被他人修改，请刷新重试", ZhTW = "資料已被他人修改，請重新整理後重試", En = "Data was modified by another user; please refresh and retry", Ja = "データが他のユーザーにより変更されました。更新して再試行してください", Ko = "데이터가 다른 사용자에 의해 변경되었습니다. 새로 고침 후 다시 시도하세요" },

        // ── 编码规则预检 / 编码引擎（03 章）──
        new Sys_Lang { LangKey = "E-SPACE-301", ZhCN = "未找到可用的编码规则", ZhTW = "未找到可用的編碼規則", En = "No code rule found", Ja = "利用可能なコード規則が見つかりません", Ko = "사용 가능한 코드 규칙을 찾을 수 없습니다" },
        new Sys_Lang { LangKey = "E-SPACE-302", ZhCN = "存在多条编码规则但未指定默认", ZhTW = "存在多條編碼規則但未指定預設", En = "Multiple code rules exist but none is set as default", Ja = "複数のコード規則がありますが既定が未指定です", Ko = "코드 규칙이 여러 개이나 기본값이 지정되지 않았습니다" },
        new Sys_Lang { LangKey = "E-SPACE-303", ZhCN = "编码规则缺少可区分库区的字段段", ZhTW = "編碼規則缺少可區分庫區的欄位段", En = "Code rule lacks a segment to distinguish zones", Ja = "コード規則にゾーンを識別できるセグメントがありません", Ko = "코드 규칙에 존을 구분할 세그먼트가 없습니다" },
        new Sys_Lang { LangKey = "E-SPACE-304", ZhCN = "编码重复（批内重复或与既有编码冲突）", ZhTW = "編碼重複（批內重複或與既有編碼衝突）", En = "Duplicate code (within batch or conflicts with an existing code)", Ja = "コードが重複しています（バッチ内または既存と競合）", Ko = "코드가 중복됩니다(배치 내 또는 기존과 충돌)" },
        new Sys_Lang { LangKey = "E-SPACE-305", ZhCN = "巷道字段段未标记为可选", ZhTW = "巷道欄位段未標記為可選", En = "Aisle segment is not marked optional", Ja = "通路セグメントが任意に設定されていません", Ko = "통로 세그먼트가 선택 항목으로 표시되지 않았습니다" },
        new Sys_Lang { LangKey = "E-SPACE-306", ZhCN = "编码规则缺少库位粒度字段段", ZhTW = "編碼規則缺少庫位粒度欄位段", En = "Code rule lacks location-level segments", Ja = "コード規則にロケーション粒度のセグメントがありません", Ko = "코드 규칙에 로케이션 단위 세그먼트가 없습니다" },
        new Sys_Lang { LangKey = "E-SPACE-307", ZhCN = "存在空码或重复码，无法发布", ZhTW = "存在空碼或重複碼，無法發布", En = "Empty or duplicate codes exist; cannot publish", Ja = "空コードまたは重複コードがあり公開できません", Ko = "빈 코드 또는 중복 코드가 있어 게시할 수 없습니다" },

        // ── 发布 / 停用 / 删除护栏（04 章 §11）──
        new Sys_Lang { LangKey = "E-SPACE-401", ZhCN = "库位仍有库存，不能停用", ZhTW = "庫位仍有庫存，不能停用", En = "Location still has stock; cannot deactivate", Ja = "ロケーションに在庫が残っており無効化できません", Ko = "로케이션에 재고가 남아 있어 비활성화할 수 없습니다" },
        new Sys_Lang { LangKey = "E-SPACE-402", ZhCN = "该巷道下有已发布库位，不能直接删除（可用 mode=deactivate|rehome）", ZhTW = "該巷道下有已發布庫位，不能直接刪除（可用 mode=deactivate|rehome）", En = "Aisle has published locations; cannot delete directly (use mode=deactivate|rehome)", Ja = "通路に公開済みロケーションがあり直接削除できません（mode=deactivate|rehome を使用）", Ko = "통로에 게시된 로케이션이 있어 직접 삭제할 수 없습니다(mode=deactivate|rehome 사용)" },
        new Sys_Lang { LangKey = "E-SPACE-403", ZhCN = "该货架下有已发布库位，请先停用（或 mode=deactivate|rehome）", ZhTW = "該貨架下有已發布庫位，請先停用（或 mode=deactivate|rehome）", En = "Rack has published locations; deactivate them first (or mode=deactivate|rehome)", Ja = "ラックに公開済みロケーションがあります。先に無効化してください（または mode=deactivate|rehome）", Ko = "랙에 게시된 로케이션이 있습니다. 먼저 비활성화하세요(또는 mode=deactivate|rehome)" },
        new Sys_Lang { LangKey = "E-SPACE-405", ZhCN = "站点编码超过 10 字符且未配置 WarehouseCd 映射，无法发布/停用", ZhTW = "站點編碼超過 10 字元且未設定 WarehouseCd 對應，無法發布/停用", En = "Site code exceeds 10 chars with no WarehouseCd mapping; cannot publish/deactivate", Ja = "サイトコードが10文字を超え WarehouseCd マッピング未設定のため公開/無効化できません", Ko = "사이트 코드가 10자를 초과하고 WarehouseCd 매핑이 없어 게시/비활성화할 수 없습니다" },
        new Sys_Lang { LangKey = "E-SPACE-406", ZhCN = "站点下存在已发布库位，不可修改站点编码/仓库码（WMS 锚）", ZhTW = "站點下存在已發布庫位，不可修改站點編碼/倉庫碼（WMS 錨）", En = "Site has published locations; site code / warehouse code cannot be changed (WMS anchor)", Ja = "公開済みロケーションが存在するため、サイトコード/倉庫コードは変更できません", Ko = "게시된 로케이션이 있어 사이트 코드/창고 코드를 변경할 수 없습니다(WMS 앵커)" },
        new Sys_Lang { LangKey = "E-SPACE-407", ZhCN = "目标巷道不存在，或与货架不在同一库区", ZhTW = "目標巷道不存在，或與貨架不在同一庫區", En = "Target aisle not found, or not in the same zone as the rack", Ja = "移動先の通路が存在しないか、ラックと同じゾーンにありません", Ko = "대상 통로가 없거나 랙과 같은 존에 있지 않습니다" },
        new Sys_Lang { LangKey = "E-SPACE-408", ZhCN = "已发布库位不可删除，请先停用", ZhTW = "已發布庫位不可刪除，請先停用", En = "Published locations cannot be deleted; deactivate first", Ja = "公開済みロケーションは削除できません。先に無効化してください", Ko = "게시된 로케이션은 삭제할 수 없습니다. 먼저 비활성화하세요" },

        // ── 连接器（05 章）──
        new Sys_Lang { LangKey = "E-SPACE-501", ZhCN = "连接器编码已存在", ZhTW = "連接器編碼已存在", En = "Connector code already exists", Ja = "コネクタコードは既に存在します", Ko = "커넥터 코드가 이미 존재합니다" },
        new Sys_Lang { LangKey = "E-SPACE-502", ZhCN = "连接器不存在", ZhTW = "連接器不存在", En = "Connector not found", Ja = "コネクタが存在しません", Ko = "커넥터가 없습니다" },

        // ── 定位（06 章）──
        new Sys_Lang { LangKey = "E-SPACE-601", ZhCN = "未找到该编码对应的库位", ZhTW = "未找到該編碼對應的庫位", En = "No location found for this code", Ja = "このコードに該当するロケーションが見つかりません", Ko = "이 코드에 해당하는 로케이션을 찾을 수 없습니다" },

        // ── E00-S04 执行边界 / 审计查询稳定错误码 ──
        new Sys_Lang { LangKey = "SPACE_AUTHENTICATION_REQUIRED", ZhCN = "请先登录后访问空间功能", ZhTW = "請先登入後存取空間功能", En = "Authentication is required to access Space", Ja = "Space へのアクセスにはログインが必要です", Ko = "Space에 접근하려면 로그인이 필요합니다" },
        new Sys_Lang { LangKey = "SPACE_ACTOR_CONTEXT_REQUIRED", ZhCN = "当前用户上下文不可用", ZhTW = "目前使用者上下文不可用", En = "The current user context is unavailable", Ja = "現在のユーザーコンテキストを利用できません", Ko = "현재 사용자 컨텍스트를 사용할 수 없습니다" },
        new Sys_Lang { LangKey = "SPACE_TENANT_CONTEXT_REQUIRED", ZhCN = "当前租户上下文不可用", ZhTW = "目前租戶上下文不可用", En = "The current tenant context is unavailable", Ja = "現在のテナントコンテキストを利用できません", Ko = "현재 테넌트 컨텍스트를 사용할 수 없습니다" },
        new Sys_Lang { LangKey = "SPACE_EXTERNAL_SUBJECT_DENIED", ZhCN = "此外部身份不可访问空间功能", ZhTW = "此外部身分不可存取空間功能", En = "This external identity cannot access Space", Ja = "この外部 ID は Space にアクセスできません", Ko = "이 외부 ID는 Space에 접근할 수 없습니다" },
        new Sys_Lang { LangKey = "SPACE_AUDIT_READ_FORBIDDEN", ZhCN = "没有查看空间审计记录的权限", ZhTW = "沒有檢視空間稽核記錄的權限", En = "Permission to view Space audit records is required", Ja = "Space 監査記録を表示する権限がありません", Ko = "Space 감사 기록을 볼 권한이 없습니다" },
        new Sys_Lang { LangKey = "SPACE_CORRELATION_ID_INVALID", ZhCN = "关联编号格式无效", ZhTW = "關聯編號格式無效", En = "The correlation identifier is invalid", Ja = "相関 ID の形式が無効です", Ko = "상관관계 ID 형식이 올바르지 않습니다" },
        new Sys_Lang { LangKey = "SPACE_AUDIT_UNAVAILABLE", ZhCN = "空间审计服务暂时不可用", ZhTW = "空間稽核服務暫時無法使用", En = "The Space audit service is temporarily unavailable", Ja = "Space 監査サービスは一時的に利用できません", Ko = "Space 감사 서비스를 일시적으로 사용할 수 없습니다" },
        new Sys_Lang { LangKey = "SPACE_OPERATION_OUTCOME_UNKNOWN", ZhCN = "操作结果暂时无法确认，请使用关联编号查询", ZhTW = "操作結果暫時無法確認，請使用關聯編號查詢", En = "The operation result cannot be confirmed; use the correlation identifier to check", Ja = "操作結果を確認できません。相関 ID で確認してください", Ko = "작업 결과를 확인할 수 없습니다. 상관관계 ID로 확인하세요" },
        new Sys_Lang { LangKey = "SPACE_AUDIT_QUERY_RANGE_INVALID", ZhCN = "审计查询时间范围无效", ZhTW = "稽核查詢時間範圍無效", En = "The audit query time range is invalid", Ja = "監査照会の期間が無効です", Ko = "감사 조회 기간이 올바르지 않습니다" },
        new Sys_Lang { LangKey = "SPACE_AUDIT_QUERY_DISABLED", ZhCN = "空间审计查询当前未开放", ZhTW = "空間稽核查詢目前未開放", En = "Space audit queries are currently unavailable", Ja = "Space 監査照会は現在利用できません", Ko = "Space 감사 조회는 현재 사용할 수 없습니다" },

        // ── 警告类：WMS 同步停用未生效（04 章 §11，v1.1）──
        new Sys_Lang { LangKey = "W-SPACE-404", ZhCN = "停用未生效：WMS 侧仍有库存", ZhTW = "停用未生效：WMS 側仍有庫存", En = "Deactivation not applied: WMS still has stock", Ja = "無効化は未反映：WMS 側に在庫が残っています", Ko = "비활성화 미적용: WMS에 재고가 남아 있습니다" },
    };
}
