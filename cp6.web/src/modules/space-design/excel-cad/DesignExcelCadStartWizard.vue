<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { isAxiosError } from 'axios'
import { designExcelCadMatchApi } from '@/api/space/designExcelCadMatch'
import {
  designExcelMappingApi,
  type SpaceExcelMappingProfile,
} from '@/api/space/designExcelMapping'
import { designExcelPreflightApi } from '@/api/space/designExcelPreflight'
import { designSourcesApi } from '@/api/space/designSources'
import { uploadReuseNotice } from '@/modules/space-design/sources/uploadReuseNotice'
import type {
  ISpaceExcelPreflightDto,
} from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

const props = defineProps<{
  versionId: string
  floorLogicalId: string
  cadSourceId: string
  cadParseJobId: string
  currentContentRevision: number
  initialExcelSourceId?: string
  initialPreflightJobId?: string
}>()

const emit = defineEmits<{
  close: []
  sourceUploaded: []
  preflightStarted: [sourceId: string, jobId: string]
  started: [jobId: string]
}>()

const maximumExcelBytes = 50 * 1024 * 1024
const file = ref<File | null>(null)
const profiles = ref<SpaceExcelMappingProfile[]>([])
const selectedProfileKey = ref('')
const excelSourceId = ref(props.initialExcelSourceId ?? '')
const preflightJobId = ref(props.initialPreflightJobId ?? '')
const excelSourceName = ref('')
const cadSourceName = ref('当前 CAD 来源')
const sourceState = ref('')
const preflight = ref<ISpaceExcelPreflightDto | null>(null)
const loading = ref(true)
const busy = ref(false)
const error = ref('')
const notice = ref('')
const confirmed = ref(false)
let disposed = false
let preflightIdempotencyKey = ''
let matchIdempotencyKey = ''

const selectedProfile = computed(() => {
  const [id, version] = selectedProfileKey.value.split(':')
  return profiles.value.find(candidate =>
    candidate.id === id && candidate.version === Number(version),
  )
})
const preflightTerminal = computed(() => [
  'Succeeded',
  'Failed',
  'Cancelled',
  'DeadLetter',
].includes(preflight.value?.status ?? ''))
const canStartPreflight = computed(() => Boolean(
  file.value && selectedProfile.value && !busy.value,
))
const canStartMatch = computed(() => Boolean(
  preflight.value?.status === 'Succeeded'
  && preflight.value.canConfirm
  && preflight.value.blockingCount === 0
  && confirmed.value
  && Number.isInteger(props.currentContentRevision)
  && !busy.value,
))

onMounted(() => void initialize())
onBeforeUnmount(() => {
  disposed = true
})

async function initialize(): Promise<void> {
  loading.value = true
  error.value = ''
  notice.value = ''
  try {
    const [loadedProfiles, sourcePage] = await Promise.all([
      designExcelMappingApi.listProfiles(),
      designSourcesApi.list(props.versionId),
    ])
    profiles.value = loadedProfiles
    const cad = sourcePage.items?.find(source => source.id === props.cadSourceId)
    cadSourceName.value = cad?.displayName || '当前 CAD 来源'
    const excel = sourcePage.items?.find(source => source.id === excelSourceId.value)
    excelSourceName.value = excel?.displayName ?? ''
    sourceState.value = excel?.state ?? ''
    const first = profiles.value[0]
    if (first) selectedProfileKey.value = `${first.id}:${first.version}`
    if (excelSourceId.value && preflightJobId.value) {
      await monitorPreflight()
    }
  } catch (cause) {
    error.value = message(cause, '无法加载 Excel 映射与来源信息。')
  } finally {
    loading.value = false
  }
}

function chooseFile(event: Event): void {
  const input = event.currentTarget as HTMLInputElement
  const selected = input.files?.[0] ?? null
  input.value = ''
  error.value = ''
  notice.value = ''
  preflight.value = null
  confirmed.value = false
  if (!selected) return
  if (!selected.name.toLocaleLowerCase().endsWith('.xlsx')) {
    error.value = '请选择 .xlsx 工作簿；旧 .xls 和其他格式不会上传。'
    return
  }
  if (selected.size > maximumExcelBytes) {
    error.value = 'Excel 文件不能超过 50MB。'
    return
  }
  file.value = selected
  excelSourceName.value = selected.name
}

