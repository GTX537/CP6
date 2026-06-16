using CP6.Entity.DomainModels;

namespace CP6.WebApi.Seed;

/// <summary>
/// OA 阶段4 章09 自研设计器（表单/流程）画面词条。中文原文＝key，五语翻译。
/// </summary>
public static class I18nWfDesignerSeed
{
    public static readonly Sys_Lang[] Items = new[]
    {
        // ── 菜单名 ──
        new Sys_Lang { LangKey = "表单设计器", ZhCN = "表单设计器", ZhTW = "表單設計器", En = "Form Designer", Ja = "フォームデザイナー", Ko = "폼 디자이너" },
        new Sys_Lang { LangKey = "流程设计器", ZhCN = "流程设计器", ZhTW = "流程設計器", En = "Flow Designer", Ja = "フローデザイナー", Ko = "플로우 디자이너" },

        // ── 表单设计器（FormDesigner）+ 控件库 ──
        new Sys_Lang { LangKey = "表单标识", ZhCN = "表单标识", ZhTW = "表單標識", En = "Form Key", Ja = "フォームキー", Ko = "폼 식별자" },
        new Sys_Lang { LangKey = "表单名称", ZhCN = "表单名称", ZhTW = "表單名稱", En = "Form Name", Ja = "フォーム名", Ko = "폼 이름" },
        new Sys_Lang { LangKey = "加载", ZhCN = "加载", ZhTW = "載入", En = "Load", Ja = "読み込み", Ko = "불러오기" },
        new Sys_Lang { LangKey = "重做", ZhCN = "重做", ZhTW = "重做", En = "Redo", Ja = "やり直し", Ko = "다시 실행" },
        new Sys_Lang { LangKey = "控件库", ZhCN = "控件库", ZhTW = "控制項庫", En = "Control Library", Ja = "コントロールライブラリ", Ko = "컨트롤 라이브러리" },
        new Sys_Lang { LangKey = "画布", ZhCN = "画布", ZhTW = "畫布", En = "Canvas", Ja = "キャンバス", Ko = "캔버스" },
        new Sys_Lang { LangKey = "暂无字段，点击左侧控件添加", ZhCN = "暂无字段，点击左侧控件添加", ZhTW = "暫無欄位，點擊左側控制項新增", En = "No fields yet — click a control on the left to add", Ja = "フィールドがありません。左のコントロールをクリックして追加", Ko = "필드가 없습니다. 왼쪽 컨트롤을 클릭하여 추가하세요" },
        new Sys_Lang { LangKey = "上移", ZhCN = "上移", ZhTW = "上移", En = "Move Up", Ja = "上へ", Ko = "위로" },
        new Sys_Lang { LangKey = "下移", ZhCN = "下移", ZhTW = "下移", En = "Move Down", Ja = "下へ", Ko = "아래로" },
        new Sys_Lang { LangKey = "属性", ZhCN = "属性", ZhTW = "屬性", En = "Properties", Ja = "プロパティ", Ko = "속성" },
        new Sys_Lang { LangKey = "请先选择字段", ZhCN = "请先选择字段", ZhTW = "請先選擇欄位", En = "Select a field first", Ja = "先にフィールドを選択してください", Ko = "먼저 필드를 선택하세요" },
        new Sys_Lang { LangKey = "字段标识", ZhCN = "字段标识", ZhTW = "欄位標識", En = "Field Key", Ja = "フィールドキー", Ko = "필드 식별자" },
        new Sys_Lang { LangKey = "标签", ZhCN = "标签", ZhTW = "標籤", En = "Label", Ja = "ラベル", Ko = "레이블" },
        new Sys_Lang { LangKey = "最大长度", ZhCN = "最大长度", ZhTW = "最大長度", En = "Max Length", Ja = "最大長", Ko = "최대 길이" },
        new Sys_Lang { LangKey = "占位提示", ZhCN = "占位提示", ZhTW = "佔位提示", En = "Placeholder", Ja = "プレースホルダー", Ko = "자리 표시자" },
        new Sys_Lang { LangKey = "校验正则", ZhCN = "校验正则", ZhTW = "校驗正則", En = "Validation Regex", Ja = "検証正規表現", Ko = "검증 정규식" },
        new Sys_Lang { LangKey = "选项", ZhCN = "选项", ZhTW = "選項", En = "Options", Ja = "選択肢", Ko = "옵션" },
        new Sys_Lang { LangKey = "值", ZhCN = "值", ZhTW = "值", En = "Value", Ja = "値", Ko = "값" },
        new Sys_Lang { LangKey = "添加选项", ZhCN = "添加选项", ZhTW = "新增選項", En = "Add Option", Ja = "選択肢を追加", Ko = "옵션 추가" },
        new Sys_Lang { LangKey = "请输入表单标识", ZhCN = "请输入表单标识", ZhTW = "請輸入表單標識", En = "Please enter the form key", Ja = "フォームキーを入力してください", Ko = "폼 식별자를 입력하세요" },
        new Sys_Lang { LangKey = "该表单尚未定义", ZhCN = "该表单尚未定义", ZhTW = "該表單尚未定義", En = "This form is not defined yet", Ja = "このフォームはまだ定義されていません", Ko = "이 폼은 아직 정의되지 않았습니다" },
        new Sys_Lang { LangKey = "请填写表单标识与名称", ZhCN = "请填写表单标识与名称", ZhTW = "請填寫表單標識與名稱", En = "Please fill in the form key and name", Ja = "フォームキーと名前を入力してください", Ko = "폼 식별자와 이름을 입력하세요" },
        new Sys_Lang { LangKey = "单行文本", ZhCN = "单行文本", ZhTW = "單行文字", En = "Text", Ja = "単一行テキスト", Ko = "한 줄 텍스트" },
        new Sys_Lang { LangKey = "多行文本", ZhCN = "多行文本", ZhTW = "多行文字", En = "Textarea", Ja = "複数行テキスト", Ko = "여러 줄 텍스트" },
        new Sys_Lang { LangKey = "数字", ZhCN = "数字", ZhTW = "數字", En = "Number", Ja = "数値", Ko = "숫자" },
        new Sys_Lang { LangKey = "下拉选择", ZhCN = "下拉选择", ZhTW = "下拉選擇", En = "Select", Ja = "ドロップダウン", Ko = "드롭다운" },
        new Sys_Lang { LangKey = "单选", ZhCN = "单选", ZhTW = "單選", En = "Radio", Ja = "ラジオ", Ko = "단일 선택" },
        new Sys_Lang { LangKey = "多选", ZhCN = "多选", ZhTW = "多選", En = "Checkbox", Ja = "チェックボックス", Ko = "다중 선택" },
        new Sys_Lang { LangKey = "日期", ZhCN = "日期", ZhTW = "日期", En = "Date", Ja = "日付", Ko = "날짜" },
        new Sys_Lang { LangKey = "日期时间", ZhCN = "日期时间", ZhTW = "日期時間", En = "DateTime", Ja = "日時", Ko = "날짜 시간" },
        new Sys_Lang { LangKey = "人员", ZhCN = "人员", ZhTW = "人員", En = "User", Ja = "ユーザー", Ko = "사용자" },
        new Sys_Lang { LangKey = "部门", ZhCN = "部门", ZhTW = "部門", En = "Department", Ja = "部門", Ko = "부서" },
        new Sys_Lang { LangKey = "附件", ZhCN = "附件", ZhTW = "附件", En = "Attachment", Ja = "添付ファイル", Ko = "첨부 파일" },

        // ── 流程设计器（FlowDesigner）──
        new Sys_Lang { LangKey = "流程标识", ZhCN = "流程标识", ZhTW = "流程標識", En = "Flow Key", Ja = "フローキー", Ko = "플로우 식별자" },
        new Sys_Lang { LangKey = "流程名称", ZhCN = "流程名称", ZhTW = "流程名稱", En = "Flow Name", Ja = "フロー名", Ko = "플로우 이름" },
        new Sys_Lang { LangKey = "加开始节点", ZhCN = "加开始节点", ZhTW = "加開始節點", En = "Add Start Node", Ja = "開始ノードを追加", Ko = "시작 노드 추가" },
        new Sys_Lang { LangKey = "加审批节点", ZhCN = "加审批节点", ZhTW = "加審批節點", En = "Add Approval Node", Ja = "承認ノードを追加", Ko = "승인 노드 추가" },
        new Sys_Lang { LangKey = "加结束节点", ZhCN = "加结束节点", ZhTW = "加結束節點", En = "Add End Node", Ja = "終了ノードを追加", Ko = "종료 노드 추가" },
        new Sys_Lang { LangKey = "退出连线", ZhCN = "退出连线", ZhTW = "退出連線", En = "Exit Connect", Ja = "接続を終了", Ko = "연결 종료" },
        new Sys_Lang { LangKey = "连线模式", ZhCN = "连线模式", ZhTW = "連線模式", En = "Connect Mode", Ja = "接続モード", Ko = "연결 모드" },
        new Sys_Lang { LangKey = "请点击起始节点", ZhCN = "请点击起始节点", ZhTW = "請點擊起始節點", En = "Click the source node", Ja = "始点ノードをクリック", Ko = "시작 노드를 클릭하세요" },
        new Sys_Lang { LangKey = "请点击目标节点", ZhCN = "请点击目标节点", ZhTW = "請點擊目標節點", En = "Click the target node", Ja = "終点ノードをクリック", Ko = "대상 노드를 클릭하세요" },
        new Sys_Lang { LangKey = "拖动节点移动；连线模式下点两节点连线", ZhCN = "拖动节点移动；连线模式下点两节点连线", ZhTW = "拖動節點移動；連線模式下點兩節點連線", En = "Drag nodes to move; in connect mode click two nodes to link", Ja = "ノードをドラッグして移動。接続モードで2ノードをクリックして連結", Ko = "노드를 드래그하여 이동; 연결 모드에서 두 노드를 클릭하여 연결" },
        new Sys_Lang { LangKey = "节点属性", ZhCN = "节点属性", ZhTW = "節點屬性", En = "Node Properties", Ja = "ノードのプロパティ", Ko = "노드 속성" },
        new Sys_Lang { LangKey = "连线属性", ZhCN = "连线属性", ZhTW = "連線屬性", En = "Edge Properties", Ja = "接続のプロパティ", Ko = "연결 속성" },
        new Sys_Lang { LangKey = "删除选中", ZhCN = "删除选中", ZhTW = "刪除選中", En = "Delete Selected", Ja = "選択を削除", Ko = "선택 삭제" },
        new Sys_Lang { LangKey = "请先选择节点或连线", ZhCN = "请先选择节点或连线", ZhTW = "請先選擇節點或連線", En = "Select a node or edge first", Ja = "ノードまたは接続を選択してください", Ko = "노드 또는 연결을 먼저 선택하세요" },
        new Sys_Lang { LangKey = "开始", ZhCN = "开始", ZhTW = "開始", En = "Start", Ja = "開始", Ko = "시작" },
        new Sys_Lang { LangKey = "审批", ZhCN = "审批", ZhTW = "審批", En = "Approval", Ja = "承認", Ko = "승인" },
        new Sys_Lang { LangKey = "审批人策略", ZhCN = "审批人策略", ZhTW = "審批人策略", En = "Approver Strategy", Ja = "承認者戦略", Ko = "승인자 전략" },
        new Sys_Lang { LangKey = "指定人员", ZhCN = "指定人员", ZhTW = "指定人員", En = "Specified User", Ja = "指定ユーザー", Ko = "지정 사용자" },
        new Sys_Lang { LangKey = "指定审批人", ZhCN = "指定审批人", ZhTW = "指定審批人", En = "Specified Approver", Ja = "指定承認者", Ko = "지정 승인자" },
        new Sys_Lang { LangKey = "用户ID", ZhCN = "用户ID", ZhTW = "使用者ID", En = "User ID", Ja = "ユーザーID", Ko = "사용자 ID" },
        new Sys_Lang { LangKey = "审批角色", ZhCN = "审批角色", ZhTW = "審批角色", En = "Approval Role", Ja = "承認ロール", Ko = "승인 역할" },
        new Sys_Lang { LangKey = "上级层数", ZhCN = "上级层数", ZhTW = "上級層數", En = "Manager Levels", Ja = "上位レベル数", Ko = "상위 레벨 수" },
        new Sys_Lang { LangKey = "会签规则", ZhCN = "会签规则", ZhTW = "會簽規則", En = "Countersign Rule", Ja = "合議ルール", Ko = "공동 서명 규칙" },
        new Sys_Lang { LangKey = "全部同意", ZhCN = "全部同意", ZhTW = "全部同意", En = "All Approve", Ja = "全員承認", Ko = "전원 동의" },
        new Sys_Lang { LangKey = "任一同意", ZhCN = "任一同意", ZhTW = "任一同意", En = "Any Approve", Ja = "いずれか承認", Ko = "하나라도 동의" },
        new Sys_Lang { LangKey = "一票否决", ZhCN = "一票否决", ZhTW = "一票否決", En = "Single Veto", Ja = "一票否決", Ko = "한 표 거부" },
        new Sys_Lang { LangKey = "超时(小时)", ZhCN = "超时(小时)", ZhTW = "逾時(小時)", En = "Timeout (hours)", Ja = "タイムアウト(時間)", Ko = "시간 초과(시간)" },
        new Sys_Lang { LangKey = "超时动作", ZhCN = "超时动作", ZhTW = "逾時動作", En = "Timeout Action", Ja = "タイムアウト動作", Ko = "시간 초과 동작" },
        new Sys_Lang { LangKey = "催办", ZhCN = "催办", ZhTW = "催辦", En = "Remind", Ja = "催促", Ko = "독촉" },
        new Sys_Lang { LangKey = "自动通过", ZhCN = "自动通过", ZhTW = "自動通過", En = "Auto Approve", Ja = "自動承認", Ko = "자동 승인" },
        new Sys_Lang { LangKey = "自动驳回", ZhCN = "自动驳回", ZhTW = "自動駁回", En = "Auto Reject", Ja = "自動却下", Ko = "자동 반려" },
        new Sys_Lang { LangKey = "升级", ZhCN = "升级", ZhTW = "升級", En = "Escalate", Ja = "エスカレーション", Ko = "에스컬레이션" },
        new Sys_Lang { LangKey = "升级对象", ZhCN = "升级对象", ZhTW = "升級對象", En = "Escalate To", Ja = "エスカレーション先", Ko = "에스컬레이션 대상" },
        new Sys_Lang { LangKey = "起点", ZhCN = "起点", ZhTW = "起點", En = "From", Ja = "始点", Ko = "시작점" },
        new Sys_Lang { LangKey = "终点", ZhCN = "终点", ZhTW = "終點", En = "To", Ja = "終点", Ko = "끝점" },
        new Sys_Lang { LangKey = "流转条件", ZhCN = "流转条件", ZhTW = "流轉條件", En = "Transition Condition", Ja = "遷移条件", Ko = "전환 조건" },
        new Sys_Lang { LangKey = "空=无条件；如 days <= 3", ZhCN = "空=无条件；如 days <= 3", ZhTW = "空=無條件；如 days <= 3", En = "Empty = unconditional; e.g. days <= 3", Ja = "空＝無条件。例：days <= 3", Ko = "비어 있음 = 무조건; 예: days <= 3" },
        new Sys_Lang { LangKey = "请输入流程标识", ZhCN = "请输入流程标识", ZhTW = "請輸入流程標識", En = "Please enter the flow key", Ja = "フローキーを入力してください", Ko = "플로우 식별자를 입력하세요" },
        new Sys_Lang { LangKey = "该流程尚未定义", ZhCN = "该流程尚未定义", ZhTW = "該流程尚未定義", En = "This flow is not defined yet", Ja = "このフローはまだ定義されていません", Ko = "이 플로우는 아직 정의되지 않았습니다" },
        new Sys_Lang { LangKey = "请填写流程标识与名称", ZhCN = "请填写流程标识与名称", ZhTW = "請填寫流程標識與名稱", En = "Please fill in the flow key and name", Ja = "フローキーと名前を入力してください", Ko = "플로우 식별자와 이름을 입력하세요" },
    };
}
