<!--
  编码规则管理 —— CpPageShell + CpListPage + CpFormDialog（Space 波3 生命周期）。
  套用 SpaceSiteView/FloorView 范式：codeRuleApi.list() 前端切片、in-place 变更后 listRef.reload()。
  差异点：
   - scopeId 显示名解析：list 后并发拉 site→floor(→zone) 建索引，scopeNames 反查显示；失败留裸 id 截断。
   - 作用域级联表单：scopeType=1 站点→楼层；=2 站点→楼层→库区；=0 隐藏。编辑时以索引反查回填级联。
   - SegmentsEditor（段编辑器，v-model form.segments）+ 本地镜像校验黄条（权威=后端 preview）。
   - 预览弹窗：编辑中/行 segments 直接 POST preview，展示 samples / variableLen / precheck。
   - isDefault 提示「设为默认将自动取消同作用域其他默认」（后端自动清，无冲突报错）。
  删除失败的后端护栏 message 由 http 拦截器原样 ElMessage.error（此处 catch 仅止崩）。
-->
<template>
  <CpPageShell :title="t('space.rule.title')" :count="total">
    <template #actions>
      <el-button v-permission="'space-code-rule:add'" @click="openCreate">{{ t('space.rule.create') }}</el-button>
    </template>

    <CpListPage
      ref="listRef"
      :columns="columns"
      :fetch="fetchList"
      @total-change="total = $event"
    >
      <template #col-scopeId="{ row }">
        <span>{{ scopeDisplay(row) }}</span>
      </template>
      <template #col-_action="{ row }">
        <el-button v-permission="'space-code-rule:edit'" link type="primary" size="small" @click="openEdit(row)">{{ t('space.common.edit') }}</el-button>
        <el-button link type="primary" size="small" @click="openPreview(row.segments)">{{ t('space.rule.preview') }}</el-button>
        <el-button v-permission="'space-code-rule:delete'" link type="danger" size="small" @click="onDelete(row)">{{ t('space.common.delete') }}</el-button>
      </template>
    </CpListPage>

    <CpFormDialog
      v-model="dialogVisible"
      :title="dialogTitle"
      width="960"
      :form="form"
      :rules="rules"
      :submit="onSave"
      :labels="{ cancel: t('space.common.cancel'), confirm: t('space.common.save') }"
      @saved="reloadList"
    >
      <el-form-item :label="t('space.rule.fld.ruleName')" prop="ruleName">
        <el-input v-model="form.ruleName" maxlength="100" />
      </el-form-item>

      <el-form-item :label="t('space.rule.fld.scopeType')">
        <el-select v-model="form.scopeType" style="width: 240px" @change="onScopeTypeChange">
          <el-option v-for="o in scopeTypeOptions" :key="o.value" :label="o.label" :value="o.value" />
        </el-select>
      </el-form-item>

      <template v-if="form.scopeType === 1">
        <el-form-item :label="t('space.rule.fld.site')">
          <el-select v-model="selSiteId" filterable style="width: 320px" :placeholder="t('space.rule.selectSite')">
            <el-option v-for="s in cSites" :key="s.id" :label="`${s.siteCode}　${s.siteName}`" :value="s.id!" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('space.rule.fld.floor')" prop="scopeId">
          <el-select v-model="form.scopeId" filterable style="width: 320px" :placeholder="t('space.rule.selectFloor')">
            <el-option v-for="f in cFloors" :key="f.id" :label="`${f.floorCode}　${f.floorName}`" :value="f.id" />
          </el-select>
        </el-form-item>
      </template>

      <template v-else-if="form.scopeType === 2">
        <el-form-item :label="t('space.rule.fld.site')">
          <el-select v-model="selSiteId" filterable style="width: 320px" :placeholder="t('space.rule.selectSite')">
            <el-option v-for="s in cSites" :key="s.id" :label="`${s.siteCode}　${s.siteName}`" :value="s.id!" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('space.rule.fld.floor')">
          <el-select v-model="selFloorId" filterable style="width: 320px" :placeholder="t('space.rule.selectFloor')">
            <el-option v-for="f in cFloors" :key="f.id" :label="`${f.floorCode}　${f.floorName}`" :value="f.id" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('space.rule.fld.zone')" prop="scopeId">
          <el-select v-model="form.scopeId" filterable style="width: 320px" :placeholder="t('space.rule.selectZone')">
            <el-option v-for="z in cZones" :key="z.id" :label="`${z.zoneCode}　${z.zoneName}`" :value="z.id" />
          </el-select>
        </el-form-item>
      </template>

      <el-form-item :label="t('space.rule.fld.isDefault')">
        <el-checkbox v-model="form.isDefault">{{ t('space.rule.fld.isDefault') }}</el-checkbox>
        <span class="rule-tip">{{ t('space.rule.isDefaultTip') }}</span>
      </el-form-item>

      <SegmentsEditor v-model="form.segments" />

      <div class="rule-pv-actions">
        <el-button @click="openPreview(form.segments)">{{ t('space.rule.preview') }}</el-button>
      </div>
    </CpFormDialog>

    <!-- 预览弹窗（独立 el-dialog，不走 CpFormDialog） -->
    <el-dialog v-model="previewVisible" :title="t('space.rule.pv.title')" width="560">
      <div v-loading="previewLoading" class="rule-pv">
        <template v-if="previewData">
          <div class="pv-sec">
            <div class="pv-label">{{ t('space.rule.pv.samples') }}</div>
            <div class="pv-samples">
              <span v-for="(s, i) in previewData.samples" :key="i" class="cp-mono pv-sample">{{ s }}</span>
              <span v-if="!previewData.samples.length" class="pv-muted">—</span>
            </div>
          </div>

          <div class="pv-sec">
            <div class="pv-label">{{ t('space.rule.pv.variableLen') }}</div>
            <div class="pv-vl">
              <span class="pv-muted">{{ t('space.rule.pv.withAisle') }}:</span>
              <span class="cp-mono">{{ previewData.variableLen.withAisle }}</span>
            </div>
            <div class="pv-vl">
              <span class="pv-muted">{{ t('space.rule.pv.withoutAisle') }}:</span>
              <span class="cp-mono">{{ previewData.variableLen.withoutAisle }}</span>
            </div>
          </div>

          <div class="pv-sec">
            <div class="pv-label">{{ t('space.rule.pv.precheck') }}</div>
            <div v-if="previewData.precheck.ok" class="pv-ok">{{ t('space.rule.pv.ok') }}</div>
            <ul v-else class="pv-errs">
              <li v-for="(e, i) in previewData.precheck.errors" :key="i">{{ e }}</li>
            </ul>
          </div>
        </template>
        <div v-else-if="!previewLoading" class="pv-muted">{{ t('space.rule.pv.empty') }}</div>
      </div>
    </el-dialog>
  </CpPageShell>