async function startPreflight(): Promise<void> {
  const selected = file.value
  const profile = selectedProfile.value
  if (!selected || !profile || busy.value) return
  busy.value = true
  error.value = ''
  preflight.value = null
  confirmed.value = false
  try {
    const uploaded = await designExcelPreflightApi.upload(props.versionId, selected)
    const sourceId = uploaded.source.id
    if (!sourceId) throw new Error('Excel source identity is unavailable')
    excelSourceId.value = sourceId
    excelSourceName.value = uploaded.source.displayName || selected.name
    sourceState.value = uploaded.source.state ?? ''
    notice.value = uploadReuseNotice('Excel', uploaded.reused) ?? ''
    emit('sourceUploaded')
    await waitForReadySource(sourceId)
    preflightIdempotencyKey ||= crypto.randomUUID()
    const started = await designExcelPreflightApi.start(
      props.versionId,
      sourceId,
      {
        mappingProfileId: profile.id,
        mappingProfileVersion: profile.version,
      },
      preflightIdempotencyKey,
    )
    if (!started.jobId) throw new Error('Excel preflight identity is unavailable')
    preflightJobId.value = started.jobId
    emit('preflightStarted', sourceId, started.jobId)
    await monitorPreflight()
  } catch (cause) {
    error.value = message(cause, 'Excel 预检启动失败；当前 Draft 未变更。')
  } finally {
    busy.value = false
  }
}

async function waitForReadySource(sourceId: string): Promise<void> {
  for (let attempt = 0; attempt < 150 && !disposed; attempt += 1) {
    const page = await designSourcesApi.list(props.versionId)
    const source = page.items?.find(candidate => candidate.id === sourceId)
    sourceState.value = source?.state ?? sourceState.value
    if (source?.state === 'Ready' || source?.state === 'PreviewReady') return
    if (source?.state === 'Rejected' || source?.state === 'Removed') {
      throw new Error('Excel 安全扫描未通过')
    }
    await delay(2_000)
  }
  if (!disposed) throw new Error('等待 Excel 安全扫描超时')
}

async function monitorPreflight(): Promise<void> {
  if (!excelSourceId.value || !preflightJobId.value) return
  for (let attempt = 0; attempt < 450 && !disposed; attempt += 1) {
    const loaded = await designExcelPreflightApi.get(
      props.versionId,
      excelSourceId.value,
      preflightJobId.value,
    )
    preflight.value = loaded
    sourceState.value = loaded.sourceState ?? sourceState.value
    if ([
      'Succeeded',
      'Failed',
      'Cancelled',
      'DeadLetter',
    ].includes(loaded.status ?? '')) return
    await delay(2_000)
  }
  if (!disposed) error.value = '等待 Excel 预检超时；后台任务可稍后继续恢复。'
}

async function startMatch(): Promise<void> {
  const preview = preflight.value
  if (!canStartMatch.value || !preview || busy.value) return
  busy.value = true
  error.value = ''
  try {
    matchIdempotencyKey ||= crypto.randomUUID()
    const started = await designExcelCadMatchApi.start(
      props.versionId,
      {
        excelSourceId: excelSourceId.value,
        preflightJobId: preflightJobId.value,
        cadSourceId: props.cadSourceId,
        cadParseJobId: props.cadParseJobId,
        floorLogicalId: props.floorLogicalId,
        expectedContentRevision: props.currentContentRevision,
      },
      matchIdempotencyKey,
    )
    if (!started.jobId) throw new Error('Excel-CAD match identity is unavailable')
    emit('started', started.jobId)
  } catch (cause) {
    error.value = message(
      cause,
      '权威匹配启动失败；CAD、Excel 与当前 Draft 均未被修改。',
    )
  } finally {
    busy.value = false
  }
}

async function downloadReport(): Promise<void> {
  if (!excelSourceId.value || !preflightJobId.value) return
  try {
    const blob = await designExcelPreflightApi.downloadReport(
      props.versionId,
      excelSourceId.value,
      preflightJobId.value,
    )
    const href = URL.createObjectURL(blob)
    const anchor = document.createElement('a')
    anchor.href = href
    anchor.download = `excel-preflight-${excelSourceName.value || 'report'}.csv`
    anchor.click()
    URL.revokeObjectURL(href)
  } catch (cause) {
    error.value = message(cause, 'Excel 预检报告下载失败。')
  }
}

