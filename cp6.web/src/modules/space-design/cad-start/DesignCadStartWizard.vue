<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import {
  designCadParseApi,
  type PreviewSpaceCadPreparationResponse,
  type SpaceCadGeometryRule,
  type SpaceCadLayerMappingOverride,
  type SpaceCadMappingProfile,
  type SpaceCadSemanticTarget,
  type SpaceCadSiteCapability,
} from '@/api/space/designCadParse'

const props = defineProps<{
  siteId: string
  versionId: string
  sourceId: string
  floorLogicalId: string
}>()

const emit = defineEmits<{
  close: []
  started: [jobId: string]
}>()

const scanState = ref('正在确认安全扫描…')
const profiles = ref<SpaceCadMappingProfile[]>([])
const capability = ref<SpaceCadSiteCapability | null>(null)
const preview = ref<PreviewSpaceCadPreparationResponse | null>(null)
const busy = ref(false)
const error = ref('')
const confirmedConversion = ref(false)
const confirmedMapping = ref(false)
const previewDirty = ref(false)
const layerOverrides = ref<SpaceCadLayerMappingOverride[]>([])
const layerSearch = ref('')
const blockSearch = ref('')
const dialogElement = ref<HTMLElement | null>(null)
let disposed = false

const semanticTargets: Array<{ value: SpaceCadSemanticTarget; label: string }> = [
  { value: 'Wall', label: '墙' },
  { value: 'Column', label: '柱' },
  { value: 'Door', label: '门' },
  { value: 'Dock', label: '月台' },
  { value: 'Zone', label: '库区' },
  { value: 'Aisle', label: '巷道' },
  { value: 'Rack', label: '货架' },
  { value: 'Equipment', label: '静态设备' },
  { value: 'VerticalCirculation', label: '垂直交通' },
  { value: 'Annotation', label: '标注' },
  { value: 'Guide', label: '辅助线' },
  { value: 'RestrictedArea', label: '限制区' },
]
const geometryRules: Array<{ value: SpaceCadGeometryRule; label: string }> = [
  { value: 'DirectGeometry', label: '直接几何' },
  { value: 'Centerline', label: '中心线' },
  { value: 'ClosedBoundary', label: '闭合边界' },
]

const form = reactive({
  confirmedUnit: '',
  sourceOriginX: 0,
  sourceOriginY: 0,
  floorOriginX: 0,
  floorOriginY: 0,
  rotationZDegrees: 0,
  mappingProfileKey: '',
})

const selectedProfile = computed(() => {
  const [id, version] = form.mappingProfileKey.split(':')
  return profiles.value.find(
    (candidate) => candidate.profileId === id && candidate.version === Number(version),
  )
})
const canPreview = computed(() =>
  capability.value?.canPrepareCad === true &&
  Boolean(form.confirmedUnit && selectedProfile.value && props.floorLogicalId) &&
  [
    form.sourceOriginX,
    form.sourceOriginY,
    form.floorOriginX,
    form.floorOriginY,
    form.rotationZDegrees,
  ].every(Number.isFinite),
)
const canStart = computed(() =>
  Boolean(preview.value?.readyForParsing && preview.value.startRequest) &&
  !previewDirty.value && confirmedConversion.value && confirmedMapping.value && !busy.value,
)
const filteredLayers = computed(() => {
  const query = layerSearch.value.trim().toLocaleLowerCase()
  const layers = preview.value?.inventory?.layers ?? []
  if (!query) return layers
  return layers.filter((layer) => [
    layer.layerId,
    layer.name,
    layer.color ?? '',
    layer.lineType ?? '',
  ].some((value) => value.toLocaleLowerCase().includes(query)))
})
const filteredBlocks = computed(() => {
  const query = blockSearch.value.trim().toLocaleLowerCase()
  const blocks = preview.value?.inventory?.blocks ?? []
  if (!query) return blocks
  return blocks.filter((block) => [
    block.blockId,
    block.name,
  ].some((value) => value.toLocaleLowerCase().includes(query)))
})

