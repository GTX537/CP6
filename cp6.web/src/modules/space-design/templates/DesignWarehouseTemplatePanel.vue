<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type {
  ISpaceDraftWarehouseTemplatePreviewDto,
  ISpaceWarehouseTemplateDto,
  ISpaceWarehouseTemplateFloorPlanDto,
  ISpaceWarehouseTemplateInstantiationPreviewDto,
} from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

const props = defineProps<{
  templates: readonly ISpaceWarehouseTemplateDto[]
  preview: ISpaceWarehouseTemplateInstantiationPreviewDto | null
  currentFloorCode?: string
  readonly?: boolean
  loading?: boolean
  busy?: boolean
  retryPending?: boolean
  pendingFloorKey?: string
  error?: string
  draftTemplatePreview?: ISpaceDraftWarehouseTemplatePreviewDto | null
  templateCreating?: boolean
  templateCreateError?: string
}>()

const emit = defineEmits<{
  preview: [template: ISpaceWarehouseTemplateDto]
  apply: [payload: { templateId: string; templateFloorKey: string }]
  previewDraftTemplate: []
  createDraftTemplate: [payload: {
    templateCode: string
    name: string
    description?: string
  }]
}>()

const selectedTemplateId = ref('')
const selectedFloorKey = ref('')
const showTemplateBuilder = ref(false)
const templateCode = ref('')
const templateName = ref('')
const templateDescription = ref('')

const selectedTemplate = computed(() =>
  props.templates.find((item) => item.id === selectedTemplateId.value) ??
  props.templates[0],
)

const selectedPreview = computed(() =>
  props.preview?.templateId === selectedTemplate.value?.id ? props.preview : null,
)

watch(
  () => props.templates,
  (templates) => {
    if (!templates.some((item) => item.id === selectedTemplateId.value)) {
      selectedTemplateId.value = templates[0]?.id ?? ''
    }
  },
  { immediate: true },
)

watch(
  () => [selectedPreview.value, props.currentFloorCode] as const,
  ([preview]) => {
    if (!preview) {
      selectedFloorKey.value = ''
      return
    }
    const matched = preview.floors.find(
      (floor) => floor.floorCode === props.currentFloorCode,
    )
    selectedFloorKey.value = matched?.key ?? preview.floors[0]?.key ?? ''
  },
  { immediate: true },
)

function floorCounts(floor: ISpaceWarehouseTemplateFloorPlanDto) {
  const zones = selectedPreview.value?.zones.filter((item) => item.floorKey === floor.key) ?? []
  const aisles = selectedPreview.value?.aisles.filter((item) => item.floorKey === floor.key) ?? []
  const racks = selectedPreview.value?.racks.filter((item) => item.floorKey === floor.key) ?? []
  return {
    zones: zones.length,
    aisles: aisles.length,
    racks: racks.length,
    locations: racks.reduce(
      (total, rack) => total + rack.columns * rack.levels * rack.depths,
      0,
    ),
  }
}

function requestPreview(): void {
  if (selectedTemplate.value) emit('preview', selectedTemplate.value)
}

function applyFloor(): void {
  const template = selectedTemplate.value
  if (!template || !selectedFloorKey.value) return
  emit('apply', {
    templateId: template.id,
    templateFloorKey: selectedFloorKey.value,
  })
}

function createDraftTemplate(): void {
  const code = templateCode.value.trim()
  const name = templateName.value.trim()
  if (!code || !name || !props.draftTemplatePreview) return
  emit('createDraftTemplate', {
    templateCode: code,
    name,
    description: templateDescription.value.trim() || undefined,
  })
}
</script>