</template>

<script setup lang="ts">
import { ref, reactive, computed, watch, nextTick } from 'vue'
import { ElMessage, ElMessageBox, type FormRules } from 'element-plus'
import { useI18n } from 'vue-i18n'
import CpPageShell from '@/components/templates/CpPageShell.vue'
import CpListPage, { type ListColumn, type ListFetch, type ListPageExpose } from '@/components/templates/CpListPage.vue'
import CpFormDialog from '@/components/templates/CpFormDialog.vue'
import type { Tone } from '@/components/base/CpTag.vue'
import SegmentsEditor from './SegmentsEditor.vue'
import { codeRuleApi } from '@/api/space/codeRule'
import { siteApi } from '@/api/space/site'
import { floorApi } from '@/api/space/floor'
import { zoneApi } from '@/api/space/zone'
import type { CodeRuleVO, CodeSegmentDef, CodePreviewResp, SiteVO, FloorVO, ZoneVO } from '@/types/space/scene'

const { t } = useI18n()

const total = ref<number>()
const listRef = ref<ListPageExpose | null>(null)
function reloadList() { listRef.value?.reload() }

// —— 作用域显示名解析（list 后并发建索引；反查回填编辑级联）——
const scopeNames = ref<Record<string, string>>({})
const floorIndex = ref<Record<string, { siteId: string; name: string }>>({})
const zoneIndex = ref<Record<string, { floorId: string; name: string }>>({})

function truncId(id: string): string {
  return id.length > 10 ? `${id.slice(0, 10)}…` : id
}
function scopeDisplay(row: CodeRuleVO): string {
  if (row.scopeType === 0 || !row.scopeId) return '—'
  return scopeNames.value[row.scopeId] || truncId(row.scopeId)
}

