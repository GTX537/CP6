<template>
  <section class="putaway-panel" aria-label="putaway recommendations">
    <header class="putaway-header">
      <div>
        <strong>{{ t('上架推荐') }}</strong>
        <span v-if="result">{{ result.warehouseCode }} · {{ result.definitionVersion }}</span>
      </div>
      <button class="close" :aria-label="t('关闭')" @click="$emit('close')">×</button>
    </header>

    <p class="safety-note">{{ t('推荐不会预留库位、移动库存或创建任务') }}</p>
    <form class="putaway-form" @submit.prevent="submit">
      <label>
        <span>{{ t('物料') }}</span>
        <input v-model="materialNumber" required maxlength="200" :disabled="loading" />
      </label>
      <label>
        <span>{{ t('货主') }}</span>
        <input v-model="ownerId" maxlength="100" :disabled="loading" />
      </label>
      <label>
        <span>{{ t('批次') }}</span>
        <input v-model="lotNumber" maxlength="200" :disabled="loading" />
      </label>
      <label>
        <span>{{ t('入库数量') }}</span>
        <input v-model.number="inboundQuantity" type="number" min="0.000001" step="any" required :disabled="loading" />
      </label>
      <label>
        <span>{{ t('宽度要求（毫米）') }}</span>
        <input v-model.number="requiredWidth" type="number" min="1" step="1" :disabled="loading" />
      </label>
      <label>
        <span>{{ t('高度要求（毫米）') }}</span>
        <input v-model.number="requiredHeight" type="number" min="1" step="1" :disabled="loading" />
      </label>
      <label>
        <span>{{ t('深度要求（毫米）') }}</span>
        <input v-model.number="requiredDepth" type="number" min="1" step="1" :disabled="loading" />
      </label>
      <label>
        <span>{{ t('最大承载要求') }}</span>
        <input v-model.number="requiredMaxLoad" type="number" min="0.000001" step="any" :disabled="loading" />
      </label>
      <label>
        <span>{{ t('最大候选数') }}</span>
        <input v-model.number="maximumCandidates" type="number" min="1" max="50" step="1" required :disabled="loading" />
      </label>
      <label class="check">
        <input v-model="scopeCurrentFloor" type="checkbox" :disabled="loading || !currentFloorId" />
        <span>{{ t('仅当前楼层') }}</span>
      </label>
      <label class="check">
        <input v-model="allowConsolidation" type="checkbox" :disabled="loading" />
        <span>{{ t('允许精确库存合并') }}</span>
      </label>
      <button class="generate" type="submit" :disabled="loading || !canSubmit">
        {{ loading ? t('生成中') : t('生成推荐') }}
      </button>
    </form>

    <p v-if="error" class="putaway-error">{{ error }}</p>
    <p v-if="loading && result" class="refreshing">{{ t('正在更新，当前显示上次成功推荐') }}</p>
    <div v-if="!result" class="putaway-state">
      {{ loading ? t('生成中') : t('尚无推荐结果') }}
    </div>

    <template v-else>
      <section class="source-section">
        <div class="section-title">
          <strong>{{ t('来源时点') }}</strong>
          <span>{{ formatTime(result.generatedAtUtc) }}</span>
        </div>
        <p>
          {{ t('库存来源') }}：{{ result.sources.inventory.kind }} ·
          {{ formatTime(result.sources.inventory.observedAtUtc) }}
        </p>
        <p>
          {{ t('活动任务来源') }}：{{ result.sources.activeTasks.kind }} ·
          {{ formatTime(result.sources.activeTasks.observedAtUtc) }}
        </p>
      </section>

      <section class="candidate-section">
        <div class="section-title">
          <strong>{{ t('候选库位') }}</strong>
          <span>{{ result.returnedCandidateCount }}/{{ result.eligibleCandidateCount }}</span>
        </div>
        <p v-if="result.isTruncated" class="truncated">{{ t('候选结果已截断') }}</p>
        <div v-if="result.candidates.length" class="location-list">
          <button
            v-for="candidate in result.candidates"
            :key="candidate.locationLogicalId"
            @click="$emit('locate', candidate.spaceLocationCode)"
          >
            <span>#{{ candidate.rank }} · {{ candidate.spaceLocationCode }}</span>
            <small>
              {{ t(candidate.category) }} · {{ candidate.floorCode }}
              <template v-if="candidate.zoneCode">/{{ candidate.zoneCode }}</template>
              · {{ t('当前数量') }} {{ formatNumber(candidate.currentPhysicalQuantity) }}
              <template v-if="candidate.distanceToMatchingStockMeters !== null">
                · {{ t('几何距离') }} {{ formatNumber(candidate.distanceToMatchingStockMeters) }} m
              </template>
            </small>
            <code>{{ candidate.ruleHits.join(' · ') }}</code>
          </button>
        </div>
        <p v-else class="empty">{{ t('没有符合硬约束的候选库位') }}</p>
      </section>

      <section class="exclusion-section">
        <div class="section-title">
          <strong>{{ t('排除统计') }}</strong>
          <span>{{ result.examinedLocationCount - result.eligibleCandidateCount }}</span>
        </div>
        <div class="exclusion-grid">
          <span v-for="item in exclusionEntries" :key="item.reason">
            {{ t(item.reason) }} <strong>{{ item.count }}</strong>
          </span>
        </div>
        <div v-if="result.exclusionSamples.length" class="sample-block">
          <strong>{{ t('排除样例') }}</strong>
          <span v-if="result.exclusionSamplesTruncated" class="truncated">
            {{ t('排除样例已截断') }}
          </span>
          <div class="location-list">
            <button
              v-for="sample in result.exclusionSamples.slice(0, 10)"
              :key="sample.locationLogicalId"
              :disabled="!sample.spaceLocationCode"
              @click="locateSample(sample.spaceLocationCode)"
            >
              <span>{{ sample.spaceLocationCode || sample.locationLogicalId }}</span>
              <small>{{ t(sample.reason) }} · {{ sample.floorCode || sample.floorLogicalId }}</small>
            </button>
          </div>
        </div>
      </section>

      <details class="limitation-section">
        <summary>{{ t('限制说明') }} ({{ result.limitations.length }})</summary>
        <code v-for="item in result.limitations" :key="item">{{ item }}</code>
      </details>
    </template>
  </section>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type {
  GenerateSpacePutawayRecommendationRequest,
  SpacePutawayRecommendation,
} from '@/types/space/runtime'

