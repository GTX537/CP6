/* ============================================================
 * WFS 波① T10：服务任务面板 韩文(Ko)译文润色（去 Konglish 音译）
 * ============================================================
 * 範囲：oa.designer.svc.*（6 词条，仅 Ko 字段）
 * 背景：SeedLangs(Program.cs:1755) 为 insert-only（判存跳过），
 *       改 I18nOaServiceTaskScreenSeed.cs 常量只对全新库生效；
 *       已部署库这 6 个 LangKey 已存在，须本脚本 UPDATE 补齐。
 * 冪等：UPDATE ... WHERE Ko = 旧值 —— 重复执行零副作用（新值不再匹配旧值）。
 * 実行：sqlcmd -S "localhost\KOUSQLSERVER" -E -d CP6DB -f 65001 -i docs/seeds/wfs-svc-ko-i18n-fix.sql -b
 * ============================================================ */
SET NOCOUNT ON; SET XACT_ABORT ON;
PRINT '=== WFS T10 服务任务 Ko 译文润色 開始 ===';

DECLARE @n int = 0;

-- oa.designer.svc.title：서비스 태스크 → 서비스 작업
UPDATE Sys_Langs SET Ko = N'서비스 작업'
  WHERE LangKey = N'oa.designer.svc.title' AND Ko = N'서비스 태스크';
SET @n += @@ROWCOUNT;

-- oa.designer.svc.kind.dataWriteback：데이터 기록 → 데이터 쓰기
UPDATE Sys_Langs SET Ko = N'데이터 쓰기'
  WHERE LangKey = N'oa.designer.svc.kind.dataWriteback' AND Ko = N'데이터 기록';
SET @n += @@ROWCOUNT;

-- oa.designer.svc.action：액션 → 동작
UPDATE Sys_Langs SET Ko = N'동작'
  WHERE LangKey = N'oa.designer.svc.action' AND Ko = N'액션';
SET @n += @@ROWCOUNT;

-- oa.designer.svc.timerAction：실행 시 액션 → 실행 시 동작
UPDATE Sys_Langs SET Ko = N'실행 시 동작'
  WHERE LangKey = N'oa.designer.svc.timerAction' AND Ko = N'실행 시 액션';
SET @n += @@ROWCOUNT;

-- oa.designer.svc.errorEdge：실패 엣지 → 실패 분기
UPDATE Sys_Langs SET Ko = N'실패 분기'
  WHERE LangKey = N'oa.designer.svc.errorEdge' AND Ko = N'실패 엣지';
SET @n += @@ROWCOUNT;

-- oa.designer.svc.errorEdgeHint：…이 엣지로 진행됩니다 → …이 분기로 진행됩니다
UPDATE Sys_Langs SET Ko = N'서비스 태스크가 재시도를 모두 소진하면 이 분기로 진행됩니다'
  WHERE LangKey = N'oa.designer.svc.errorEdgeHint'
    AND Ko = N'서비스 태스크가 재시도를 모두 소진하면 이 엣지로 진행됩니다';
SET @n += @@ROWCOUNT;

PRINT CONCAT('=== WFS T10 完了: UPDATED=', @n, ' /6 (幂等重跑期望 0) ===');