// best-effort：仅当存在 type1/2 规则才拉；任意子请求失败静默（留裸 id 截断）
async function resolveScopeNames(rules: CodeRuleVO[]) {
  const needFloor = rules.some((r) => r.scopeType === 1 || r.scopeType === 2)
  const needZone = rules.some((r) => r.scopeType === 2)
  if (!needFloor) return
  try {
    const sitesRes = await siteApi.list()
    const sitesArr = sitesRes.data || []
    const floorLists = await Promise.all(
      sitesArr.map((s) => floorApi.list(s.id!).then((r) => r.data || []).catch(() => [] as FloorVO[])),
    )
    const allFloors = floorLists.flat()
    const names: Record<string, string> = { ...scopeNames.value }
    const fIdx: Record<string, { siteId: string; name: string }> = { ...floorIndex.value }
    for (const f of allFloors) {
      const nm = `${f.floorCode} ${f.floorName}`
      fIdx[f.id] = { siteId: f.siteId, name: nm }
      names[f.id] = nm
    }
    floorIndex.value = fIdx
    if (needZone) {
      const zoneLists = await Promise.all(
        allFloors.map((f) => zoneApi.list(f.id).then((r) => r.data || []).catch(() => [] as ZoneVO[])),
      )
      const zIdx: Record<string, { floorId: string; name: string }> = { ...zoneIndex.value }
      for (const z of zoneLists.flat()) {
        const nm = `${z.zoneCode} ${z.zoneName}`
        zIdx[z.id] = { floorId: z.floorId, name: nm }
        names[z.id] = nm
      }
      zoneIndex.value = zIdx
    }
    scopeNames.value = names
  } catch { /* best-effort：失败留裸 id 截断 */ }
}

const scopeTypeOptions = computed(() => [
  { value: 0, label: t('space.rule.scope.0') },
  { value: 1, label: t('space.rule.scope.1') },
  { value: 2, label: t('space.rule.scope.2') },
])
const scopeTone = (v: number): Tone => (v === 1 ? 'info' : v === 2 ? 'warn' : 'muted')

const columns = computed<ListColumn<CodeRuleVO>[]>(() => [
  { prop: 'ruleName', label: t('space.rule.fld.ruleName'), minWidth: 200 },
  { prop: 'scopeType', label: t('space.rule.fld.scopeType'), width: 130, kind: 'tag',
    map: (v) => ({ label: t(`space.rule.scope.${v as number}`), tone: scopeTone(v as number) }) },
  { prop: 'scopeId', label: t('space.rule.fld.scopeId'), minWidth: 180 },
  { prop: 'segments', label: t('space.rule.fld.segments'), width: 110, align: 'center',
    map: (v) => ({ label: String((v as CodeSegmentDef[] | undefined)?.length ?? 0) }) },
  { prop: 'isDefault', label: t('space.rule.fld.isDefault'), width: 110, align: 'center', kind: 'tag',
    map: (v) => (v
      ? { label: t('space.rule.default.yes'), tone: 'ok' }
      : { label: t('space.rule.default.no'), tone: 'muted' }) },
  { prop: '_action', label: t('space.common.action'), width: 220, fixed: 'right' },
])

// list 端点无分页 → 前端切片；并发触发显示名解析（fire-and-forget）
const fetchList: ListFetch<CodeRuleVO> = async ({ page, size }) => {
  const res = await codeRuleApi.list()
  const all = res.data || []
  void resolveScopeNames(all)
  const start = (page - 1) * size
  return { rows: all.slice(start, start + size), total: all.length }
}

// —— 新建/编辑对话框 ——
const dialogVisible = ref(false)
const form = reactive<CodeRuleVO>({ ruleName: '', scopeType: 0, scopeId: null, segments: [], isDefault: false })
const dialogTitle = computed(() => (form.id ? t('space.rule.dlg.edit') : t('space.rule.dlg.create')))
const rules = computed<FormRules>(() => ({
  ruleName: [{ required: true, message: t('space.common.required'), trigger: 'blur' }],
  ...(form.scopeType !== 0
    ? { scopeId: [{ required: true, message: t('space.common.required'), trigger: 'change' }] }
    : {}),
}))

// —— 级联选项与中间选择 ——
const cSites = ref<SiteVO[]>([])
const cFloors = ref<FloorVO[]>([])
const cZones = ref<ZoneVO[]>([])
const selSiteId = ref<string | undefined>()
const selFloorId = ref<string | undefined>()
const hydrating = ref(false) // 编辑回填期抑制级联联动清空

async function loadSites() {
  if (cSites.value.length) return
  const res = await siteApi.list().catch(() => ({ data: [] as SiteVO[] }) as { data: SiteVO[] })
  cSites.value = res.data || []
}