watch(
  () => [
    form.confirmedUnit,
    form.sourceOriginX,
    form.sourceOriginY,
    form.floorOriginX,
    form.floorOriginY,
    form.rotationZDegrees,
    form.mappingProfileKey,
  ],
  () => markPreviewDirty(),
)

onMounted(async () => {
  await nextTick()
  dialogElement.value?.focus()
  try {
    const [loadedCapability, loadedProfiles] = await Promise.all([
      designCadParseApi.getCadCapability(props.siteId),
      designCadParseApi.listMappingProfiles(props.versionId),
    ])
    capability.value = loadedCapability
    profiles.value = loadedProfiles
    if (!loadedCapability.canPrepareCad) {
      error.value = '当前 Site 没有可用且有效的 CAD Provider；Draft 未变更。'
      return
    }
    await waitForCleanSource()
  } catch (cause) {
    error.value = message(cause, '无法加载 CAD 准备信息')
  }
})

onBeforeUnmount(() => {
  disposed = true
})

async function waitForCleanSource(): Promise<void> {
  for (let attempt = 0; attempt < 150 && !disposed; attempt += 1) {
    const status = await designCadParseApi.getPreparationStatus(
      props.versionId,
      props.sourceId,
    )
    scanState.value = `来源 ${status.sourceState} · 文件 ${status.fileState}`
    if (status.blockingCode) {
      throw new Error('安全扫描未通过；当前 Draft 未变更')
    }
    if (status.readyForPreparation) return
    await delay(2_000)
  }
  if (!disposed) throw new Error('安全扫描等待超时，请稍后重试')
}

async function buildPreview(): Promise<void> {
  const profile = selectedProfile.value
  if (!profile || !canPreview.value) return
  busy.value = true
  error.value = ''
  preview.value = null
  confirmedConversion.value = false
  confirmedMapping.value = false
  try {
    const status = await designCadParseApi.getPreparationStatus(
      props.versionId,
      props.sourceId,
    )
    if (!status.readyForPreparation) throw new Error('请等待安全扫描完成')
    const result = await designCadParseApi.previewPreparation(
      props.versionId,
      props.sourceId,
      {
        floorLogicalId: props.floorLogicalId,
        confirmedUnit: form.confirmedUnit,
        sourceOriginInSourceUnits: { x: form.sourceOriginX, y: form.sourceOriginY },
        floorOriginMillimeters: {
          x: form.floorOriginX,
          y: form.floorOriginY,
          z: 0,
        },
        rotationZDegrees: form.rotationZDegrees,
        mappingProfileId: profile.profileId,
        mappingProfileVersion: profile.version,
        layerOverrides: layerOverrides.value,
      },
    )
    preview.value = result
    layerOverrides.value = result.mappingPreview?.layerOverrides ?? layerOverrides.value
    previewDirty.value = false
    if (!preview.value.readyForParsing) {
      error.value = '预览仍有阻断项；请修正单位、坐标或映射后重新预览。'
    }
  } catch (cause) {
    error.value = message(cause, 'CAD 准备预览失败；当前 Draft 未变更')
  } finally {
    busy.value = false
  }
}

function layerDecision(layerId: string) {
  return preview.value?.mappingPreview?.decisions.find(
    (decision) => decision.sourceKind === 'Layer' && decision.sourceKey === layerId,
  )
}

function layerOverride(layerId: string) {
  return layerOverrides.value.find((candidate) => candidate.layerId === layerId)
}

function layerMode(layerId: string): string {
  const existing = layerOverride(layerId)
  if (!existing) return 'profile'
  return existing.ignore ? 'ignore' : existing.target ?? 'profile'
}

function setLayerMode(layerId: string, event: Event): void {
  const value = (event.target as HTMLSelectElement).value
  if (value === 'profile') {
    layerOverrides.value = layerOverrides.value.filter(
      (candidate) => candidate.layerId !== layerId,
    )
  } else if (value === 'ignore') {
    upsertLayerOverride({ layerId, ignore: true })
  } else {
    const target = value as SpaceCadSemanticTarget
    upsertLayerOverride({
      layerId,
      ignore: false,
      target,
      geometryRule: defaultGeometryRule(target),
      confidenceWeight: .95,
    })
  }
  markPreviewDirty()
}