function message(cause: unknown, fallback: string): string {
  if (isAxiosError(cause)) {
    const detail = cause.response?.data?.detail
    if (typeof detail === 'string' && detail.trim()) return detail
  }
  if (cause instanceof Error && cause.message.trim()) return cause.message
  return fallback
}

function delay(milliseconds: number): Promise<void> {
  return new Promise(resolve => window.setTimeout(resolve, milliseconds))
}
</script>

<template>
  <div class="excel-cad-backdrop" role="presentation">
    <section
      class="excel-cad-wizard"
      role="dialog"
      aria-modal="true"
      aria-labelledby="excel-cad-title"
      data-test="excel-cad-start-wizard"
    >
      <header>
        <div>
          <span class="eyebrow">AUTHORITATIVE MATCH</span>
          <h2 id="excel-cad-title">Excel + CAD 权威匹配</h2>
          <p>上传业务属性工作簿，预检通过后与 {{ cadSourceName }} 匹配；确认前零 Draft 写入。</p>
        </div>
        <button type="button" class="icon-button" aria-label="关闭 Excel CAD 向导" @click="emit('close')">×</button>
      </header>

      <div class="wizard-body" :aria-busy="loading || busy">
        <section class="step">
          <span class="step-number">1</span>
          <div class="fields">
            <h3>工作簿与映射</h3>
            <label>Excel 工作簿
              <input type="file" accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" @change="chooseFile" />
            </label>
            <label>Excel 映射 Profile
              <select v-model="selectedProfileKey" aria-label="Excel 映射 Profile" :disabled="loading || busy || Boolean(preflightJobId)">
                <option value="" disabled>选择服务器已知 Profile</option>
                <option v-for="profile in profiles" :key="`${profile.id}:${profile.version}`" :value="`${profile.id}:${profile.version}`">
                  {{ profile.name }} · {{ profile.scope === 'Tenant' ? '租户私有' : '系统公共' }} · v{{ profile.version }}
                </option>
              </select>
            </label>
            <p class="source-state">CAD：{{ cadSourceName }} · Excel：{{ excelSourceName || '未选择' }} {{ sourceState ? `· ${sourceState}` : '' }}</p>
            <button type="button" class="primary" :disabled="!canStartPreflight" @click="startPreflight">
              {{ busy && !preflightTerminal ? '上传、扫描与预检中…' : '上传并运行预检' }}
            </button>
          </div>
        </section>

        <section v-if="preflight" class="step">
          <span class="step-number">2</span>
          <div>
            <h3>Excel 预检 · {{ preflight.status }}</h3>
            <div class="metrics" data-test="excel-preflight-summary">
              <span>数据行 <strong>{{ preflight.dataRowCount ?? 0 }}</strong></span>
              <span>有效 <strong>{{ preflight.validRowCount ?? 0 }}</strong></span>
              <span>信息 <strong>{{ preflight.infoCount ?? 0 }}</strong></span>
              <span>警告 <strong>{{ preflight.warningCount ?? 0 }}</strong></span>
              <span class="blocking">阻断 <strong>{{ preflight.blockingCount ?? 0 }}</strong></span>
            </div>
            <ul v-if="preflight.issues?.length" class="issues" aria-label="Excel 预检问题">
              <li v-for="issue in preflight.issues" :key="issue.id" :class="{ blocking: issue.severity === 'Blocking' }">
                <strong>{{ issue.severity }} · {{ issue.code }}</strong>
                <span>{{ issue.sheet || '工作簿' }}{{ issue.row ? ` / 第 ${issue.row} 行` : '' }}{{ issue.column ? ` / ${issue.column} 列` : '' }}</span>
                <small>{{ issue.fixHint || '请修正工作簿后重新上传。' }}</small>
              </li>
            </ul>
            <button v-if="preflightTerminal" type="button" @click="downloadReport">下载预检 CSV 报告</button>
            <label class="confirmation">
              <input v-model="confirmed" type="checkbox" :disabled="!preflight.canConfirm || (preflight.blockingCount ?? 0) > 0" />
              我已复核 Excel 预检，并确认将它与当前 CAD、楼层和最新 Draft Revision 建立权威匹配。
            </label>
          </div>
        </section>

        <p v-if="error" class="error" role="alert">{{ error }}</p>
        <p v-if="notice" class="notice" role="status">{{ notice }}</p>
      </div>

      <footer>
        <span>匹配成功后进入现有审核面板；只有再次确认 Apply 才会原子写入 Draft。</span>
        <div>
          <button type="button" @click="emit('close')">稍后继续</button>
          <button type="button" class="primary" :disabled="!canStartMatch" @click="startMatch">确认并生成匹配结果</button>
        </div>
      </footer>
    </section>
  </div>