watch(selSiteId, async (nv) => {
  cFloors.value = []
  cZones.value = []
  if (!hydrating.value) { selFloorId.value = undefined; form.scopeId = null }
  if (nv) cFloors.value = await floorApi.list(nv).then((r) => r.data || []).catch(() => [])
})
watch(selFloorId, async (nv) => {
  cZones.value = []
  if (!hydrating.value && form.scopeType === 2) form.scopeId = null
  if (nv) cZones.value = await zoneApi.list(nv).then((r) => r.data || []).catch(() => [])
})

function onScopeTypeChange() {
  form.scopeId = null
  selSiteId.value = undefined
  selFloorId.value = undefined
  cFloors.value = []
  cZones.value = []
}

async function openCreate() {
  Object.assign(form, { id: undefined, ruleName: '', scopeType: 0, scopeId: null, segments: [], isDefault: false })
  selSiteId.value = undefined
  selFloorId.value = undefined
  await loadSites()
  dialogVisible.value = true
}

async function openEdit(row: CodeRuleVO) {
  Object.assign(form, {
    id: row.id, ruleName: row.ruleName, scopeType: row.scopeType, scopeId: row.scopeId,
    isDefault: row.isDefault, segments: (row.segments || []).map((s) => ({ ...s })), // 深拷贝段，避免直改列表行
  })
  hydrating.value = true
  await loadSites()
  if (row.scopeType === 1 && row.scopeId) {
    selSiteId.value = floorIndex.value[row.scopeId]?.siteId
    await nextTick()
  } else if (row.scopeType === 2 && row.scopeId) {
    const zi = zoneIndex.value[row.scopeId]
    selSiteId.value = zi ? floorIndex.value[zi.floorId]?.siteId : undefined
    await nextTick()
    selFloorId.value = zi?.floorId
    await nextTick()
  }
  hydrating.value = false
  dialogVisible.value = true
}

async function onSave() {
  const payload: CodeRuleVO = { ...form, segments: form.segments }
  if (form.scopeType === 0) payload.scopeId = null
  if (form.id) await codeRuleApi.update(form.id, payload)
  else await codeRuleApi.create(payload)
  ElMessage.success(t('space.common.success'))
}

async function onDelete(row: CodeRuleVO) {
  try {
    await ElMessageBox.confirm(`${t('space.common.confirmDelete')} [${row.ruleName}]`, t('space.common.confirm'), { type: 'warning' })
    await codeRuleApi.remove(row.id!)
    ElMessage.success(t('space.common.success'))
    reloadList()
  } catch { /* 取消 / 后端护栏错误（后者已由 http 拦截器原样提示） */ }
}

// —— 预览弹窗 ——
const previewVisible = ref(false)
const previewLoading = ref(false)
const previewData = ref<CodePreviewResp | null>(null)
async function openPreview(segments: CodeSegmentDef[]) {
  previewVisible.value = true
  previewLoading.value = true
  previewData.value = null
  try {
    const res = await codeRuleApi.preview(segments || [])
    previewData.value = res.data
  } catch {
    previewData.value = null // 错误 message 已由 http 拦截器提示
  } finally {
    previewLoading.value = false
  }
}
</script>

<style scoped>
.rule-tip { margin-left: 12px; color: var(--cp-muted); font-size: var(--cp-fs-sm); }
.rule-pv-actions { display: flex; justify-content: flex-end; margin-top: 12px; }

.rule-pv { display: flex; flex-direction: column; gap: 16px; }
.pv-sec { display: flex; flex-direction: column; gap: 6px; }
.pv-label { font-weight: 700; color: var(--cp-ink); }
.pv-samples { display: flex; flex-wrap: wrap; gap: 8px; }
.pv-sample { padding: 2px 8px; background: var(--cp-brand-bg, #eef2ff); border-radius: 4px; }
.pv-vl { display: flex; gap: 8px; align-items: baseline; }
.pv-muted { color: var(--cp-muted); }
.pv-ok { color: #389e0d; font-weight: 700; }
.pv-errs { margin: 0; padding-left: 18px; color: #cf1322; }

/* 本地补 .cp-mono（scoped；波2 前车之鉴——勿依赖模板全局类） */
.cp-mono { font-family: var(--cp-font-mono, ui-monospace, SFMono-Regular, Menlo, Consolas, monospace);
  font-weight: 700; color: var(--cp-brand-deep); }
</style>