function setOverrideGeometry(layerId: string, event: Event): void {
  const existing = layerOverride(layerId)
  if (!existing || existing.ignore) return
  upsertLayerOverride({
    ...existing,
    geometryRule: (event.target as HTMLSelectElement).value as SpaceCadGeometryRule,
  })
  markPreviewDirty()
}

function setOverrideConfidence(layerId: string, event: Event): void {
  const existing = layerOverride(layerId)
  if (!existing || existing.ignore) return
  const confidenceWeight = Number((event.target as HTMLInputElement).value)
  if (!Number.isFinite(confidenceWeight)) return
  upsertLayerOverride({ ...existing, confidenceWeight })
  markPreviewDirty()
}

function upsertLayerOverride(value: SpaceCadLayerMappingOverride): void {
  layerOverrides.value = [
    ...layerOverrides.value.filter((candidate) => candidate.layerId !== value.layerId),
    value,
  ].sort((left, right) => left.layerId.localeCompare(right.layerId))
}

function defaultGeometryRule(target: SpaceCadSemanticTarget): SpaceCadGeometryRule {
  if (target === 'Wall') return 'Centerline'
  if (['Dock', 'Zone', 'RestrictedArea'].includes(target)) return 'ClosedBoundary'
  return 'DirectGeometry'
}

function markPreviewDirty(): void {
  if (!preview.value) return
  previewDirty.value = true
  confirmedConversion.value = false
  confirmedMapping.value = false
}

function decisionLabel(layerId: string): string {
  const decision = layerDecision(layerId)
  if (!decision) return '未决'
  if (decision.status === 'Ignored') return '忽略'
  if (decision.status === 'Conflict') return '冲突'
  if (decision.status === 'Unmapped') return '未映射'
  const target = semanticTargets.find((candidate) => candidate.value === decision.target)
  return `${target?.label ?? decision.target ?? '已映射'} · ${decision.decisionSource === 'LayerOverride' ? '逐层覆盖' : 'Profile'}`
}

async function startParse(): Promise<void> {
  if (!canStart.value || !preview.value?.startRequest) return
  busy.value = true
  error.value = ''
  try {
    const started = await designCadParseApi.start(
      props.versionId,
      props.sourceId,
      preview.value.startRequest,
    )
    emit('started', started.jobId)
  } catch (cause) {
    error.value = message(cause, '解析启动失败；当前 Draft 未变更')
  } finally {
    busy.value = false
  }
}

function delay(milliseconds: number) {
  return new Promise((resolve) => window.setTimeout(resolve, milliseconds))
}

function message(cause: unknown, fallback: string): string {
  if (cause instanceof Error && cause.message) return cause.message
  return fallback
}

function handleDialogKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') {
    event.preventDefault()
    emit('close')
    return
  }
  if (event.key !== 'Tab' || !dialogElement.value) return
  const focusable = Array.from(
    dialogElement.value.querySelectorAll<HTMLElement>(
      'button:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])',
    ),
  ).filter((candidate) => candidate.offsetParent !== null)
  if (focusable.length === 0) return
  const first = focusable[0]!
  const last = focusable[focusable.length - 1]!
  if (event.shiftKey && document.activeElement === first) {
    event.preventDefault()
    last.focus()
  } else if (!event.shiftKey && document.activeElement === last) {
    event.preventDefault()
    first.focus()
  }
}
</script>