</template>

<style scoped>
.excel-cad-backdrop { position:fixed; inset:0; z-index:1200; display:grid; place-items:center; padding:24px; background:rgba(2,8,18,.78); }
.excel-cad-wizard { width:min(920px,100%); max-height:calc(100vh - 48px); overflow:auto; border:1px solid #2a3950; border-radius:12px; color:#f4f7fb; background:#111a2b; box-shadow:0 28px 90px rgba(0,0,0,.55); }
header,footer { display:flex; align-items:center; justify-content:space-between; gap:24px; padding:18px 22px; border-bottom:1px solid #2a3950; }
footer { border-top:1px solid #2a3950; border-bottom:0; color:#aebbd0; font-size:14px; }
footer div { display:flex; gap:10px; }
h2,h3,p { margin:0; }
h2 { margin:3px 0 5px; font-size:22px; }
h3 { margin-bottom:10px; font-size:17px; }
.eyebrow { color:#18c2c9; font-size:13px; font-weight:800; letter-spacing:.08em; }
.icon-button { width:44px; height:44px; border:0; font-size:28px; background:transparent; }
.wizard-body { display:grid; gap:1px; background:#2a3950; }
.step { display:grid; grid-template-columns:44px 1fr; gap:14px; padding:18px 22px; background:#111a2b; }
.step-number { display:grid; place-items:center; width:36px; height:36px; border:1px solid #18c2c9; border-radius:50%; color:#18c2c9; font-weight:800; }
.fields { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:12px; }
.fields h3,.fields .source-state,.fields .primary { grid-column:1/-1; }
label { display:grid; gap:5px; color:#c6d2e3; font-size:14px; }
input,select,button { box-sizing:border-box; min-height:44px; border:1px solid #3b4d67; border-radius:6px; color:#f4f7fb; background:#172236; padding:8px 10px; font:inherit; }
button { cursor:pointer; }
button:focus-visible,input:focus-visible,select:focus-visible { outline:3px solid #8cebf0; outline-offset:2px; }
button:disabled { cursor:not-allowed; opacity:.45; }
.primary { border-color:#18c2c9; color:#041014; background:#18c2c9; font-weight:800; }
.source-state { color:#aebbd0; font-size:14px; }
.metrics { display:grid; grid-template-columns:repeat(5,minmax(0,1fr)); gap:8px; margin-bottom:14px; }
.metrics span { padding:10px; border:1px solid #2a3950; border-radius:6px; background:#0d1626; }
.issues { max-height:240px; overflow:auto; margin:0 0 12px; padding:0; list-style:none; }
.issues li { display:grid; gap:3px; padding:10px; border:1px solid #2a3950; border-radius:6px; color:#ffd27a; font-size:16px; }
.issues li + li { margin-top:6px; }
.issues small { color:#aebbd0; font-size:16px; }
.confirmation { display:flex; align-items:flex-start; gap:10px; margin-top:14px; font-size:16px; }
.confirmation input { width:44px; height:44px; flex:0 0 44px; margin:0; }
.blocking,.error { color:#ff8590; }
.error { padding:14px 22px; background:#321922; }
.notice { padding:14px 22px; color:#8cebf0; background:#102b35; }
@media (max-width:760px) { .fields,.metrics { grid-template-columns:1fr; } .metrics { grid-template-columns:repeat(2,1fr); } header,footer { align-items:flex-start; flex-direction:column; } }
</style>
