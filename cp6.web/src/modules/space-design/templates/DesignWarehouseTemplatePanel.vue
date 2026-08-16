<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type {
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
}>()

const emit = defineEmits<{
  preview: [template: ISpaceWarehouseTemplateDto]
  apply: [payload: { templateId: string; templateFloorKey: string }]
}>()

const selectedTemplateId = ref('')
const selectedFloorKey = ref('')

const selectedTemplate = computed(() =>
  props.templates.find((item) => item.id === selectedTemplateId.value) ??
  props.templates[0],
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
  () => [props.preview, props.currentFloorCode] as const,
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
  const zones = props.preview?.zones.filter((item) => item.floorKey === floor.key) ?? []
  const aisles = props.preview?.aisles.filter((item) => item.floorKey === floor.key) ?? []
  const racks = props.preview?.racks.filter((item) => item.floorKey === floor.key) ?? []
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
</script>

<template>
  <section class="template-panel" aria-labelledby="warehouse-template-title">
    <div class="panel-heading">
      <div>
        <span class="eyebrow">SYSTEM TEMPLATE</span>
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

    <template v-if="preview">
      <div class="seal" role="status">
        <strong>预览已密封</strong>
        <code :title="preview.proposalHash">{{ preview.proposalHash }}</code>
      </div>

      <fieldset>
        <legend>选择写入当前楼层的模板楼层</legend>
        <label
          v-for="item in preview.floors"
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
button { min-height: 44px; box-sizing: border-box; font: inherit; }
select { border: 1px solid #365762; background: #071920; color: inherit; padding: 0 10px; }
button { cursor: pointer; font-weight: 800; }
button:disabled { cursor: not-allowed; opacity: .45; }
button:focus-visible,
select:focus-visible,
input:focus-visible { outline: 3px solid #7df7f3; outline-offset: 2px; }
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
</style>