<template>
  <div class="cad-wizard-backdrop" role="presentation">
    <section
      ref="dialogElement"
      class="cad-wizard"
      role="dialog"
      aria-modal="true"
      aria-labelledby="cad-wizard-title"
      tabindex="-1"
      @keydown="handleDialogKeydown"
    >
      <header>
        <div>
          <p class="eyebrow">确定性 CAD 导入</p>
          <h2 id="cad-wizard-title">确认楼层、单位、坐标与映射</h2>
          <p>{{ scanState }}</p>
        </div>
        <button type="button" class="icon-button" aria-label="关闭 CAD 向导" @click="emit('close')">×</button>
      </header>

      <aside
        v-if="capability"
        class="provider-capability"
        :class="{ 'ga-ready': capability.cadGaReady, blocked: !capability.canPrepareCad }"
        aria-label="Site CAD Provider 能力"
      >
        <div>
          <strong>{{ capability.cadGaReady ? 'CAD GA 已就绪' : 'CAD GA 尚未就绪' }}</strong>
          <span>配置 Revision {{ capability.configurationRevision }}</span>
        </div>
        <div class="provider-slots">
          <span>
            主：{{ capability.primary?.displayName ?? '未配置' }}<template v-if="capability.primary">
              · v{{ capability.primary.providerVersion }}
            </template>
          </span>
          <span>
            备：{{ capability.backup?.displayName ?? '未配置' }}<template v-if="capability.backup">
              · v{{ capability.backup.providerVersion }}
            </template>
          </span>
        </div>
        <p v-if="capability.blockingCodes.length">
          门禁：{{ capability.blockingCodes.join(' · ') }}
        </p>
      </aside>

      <div class="wizard-body">
        <section class="step">
          <span class="step-number">1</span>
          <div>
            <h3>目标楼层</h3>
            <code>{{ floorLogicalId }}</code>
            <p>解析结果只绑定到当前楼层；确认变更集前不会写入 Draft。</p>
          </div>
        </section>

        <section class="step">
          <span class="step-number">2</span>
          <div class="fields">
            <h3>单位与坐标</h3>
            <label>来源单位
              <select v-model="form.confirmedUnit" aria-label="来源单位">
                <option value="" disabled>请选择，不自动猜测</option>
                <option value="Millimeter">毫米</option>
                <option value="Centimeter">厘米</option>
                <option value="Meter">米</option>
                <option value="Inch">英寸</option>
                <option value="Foot">英尺</option>
              </select>
            </label>
            <label>来源原点 X <input v-model.number="form.sourceOriginX" type="number" /></label>
            <label>来源原点 Y <input v-model.number="form.sourceOriginY" type="number" /></label>
            <label>楼层原点 X (mm) <input v-model.number="form.floorOriginX" type="number" /></label>
            <label>楼层原点 Y (mm) <input v-model.number="form.floorOriginY" type="number" /></label>
            <label>旋转角度
              <input v-model.number="form.rotationZDegrees" type="number" min="-360" max="360" step="0.1" />
            </label>
          </div>
        </section>

        <section class="step">
          <span class="step-number">3</span>
          <div class="fields">
            <h3>映射 Profile</h3>
            <label>语义映射
              <select v-model="form.mappingProfileKey" aria-label="映射 Profile">
                <option value="" disabled>请选择服务器已知 Profile</option>
                <option
                  v-for="profile in profiles"
                  :key="`${profile.profileId}:${profile.version}`"
                  :value="`${profile.profileId}:${profile.version}`"
                >{{ profile.name }} · {{ profile.scope === 'Tenant' ? '租户私有' : '系统公共' }} · v{{ profile.version }} · {{ profile.ruleCount }} 条规则</option>
              </select>
            </label>
            <button type="button" class="primary" :disabled="!canPreview || busy" @click="buildPreview">
              {{ busy ? '处理中…' : previewDirty ? '重新生成语义预览' : '生成语义预览' }}
            </button>
          </div>
        </section>

        <section v-if="preview" class="step preview-step">
          <span class="step-number">4</span>
          <div>
            <h3>预览与显式确认</h3>
            <div class="metrics">
              <span>图层 {{ preview.inventorySummary?.layerCount ?? 0 }}</span>
              <span>实体 {{ preview.inventorySummary?.entityCount ?? 0 }}</span>
              <span>支持 {{ preview.inventorySummary?.supportedEntityCount ?? 0 }}</span>
              <span>未支持 {{ preview.inventorySummary?.unsupportedEntityCount ?? 0 }}</span>
              <span>映射冲突 {{ preview.mappingPreview?.summary.conflictLayerCount ?? 0 }}</span>
              <span>低置信候选 {{ preview.semanticPreview?.summary.candidateCount ?? 0 }}</span>
              <span class="blocking">阻断 {{ (preview.mappingPreview?.summary.blockingCount ?? 0) + (preview.semanticPreview?.summary.blockingCount ?? 0) }}</span>
            </div>
            <p class="analysis">
              CAD 建议单位 {{ preview.coordinateAnalysis.suggestedUnit }}；
              范围{{ preview.coordinateAnalysis.isSuggestedExtentPlausible ? '合理' : '需要复核' }}。
            </p>
            <p v-if="previewDirty" class="dirty-notice" role="status">
              单位、坐标、Profile 或逐层映射已修改。必须重新生成预览后才能启动解析。
            </p>
            <section v-if="preview.inventory" class="inventory-review" aria-label="CAD 图层与块清单">
              <div class="inventory-heading">
                <div>
                  <h3>图层清单与逐层映射</h3>
                  <p>Profile 只提供初始决定；任何逐层覆盖都要重新预览并由服务端重新密封。</p>
                </div>
                <label>搜索图层
                  <input v-model="layerSearch" type="search" aria-label="搜索 CAD 图层" placeholder="名称、颜色或线型" />
                </label>
              </div>
              <div class="inventory-table" role="table" aria-label="CAD 图层清单">
                <div class="layer-head" role="row">
                  <span role="columnheader">图层</span>
                  <span role="columnheader">样式</span>
                  <span role="columnheader">对象</span>
                  <span role="columnheader">当前决定</span>
                  <span role="columnheader">逐层覆盖</span>
                </div>
                <div v-for="layer in filteredLayers" :key="layer.layerId" class="layer-row" role="row">
                  <span role="cell">
                    <strong>{{ layer.name }}</strong>
                    <small>{{ layer.layerId }} · {{ layer.isVisible ? '可见' : '隐藏' }}</small>
                  </span>
                  <span role="cell">
                    {{ layer.color ?? '无颜色' }}<br />{{ layer.lineType ?? '无线型' }}
                  </span>
                  <span role="cell">
                    {{ layer.entityCount }} 个<br />
                    <small>支持 {{ layer.supportedEntityCount }} · 未支持 {{ layer.unsupportedEntityCount }}</small>
                  </span>
                  <span role="cell" :class="{ blocking: ['Conflict', 'Unmapped'].includes(layerDecision(layer.layerId)?.status ?? '') }">
                    {{ decisionLabel(layer.layerId) }}
                  </span>
                  <span class="override-controls" role="cell">
                    <select
                      :value="layerMode(layer.layerId)"
                      :aria-label="`图层 ${layer.name} 覆盖方式`"
                      @change="setLayerMode(layer.layerId, $event)"
                    >
                      <option value="profile">使用 Profile</option>
                      <option value="ignore">忽略该图层</option>
                      <option v-for="target in semanticTargets" :key="target.value" :value="target.value">
                        映射为{{ target.label }}
                      </option>
                    </select>
                    <template v-if="layerOverride(layer.layerId) && !layerOverride(layer.layerId)?.ignore">
                      <select
                        :value="layerOverride(layer.layerId)?.geometryRule"
                        :aria-label="`图层 ${layer.name} 几何规则`"
                        @change="setOverrideGeometry(layer.layerId, $event)"
                      >
                        <option v-for="rule in geometryRules" :key="rule.value" :value="rule.value">{{ rule.label }}</option>
                      </select>
                      <label>
                        置信度
                        <input
                          :value="layerOverride(layer.layerId)?.confidenceWeight"
                          type="number"
                          min="0"
                          max="1"
                          step="0.01"
                          :aria-label="`图层 ${layer.name} 置信度`"
                          @change="setOverrideConfidence(layer.layerId, $event)"
                        />
                      </label>
                    </template>
                  </span>
                </div>
                <p v-if="filteredLayers.length === 0" class="empty-inventory">没有匹配的图层。</p>
              </div>

              <details class="block-review">
                <summary>块清单（{{ preview.inventory.blocks.length }}）</summary>
                <label>搜索块
                  <input v-model="blockSearch" type="search" aria-label="搜索 CAD 块" placeholder="块名或 ID" />
                </label>
                <div class="block-list" aria-label="CAD 块清单">
                  <div v-for="block in filteredBlocks" :key="block.blockId" class="block-row">
                    <strong>{{ block.name }}</strong>
                    <span>{{ block.isExternalReference ? '外部引用' : '本地块' }}</span>
                    <span>定义 {{ block.definitionEntityCount }} · 引用 {{ block.referenceCount }}</span>
                    <span>属性引用 {{ block.attributedReferenceCount }}</span>
                  </div>
                  <p v-if="filteredBlocks.length === 0" class="empty-inventory">没有匹配的块。</p>
                </div>
              </details>
            </section>
            <div v-if="preview.semanticPreview?.items.length" class="semantic-list" aria-label="语义预览对象">
              <div class="semantic-list-head"><span>来源</span><span>目标</span><span>置信度</span><span>处置</span></div>
              <div
                v-for="item in preview.semanticPreview.items.slice(0, 20)"
                :key="item.previewObjectId"
                class="semantic-row"
              >
                <span>{{ item.source.layerId }} · {{ item.source.sourceRef }}</span>
                <span>{{ item.target }}</span>
                <span>{{ Math.round(item.confidence * 100) }}%</span>
                <span>{{ item.disposition }}</span>
              </div>
            </div>
            <label class="confirmation">
              <input v-model="confirmedConversion" type="checkbox" />
              我已确认单位、原点、旋转和楼层转换。
            </label>
            <label class="confirmation">
              <input v-model="confirmedMapping" type="checkbox" />
              我已检查映射与语义预览；低置信和未识别对象将在审核工作区继续处理。
            </label>
          </div>
        </section>

        <p v-if="error" class="error" role="alert">{{ error }}</p>
      </div>

      <footer>
        <span>准备结果绑定当前 Draft Revision，有效期由服务端控制。</span>
        <div>
          <button type="button" @click="emit('close')">取消</button>
          <button type="button" class="primary" :disabled="!canStart" @click="startParse">确认并启动解析</button>
        </div>
      </footer>
    </section>
  </div>