const props = defineProps<{
  currentFloorId: string
  result: SpacePutawayRecommendation | null
  loading: boolean
  error: string
}>()

const emit = defineEmits<{
  (event: 'generate', request: GenerateSpacePutawayRecommendationRequest): void
  (event: 'locate', locationCode: string): void
  (event: 'close'): void
}>()

const { t } = useI18n()
const materialNumber = ref('')
const ownerId = ref('')
const lotNumber = ref('')
const inboundQuantity = ref<number | null>(1)
const requiredWidth = ref<number | null>(null)
const requiredHeight = ref<number | null>(null)
const requiredDepth = ref<number | null>(null)
const requiredMaxLoad = ref<number | null>(null)
const maximumCandidates = ref(10)
const scopeCurrentFloor = ref(true)
const allowConsolidation = ref(true)

const canSubmit = computed(() =>
  materialNumber.value.trim().length > 0 &&
  Number(inboundQuantity.value) > 0 &&
  maximumCandidates.value >= 1 &&
  maximumCandidates.value <= 50,
)

const exclusionEntries = computed(() => {
  const value = props.result?.exclusions
  if (!value) return []
  return [
    { reason: 'MISSING_SPATIAL_METADATA', count: value.missingSpatialMetadata },
    { reason: 'OUTSIDE_REQUESTED_SCOPE', count: value.outsideRequestedScope },
    { reason: 'ACTIVE_TASK_AT_OBSERVATION', count: value.activeTask },
    { reason: 'INVALID_INVENTORY_QUANTITY', count: value.invalidInventory },
    { reason: 'WMS_SPACE_LOCATION_CODE_MISMATCH', count: value.locationCodeMismatch },
    { reason: 'OCCUPIED_WITH_INCOMPATIBLE_STOCK', count: value.occupiedIncompatible },
    { reason: 'PUBLISHED_DIMENSION_TOO_SMALL', count: value.dimensionTooSmall },
    { reason: 'PUBLISHED_MAX_LOAD_UNAVAILABLE', count: value.loadUnverifiable },
    { reason: 'PUBLISHED_MAX_LOAD_INSUFFICIENT', count: value.loadInsufficient },
  ].filter(item => item.count > 0)
})

function submit(): void {
  if (!canSubmit.value) return
  emit('generate', {
    materialNumber: materialNumber.value.trim(),
    ownerId: optional(ownerId.value),
    lotNumber: optional(lotNumber.value),
    inboundQuantity: Number(inboundQuantity.value),
    floorLogicalId: scopeCurrentFloor.value && props.currentFloorId
      ? props.currentFloorId
      : null,
    requiredWidthMillimeters: positiveInteger(requiredWidth.value),
    requiredHeightMillimeters: positiveInteger(requiredHeight.value),
    requiredDepthMillimeters: positiveInteger(requiredDepth.value),
    requiredMaxLoad: positive(requiredMaxLoad.value),
    allowExactStockConsolidation: allowConsolidation.value,
    maximumCandidates: Math.trunc(maximumCandidates.value),
  })
}

