/* ============================================================
 * Space 空間管理 i18n シード（波2 P0）
 * ============================================================
 * 範囲：space.common.* / space.site.* / space.floor.* / space.home.*
 * 対象テーブル：Sys_Langs（列 LangKey / ZhCN / ZhTW / En / Ja / Ko）
 * 冪等：MERGE（既存キーは UPDATE、無ければ INSERT）
 * 実行：sqlcmd -S localhost\KOUSQLSERVER -d CP6DB -E -f 65001 -i docs/seeds/space-i18n-seed.sql -b
 *
 * キーの唯一の権威 = 3 コンポーネント実コード grep（Task 5 で対照）:
 *   SpaceHomeView.vue / SpaceSiteView.vue / SpaceFloorView.vue
 * ★ space.home.title は死キー（ページ未描画）につき種入から除外（主控裁決）。
 * ★ space.common.* は 3 画面共用のため重複排除済み（14 件）。
 * 合計 43 キー = common 14 + site 11 + floor 11 + home 7。
 * ============================================================ */
SET NOCOUNT ON; SET XACT_ABORT ON;
PRINT '=== Space i18n シード開始 ===';

IF OBJECT_ID('tempdb..#i18n') IS NOT NULL DROP TABLE #i18n;
CREATE TABLE #i18n (
    LangKey nvarchar(200) NOT NULL PRIMARY KEY,
    ZhCN nvarchar(500), ZhTW nvarchar(500), En nvarchar(500), Ja nvarchar(500), Ko nvarchar(500)
);