</template>

<style scoped>
.cad-wizard-backdrop { position:fixed; inset:0; z-index:1200; display:grid; place-items:center; padding:24px; background:rgba(2,8,18,.78); }
.cad-wizard { width:min(1180px,100%); max-height:calc(100vh - 48px); overflow:auto; border:1px solid var(--space-studio-border,#2a3950); border-radius:12px; color:var(--space-studio-text,#f4f7fb); background:#111a2b; box-shadow:0 28px 90px rgba(0,0,0,.55); }
header,footer { display:flex; align-items:center; justify-content:space-between; gap:24px; padding:18px 22px; border-bottom:1px solid #2a3950; }
footer { border-top:1px solid #2a3950; border-bottom:0; color:#aebbd0; font-size:14px; }
footer div { display:flex; gap:10px; }
h2,h3,p { margin:0; }
h2 { margin:3px 0 5px; font-size:22px; }
h3 { margin-bottom:10px; font-size:17px; }
.eyebrow { color:#18c2c9; font-size:13px; font-weight:800; letter-spacing:.08em; text-transform:uppercase; }
.icon-button { width:44px; height:44px; border:0; font-size:28px; background:transparent; }
.wizard-body { display:grid; gap:1px; background:#2a3950; }
.provider-capability { display:grid; gap:8px; padding:14px 22px; border-top:1px solid #2a3950; border-bottom:1px solid #2a3950; color:#ffd27a; background:#2a2114; }
.provider-capability.ga-ready { color:#9cf0c3; background:#10281f; }
.provider-capability.blocked { color:#ff9ba4; background:#321922; }
.provider-capability > div { display:flex; justify-content:space-between; gap:16px; }
.provider-capability span,.provider-capability p { font-size:14px; }
.provider-slots { flex-wrap:wrap; }
.step { display:grid; grid-template-columns:44px 1fr; gap:14px; padding:18px 22px; background:#111a2b; }
.step-number { display:grid; place-items:center; width:36px; height:36px; border:1px solid #18c2c9; border-radius:50%; color:#18c2c9; font-weight:800; }
.fields { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:12px; }
.fields h3,.fields .primary { grid-column:1/-1; }
label { display:grid; gap:5px; color:#c6d2e3; font-size:14px; }
input,select,button { box-sizing:border-box; min-height:44px; border:1px solid #3b4d67; border-radius:6px; color:#f4f7fb; background:#172236; padding:8px 10px; font:inherit; }
button { cursor:pointer; }
button:focus-visible,input:focus-visible,select:focus-visible { outline:3px solid #8cebf0; outline-offset:2px; }
button:disabled { cursor:not-allowed; opacity:.45; }
.primary { border-color:#18c2c9; color:#041014; background:#18c2c9; font-weight:800; }
.metrics { display:grid; grid-template-columns:repeat(3,minmax(0,1fr)); gap:8px; margin-bottom:14px; }
.metrics span { padding:10px; border:1px solid #2a3950; border-radius:6px; background:#0d1626; }
.analysis { margin:0 0 12px; color:#c6d2e3; }
.dirty-notice { margin:0 0 14px; padding:12px; border:1px solid #a97921; border-radius:6px; color:#ffd27a; background:#2a2114; }
.inventory-review { display:grid; gap:14px; margin:0 0 16px; }
.inventory-heading { display:flex; align-items:end; justify-content:space-between; gap:16px; }
.inventory-heading p { color:#aebbd0; font-size:14px; }
.inventory-heading label { min-width:260px; }
.inventory-table { max-height:360px; overflow:auto; border:1px solid #2a3950; border-radius:6px; }
.layer-head,.layer-row { display:grid; grid-template-columns:minmax(150px,1.2fr) minmax(110px,.8fr) minmax(120px,.8fr) minmax(130px,1fr) minmax(220px,1.5fr); gap:10px; align-items:start; padding:10px 12px; }
.layer-head { position:sticky; top:0; z-index:1; color:#8cebf0; background:#0d1626; font-size:13px; font-weight:800; }
.layer-row { min-height:44px; font-size:14px; }
.layer-row + .layer-row { border-top:1px solid #2a3950; }
.layer-row small { color:#aebbd0; }
.override-controls { display:grid; gap:8px; }
.override-controls label { grid-template-columns:auto 1fr; align-items:center; }
.block-review { border:1px solid #2a3950; border-radius:6px; padding:10px 12px; }
.block-review summary { min-height:44px; cursor:pointer; color:#8cebf0; font-weight:800; }
.block-review > label { max-width:360px; margin-bottom:10px; }
.block-list { max-height:220px; overflow:auto; }
.block-row { display:grid; grid-template-columns:repeat(4,minmax(0,1fr)); gap:10px; min-height:44px; align-items:center; padding:8px 0; }
.block-row + .block-row { border-top:1px solid #2a3950; }
.empty-inventory { padding:14px; color:#aebbd0; }
.semantic-list { max-height:220px; overflow:auto; margin-bottom:14px; border:1px solid #2a3950; border-radius:6px; }
.semantic-list-head,.semantic-row { display:grid; grid-template-columns:minmax(220px,2fr) repeat(3,minmax(90px,1fr)); gap:8px; padding:9px 11px; }
.semantic-list-head { position:sticky; top:0; color:#8cebf0; background:#0d1626; font-size:13px; font-weight:800; }
.semantic-row + .semantic-row { border-top:1px solid #2a3950; }
.blocking,.error { color:#ff8590; }
.confirmation { display:flex; align-items:flex-start; gap:10px; margin:10px 0; font-size:16px; }
.confirmation input { width:44px; height:44px; flex:0 0 44px; margin:0; }
.error { padding:14px 22px; background:#321922; }
code { color:#8cebf0; }
@media (max-width:900px) {
  .fields,.metrics { grid-template-columns:1fr; }
  .inventory-heading { align-items:stretch; flex-direction:column; }
  .inventory-heading label { min-width:0; }
  .layer-head,.layer-row { min-width:880px; }
  .block-row { grid-template-columns:1fr 1fr; }
}
</style>