function optional(value: string): string | null {
  return value.trim() || null
}

function positive(value: number | null): number | null {
  return value !== null && Number(value) > 0 ? Number(value) : null
}

function positiveInteger(value: number | null): number | null {
  const normalized = positive(value)
  return normalized === null ? null : Math.trunc(normalized)
}

function locateSample(value: string | null): void {
  if (value) emit('locate', value)
}

function formatNumber(value: number): string {
  return value.toLocaleString(undefined, { maximumFractionDigits: 3 })
}

function formatTime(value: string): string {
  const parsed = new Date(value)
  return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString()
}
</script>

<style scoped>
.putaway-panel {
  position: absolute;
  top: 62px;
  right: 16px;
  z-index: 21;
  width: min(520px, calc(100% - 32px));
  max-height: calc(100% - 78px);
  overflow: auto;
  padding: 14px;
  border: 1px solid rgba(77, 208, 225, .48);
  border-radius: 8px;
  background: rgba(8, 17, 25, .97);
  box-shadow: 0 12px 36px rgba(0, 0, 0, .42);
  color: #e0f2f1;
  font-size: 12px;
}
.putaway-header,
.section-title,
.location-list button { display: flex; align-items: center; justify-content: space-between; gap: 8px; }
.putaway-header strong { font-size: 15px; }
.putaway-header span { display: block; margin-top: 2px; color: #80cbc4; font-size: 10px; }
.close { border: 0; background: transparent; color: #90a4ae; font-size: 20px; cursor: pointer; }
.safety-note,
.putaway-error,
.refreshing,
.truncated { margin: 8px 0; padding: 6px 8px; border-radius: 4px; }
.safety-note { background: rgba(255, 193, 7, .1); color: #ffe082; }
.putaway-error { background: rgba(198, 40, 40, .14); color: #ff8a80; }
.refreshing { background: rgba(79, 195, 247, .08); color: #81d4fa; }
.truncated { color: #ffcc80; }
.putaway-form { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 7px; }
.putaway-form label { display: grid; gap: 3px; min-width: 0; color: #90a4ae; }
.putaway-form input {
  min-width: 0;
  padding: 5px 7px;
  border: 1px solid rgba(77, 208, 225, .3);
  border-radius: 4px;
  background: rgba(255, 255, 255, .035);
  color: #e0f2f1;
}
.putaway-form .check { display: flex; align-items: center; grid-template-columns: auto 1fr; }
.putaway-form .check input { min-width: auto; }
.generate {
  grid-column: 1 / -1;
  padding: 7px;
  border: 1px solid rgba(77, 208, 225, .5);
  border-radius: 4px;
  background: rgba(0, 188, 212, .16);
  color: #b2ebf2;
  cursor: pointer;
}
.generate:disabled { cursor: wait; opacity: .55; }
.putaway-state { padding: 24px 4px; color: #78909c; text-align: center; }
.source-section,
.candidate-section,
.exclusion-section,
.limitation-section { margin-top: 9px; padding: 10px; border-radius: 5px; background: rgba(255, 255, 255, .035); }
.source-section p { margin: 5px 0 0; color: #90a4ae; }
.section-title span { color: #80cbc4; font-size: 10px; }
.location-list { display: grid; gap: 3px; margin-top: 6px; }
.location-list button {
  width: 100%;
  align-items: flex-start;
  flex-direction: column;
  padding: 6px 7px;
  border: 0;
  border-radius: 3px;
  background: transparent;
  color: #cfd8dc;
  cursor: pointer;
  text-align: left;
}
.location-list button:hover { background: rgba(0, 188, 212, .12); }
.location-list button:disabled { cursor: default; opacity: .6; }
.location-list small { color: #80cbc4; }
.location-list code,
.limitation-section code { display: block; overflow-wrap: anywhere; color: #78909c; font-size: 9px; }
.exclusion-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 4px; margin-top: 6px; }
.exclusion-grid span { display: flex; justify-content: space-between; gap: 5px; color: #b0bec5; }
.sample-block { margin-top: 9px; }
.empty { color: #607d8b; }
.limitation-section summary { cursor: pointer; }
.limitation-section code { margin-top: 4px; }
</style>