<template>
  <section class="template-panel" aria-labelledby="warehouse-template-title">
    <div class="panel-heading">
      <div>
        <span class="eyebrow">VERSIONED TEMPLATE</span>
        <h3 id="warehouse-template-title">整仓模板</h3>
      </div>
      <span class="scope">按楼层原子写入</span>
    </div>

    <p class="description">
      先生成密封预览，再把选定模板楼层写入当前 Draft 楼层。确认前零写入。
    </p>

    <label>
      模板
      <select
        v-model="selectedTemplateId"
        data-testid="warehouse-template-select"
        :disabled="loading || busy || retryPending || templates.length === 0"
      >
        <option v-for="item in templates" :key="item.id" :value="item.id">
          {{ item.scope === 'Tenant' ? '租户私有' : '系统' }} ·
          {{ item.name }} · v{{ item.latestVersion.versionNo }}
        </option>
      </select>
    </label>

    <button
      class="secondary"
      type="button"
      data-testid="warehouse-template-preview"
      :disabled="loading || busy || retryPending || !selectedTemplate"
      @click="requestPreview"
    >{{ loading ? '加载中…' : '生成密封预览' }}</button>

    <p v-if="error" class="error" role="alert">{{ error }}</p>
    <p v-else-if="templates.length === 0 && !loading" class="empty">
      当前没有可用模板；手工构件仍可继续。
    </p>

    <template v-if="selectedPreview">
      <div class="seal" role="status">
        <strong>预览已密封</strong>
        <code :title="selectedPreview.proposalHash">{{ selectedPreview.proposalHash }}</code>
      </div>

      <fieldset>
        <legend>选择写入当前楼层的模板楼层</legend>
        <label
          v-for="item in selectedPreview.floors"
          :key="item.key"
          class="floor-option"
          :class="{ matched: item.floorCode === currentFloorCode }"
        >
          <input
            v-model="selectedFloorKey"
            type="radio"
            :value="item.key"
            :disabled="retryPending && item.key !== pendingFloorKey"
          >
          <span>
            <strong>{{ item.floorCode }} · {{ item.name }}</strong>
            <small>
              {{ floorCounts(item).zones }} 区 · {{ floorCounts(item).aisles }} 巷道 ·
              {{ floorCounts(item).racks }} 货架 · {{ floorCounts(item).locations }} 库位
            </small>
          </span>
          <em v-if="item.floorCode === currentFloorCode">编码匹配</em>
        </label>
      </fieldset>

      <button
        class="apply"
        type="button"
        data-testid="warehouse-template-apply"
        :disabled="readonly || busy || !selectedFloorKey"
        @click="applyFloor"
      >{{ busy ? '正在写入…' : retryPending ? '按原幂等请求安全重试' : '确认写入当前 Draft 楼层' }}</button>
    </template>

    <div class="builder">
      <button
        class="secondary"
        type="button"
        data-testid="open-draft-template-builder"
        :disabled="readonly || busy || templateCreating"
        @click="showTemplateBuilder = !showTemplateBuilder"
      >{{ showTemplateBuilder ? '收起模板制作' : '将当前 Draft 制作为租户模板' }}</button>

      <template v-if="showTemplateBuilder">
        <p class="description">
          先检查整个 Draft 是否为可复用的矩形楼层、区域、巷道和规则货架；检查阶段零写入。
        </p>
        <button
          class="secondary"
          type="button"
          data-testid="preview-draft-template"
          :disabled="readonly || busy || templateCreating"
          @click="emit('previewDraftTemplate')"
        >检查并密封当前 Draft</button>
        <p v-if="templateCreateError" class="error" role="alert">
          {{ templateCreateError }}
        </p>
        <form
          v-if="draftTemplatePreview"
          class="builder-form"
          @submit.prevent="createDraftTemplate"
        >
          <div class="seal" role="status">
            <strong>Draft 模板预览已密封，尚未创建</strong>
            <span>
              {{ draftTemplatePreview.counts?.floors }} 层 ·
              {{ draftTemplatePreview.counts?.racks }} 货架 ·
              {{ draftTemplatePreview.counts?.locations }} 库位
            </span>
            <code :title="draftTemplatePreview.proposalHash">
              {{ draftTemplatePreview.proposalHash }}
            </code>
          </div>
          <label>
            模板编码
            <input
              v-model="templateCode"
              data-testid="draft-template-code"
              maxlength="100"
              autocomplete="off"
              placeholder="例如 EAST-WH-01"
            >
          </label>
          <label>
            模板名称
            <input
              v-model="templateName"
              data-testid="draft-template-name"
              maxlength="200"
              autocomplete="off"
              placeholder="例如 华东标准仓"
            >
          </label>
          <label>
            说明（可选）
            <textarea
              v-model="templateDescription"
              data-testid="draft-template-description"
              maxlength="1000"
              rows="3"
            />
          </label>
          <button
            class="apply"
            type="submit"
            data-testid="create-draft-template"
            :disabled="templateCreating || !templateCode.trim() || !templateName.trim()"
          >{{ templateCreating ? '创建中…' : '确认创建租户模板' }}</button>
        </form>
      </template>
    </div>
  </section>