INSERT INTO #i18n VALUES
  -- ── space.common.*（3 画面共用・14 件）──────────────────────────────
  (N'space.common.status',        N'状态',     N'狀態',     N'Status',            N'状態',                N'상태'),
  (N'space.common.enabled',       N'启用',     N'啟用',     N'Enabled',           N'有効',                N'사용'),
  (N'space.common.disabled',      N'停用',     N'停用',     N'Disabled',          N'無効',                N'사용 안 함'),
  (N'space.common.action',        N'操作',     N'操作',     N'Actions',           N'操作',                N'작업'),
  (N'space.common.edit',          N'编辑',     N'編輯',     N'Edit',              N'編集',                N'편집'),
  (N'space.common.delete',        N'删除',     N'刪除',     N'Delete',            N'削除',                N'삭제'),
  (N'space.common.search',        N'查询',     N'查詢',     N'Search',            N'検索',                N'검색'),
  (N'space.common.clear',         N'重置',     N'重置',     N'Reset',             N'リセット',            N'초기화'),
  (N'space.common.cancel',        N'取消',     N'取消',     N'Cancel',            N'キャンセル',          N'취소'),
  (N'space.common.save',          N'保存',     N'儲存',     N'Save',              N'保存',                N'저장'),
  (N'space.common.confirm',       N'确认',     N'確認',     N'Confirm',           N'確認',                N'확인'),
  (N'space.common.confirmDelete', N'确认删除？', N'確認刪除？', N'Confirm deletion?', N'削除しますか？',      N'삭제하시겠습니까?'),
  (N'space.common.required',      N'必填项',   N'必填項',   N'Required',          N'必須項目',            N'필수 항목'),
  (N'space.common.success',       N'操作成功', N'操作成功', N'Done',              N'操作が完了しました',  N'완료되었습니다'),
  -- ── space.site.*（SpaceSiteView・11 件）──────────────────────────────
  (N'space.site.title',           N'站点管理', N'站點管理', N'Sites',             N'拠点管理',            N'거점 관리'),
  (N'space.site.create',          N'新建站点', N'新增站點', N'New Site',          N'拠点を新規',          N'거점 신규'),
  (N'space.site.floors',          N'楼层',     N'樓層',     N'Floors',            N'フロア',              N'층'),
  (N'space.site.fld.code',        N'站点编码', N'站點編碼', N'Site Code',         N'拠点コード',          N'거점 코드'),
  (N'space.site.fld.name',        N'站点名称', N'站點名稱', N'Site Name',         N'拠点名',              N'거점명'),
  (N'space.site.fld.warehouseCd', N'倉庫CD',   N'倉庫CD',   N'Warehouse Cd',      N'倉庫CD',              N'창고 CD'),
  (N'space.site.fld.address',     N'地址',     N'地址',     N'Address',           N'住所',                N'주소'),
  (N'space.site.whDefault',       N'空=同站点编码', N'空=同站點編碼', N'Blank = same as Site Code', N'空欄=拠点コードと同じ', N'공백=거점 코드와 동일'),
  (N'space.site.whDefaultTip',    N'未指定，发布时默认取站点编码 {code}', N'未指定，發佈時預設取站點編碼 {code}', N'Unset; publish defaults to site code {code}', N'未指定・発行時は拠点コード {code} を既定使用', N'미지정, 발행 시 거점 코드 {code} 기본 사용'),
  (N'space.site.dlg.create',      N'新建站点', N'新增站點', N'New Site',          N'拠点の新規作成',      N'거점 신규 등록'),
  (N'space.site.dlg.edit',        N'编辑站点', N'編輯站點', N'Edit Site',         N'拠点の編集',          N'거점 편집'),
  -- ── space.floor.*（SpaceFloorView・11 件）────────────────────────────
  (N'space.floor.title',          N'楼层管理', N'樓層管理', N'Floors',            N'フロア管理',          N'층 관리'),
  (N'space.floor.create',         N'新建楼层', N'新增樓層', N'New Floor',         N'フロア新規',          N'층 신규'),
  (N'space.floor.selectSite',     N'请选择站点', N'請選擇站點', N'Select site',    N'拠点を選択',          N'거점 선택'),
  (N'space.floor.pickSiteFirst',  N'请先选择站点', N'請先選擇站點', N'Select a site first', N'先に拠点を選択してください', N'먼저 거점을 선택하세요'),
  (N'space.floor.editor',         N'编辑画面', N'編輯畫面', N'Editor',            N'編集画面',            N'편집 화면'),
  (N'space.floor.fld.level',      N'层号',     N'樓層號',   N'Level',             N'階層',                N'층 번호'),
  (N'space.floor.fld.floorCode',  N'楼层编码', N'樓層編碼', N'Floor Code',        N'フロアコード',        N'층 코드'),
  (N'space.floor.fld.floorName',  N'楼层名称', N'樓層名稱', N'Floor Name',        N'フロア名',            N'층 이름'),
  (N'space.floor.fld.height',     N'层高 (mm)', N'樓層高度 (mm)', N'Height (mm)',  N'高さ (mm)',           N'높이 (mm)'),
  (N'space.floor.dlg.create',     N'新建楼层', N'新增樓層', N'New Floor',         N'フロア新規',          N'층 신규'),
  (N'space.floor.dlg.edit',       N'编辑楼层', N'編輯樓層', N'Edit Floor',        N'フロア編集',          N'층 편집'),
  -- ── space.home.*（SpaceHomeView・7 件・title は死キーにつき除外）───────
  (N'space.home.siteCount',       N'站点数',   N'站點數',   N'Sites',             N'サイト数',            N'사이트 수'),
  (N'space.home.floorCount',      N'楼层数',   N'樓層數',   N'Floors',            N'フロア数',            N'플로어 수'),
  (N'space.home.viewer3d',        N'3D',       N'3D',       N'3D',                N'3D',                  N'3D'),
  (N'space.home.stacked',         N'全景',     N'全景',     N'Stacked',           N'全景',                N'전경'),
  (N'space.home.noFloor',         N'暂无楼层', N'尚無樓層', N'No floors',         N'フロア未登録',        N'플로어 없음'),
  (N'space.home.empty',           N'暂无站点', N'尚無站點', N'No sites yet',      N'サイトがありません',  N'사이트가 없습니다'),
  (N'space.home.createSite',      N'去创建站点', N'前往建立站點', N'Create a site', N'サイトを作成',        N'사이트 만들기');

DECLARE @actionLog TABLE (act nvarchar(10));
MERGE Sys_Langs AS tgt USING #i18n AS src ON tgt.LangKey = src.LangKey
WHEN MATCHED THEN UPDATE SET tgt.ZhCN = src.ZhCN, tgt.ZhTW = src.ZhTW, tgt.En = src.En, tgt.Ja = src.Ja, tgt.Ko = src.Ko
WHEN NOT MATCHED BY TARGET THEN INSERT (LangKey, ZhCN, ZhTW, En, Ja, Ko) VALUES (src.LangKey, src.ZhCN, src.ZhTW, src.En, src.Ja, src.Ko)
OUTPUT $action INTO @actionLog;

DECLARE @ins INT = (SELECT COUNT(*) FROM @actionLog WHERE act = 'INSERT');
DECLARE @upd INT = (SELECT COUNT(*) FROM @actionLog WHERE act = 'UPDATE');
PRINT N'  追加: ' + CAST(@ins AS nvarchar(10)) + N' 件 / 更新: ' + CAST(@upd AS nvarchar(10)) + N' 件（合計 43 キー想定）';
DROP TABLE #i18n;
PRINT '=== Done ===';
