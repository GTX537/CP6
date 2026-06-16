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
    };
}