</template>

<style scoped>
.template-panel {
  display: grid;
  gap: 14px;
  border-bottom: 1px solid var(--space-studio-line, #24424c);
  padding: 4px 0 20px;
  color: var(--space-studio-text, #f1f7f8);
}
.panel-heading { display: flex; align-items: start; justify-content: space-between; gap: 12px; }
.panel-heading h3 { margin: 4px 0 0; font-size: 18px; }
.eyebrow,
.scope { color: var(--space-studio-accent, #26d7d3); font-size: 12px; font-weight: 800; }
.description,
.empty { margin: 0; color: var(--space-studio-muted, #a7bdc3); font-size: 14px; line-height: 1.55; }
label { display: grid; gap: 7px; color: var(--space-studio-muted, #a7bdc3); font-size: 13px; font-weight: 700; }
select,
input,
textarea,
button { min-height: 44px; box-sizing: border-box; font: inherit; }
select,
input,
textarea { border: 1px solid #365762; background: #071920; color: inherit; padding: 0 10px; }
textarea { padding-block: 10px; resize: vertical; }
button { cursor: pointer; font-weight: 800; }
button:disabled { cursor: not-allowed; opacity: .45; }
button:focus-visible,
select:focus-visible,
input:focus-visible,
textarea:focus-visible { outline: 3px solid #7df7f3; outline-offset: 2px; }
.secondary { border: 1px solid #3d6670; background: transparent; color: inherit; }
.seal { display: grid; gap: 6px; border-left: 3px solid var(--space-studio-accent, #26d7d3); background: rgb(38 215 211 / 8%); padding: 12px; }
.seal code { overflow: hidden; color: var(--space-studio-accent, #26d7d3); font-size: 11px; text-overflow: ellipsis; white-space: nowrap; }
fieldset { display: grid; gap: 8px; margin: 0; border: 0; padding: 0; }
legend { margin-bottom: 8px; font-size: 13px; font-weight: 800; }
.floor-option { grid-template-columns: auto 1fr auto; align-items: center; border: 1px solid #31535d; padding: 10px; }
.floor-option.matched { border-color: #3d827f; }
.floor-option input { width: 18px; height: 18px; }
.floor-option span { display: grid; gap: 4px; }
.floor-option strong { color: var(--space-studio-text, #f1f7f8); font-size: 14px; }
.floor-option small { font-size: 12px; font-weight: 500; }
.floor-option em { color: var(--space-studio-accent, #26d7d3); font-size: 11px; font-style: normal; }
.apply { border: 0; background: var(--space-studio-accent, #26d7d3); color: #032c31; padding: 0 12px; }
.error { margin: 0; color: #ffd0d0; font-size: 14px; }
.builder { display: grid; gap: 12px; border-top: 1px solid var(--space-studio-line, #24424c); padding-top: 16px; }
.builder-form { display: grid; gap: 12px; }
</style>
