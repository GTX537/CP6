<template>
  <section class="warehouse-overview" aria-label="warehouse overview">
    <header class="overview-header">
      <div>
        <strong>{{ t('仓库运行快照') }}</strong>
        <span v-if="response" :class="response.isRuntimeComplete ? 'complete' : 'partial'">
          {{ response.isRuntimeComplete ? t('完整') : t('部分可用') }}
        </span>
      </div>
      <button class="close" :aria-label="t('关闭')" @click="$emit('close')">×</button>
    </header>

    <div class="overview-controls">
      <label>
        {{ t('ABC 窗口') }}
        <input v-model.number="windowDays" type="number" min="1" max="365" />
        {{ t('天') }}
      </label>
      <button :disabled="loading" @click="refresh">
        {{ loading ? t('刷新中') : t('刷新') }}
      </button>
    </div>

    <div v-if="!response" class="empty-state">
      {{ loading ? t('正在读取仓库快照') : t('尚未读取仓库快照') }}
    </div>

    <template v-else>
      <p class="snapshot-meta">
        {{ response.warehouseCode }} · {{ formatTime(response.capturedAtUtc) }}
      </p>

      <div class="kpi-grid">
        <article>
          <span>{{ t('楼层面积') }}</span>
          <strong>{{ formatNumber(response.model.totalFloorAreaSquareMeters) }} m²</strong>
          <small>
            {{ response.model.areaAvailableFloorCount }}/{{ response.model.floorCount }}
            {{ t('层有面积口径') }}
          </small>
        </article>
        <article>
          <span>{{ t('货架占地') }}</span>
          <strong>{{ formatNumber(response.model.rackFootprintSquareMeters) }} m²</strong>
          <small>{{ formatPercent(response.model.rackFootprintRatePercent) }}</small>
        </article>
        <article>
          <span>{{ t('库位占用率') }}</span>
          <strong>{{ formatPercent(response.inventory.occupiedLocationRatePercent) }}</strong>
          <small>
            {{ formatCount(response.inventory.occupiedLocationCount) }}/{{ response.model.activeLocationCount }}
            {{ t('个活动库位') }}
          </small>
        </article>
        <article>
          <span>{{ t('容量利用率') }}</span>
          <strong>{{ formatPercent(response.inventory.capacityUtilizationPercent) }}</strong>
          <small class="unavailable">{{ response.inventory.capacityUtilizationReason }}</small>
        </article>
        <article>
          <span>{{ t('库存范围') }}</span>
          <strong>{{ formatCount(response.inventory.inventoryLineCount) }} {{ t('行') }}</strong>
          <small>
            SKU {{ formatCount(response.inventory.distinctMaterialCount) }} ·
            {{ t('批次') }} {{ formatCount(response.inventory.distinctLotCount) }} ·
            {{ t('容器') }} {{ formatCount(response.inventory.distinctContainerCount) }}
          </small>
        </article>
        <article>
          <span>{{ t('活动任务') }}</span>
          <strong>{{ formatCount(response.tasks.activeTaskCount) }}</strong>
          <small>{{ formatCount(response.tasks.activeTaskStopCount) }} {{ t('个停靠点') }}</small>
        </article>
      </div>

      <div class="source-row">
        <span :class="`source-${response.inventory.source.kind.toLowerCase()}`">
          {{ t('库存') }} {{ dataSourceLabel(response.inventory.source) }} ·
          {{ response.inventory.source.dataSourceId }} ·
          {{ formatTime(response.inventory.source.observedAtUtc) }}
        </span>
        <span :class="`source-${response.tasks.source.kind.toLowerCase()}`">
          {{ t('任务') }} {{ dataSourceLabel(response.tasks.source) }} ·
          {{ response.tasks.source.dataSourceId }} ·
          {{ formatTime(response.tasks.source.observedAtUtc) }}
        </span>
        <span :class="`source-${response.abc.source.kind.toLowerCase()}`">
          ABC {{ dataSourceLabel(response.abc.source) }} ·
          {{ response.abc.source.dataSourceId }} ·
          {{ formatTime(response.abc.source.observedAtUtc) }}
        </span>
      </div>

      <section class="abc-section">
        <div class="section-title">
          <strong>ABC</strong>
          <label>
            <input
              type="checkbox"
              :checked="abcOverlayOn"
              :disabled="!response.abc.spatialMappingAvailable"
              @change="toggleAbc"
            />
            {{ t('空间叠加') }}
          </label>
        </div>
        <p>
          {{ response.abc.windowStartDate }} → {{ response.abc.windowEndDateExclusive }} ·
          {{ t('完整自然日，结束日不含') }}
        </p>
        <div class="abc-counts">
          <span class="rank-a">A {{ formatCount(response.abc.aCount) }}</span>
          <span class="rank-b">B {{ formatCount(response.abc.bCount) }}</span>
          <span class="rank-c">C {{ formatCount(response.abc.cCount) }}</span>
          <span class="rank-u">U {{ formatCount(response.abc.unclassifiedCount) }}</span>
        </div>
        <small>{{ t('按出库数量降序，以前序累计占比划分 80% / 95%') }}</small>
      </section>

      <section class="anomaly-section">
        <strong>{{ t('异常快照') }} · {{ anomalyCount }}</strong>
        <span>{{ t('活动告警') }} {{ response.anomalies.activeDeviceAlarmCount }}</span>
        <span>{{ t('严重告警') }} {{ response.anomalies.criticalDeviceAlarmCount }}</span>
        <span>{{ t('编码不一致') }} {{ formatCount(response.anomalies.codeMismatchLocationCount) }}</span>
        <span>{{ t('超分配行') }} {{ formatCount(response.anomalies.overAllocatedInventoryLineCount) }}</span>
        <span>{{ t('缺面积楼层') }} {{ response.anomalies.areaMissingFloorCount }}</span>
      </section>

      <div class="floor-list">
        <button
          v-for="floor in response.floors"
          :key="floor.floorLogicalId"
          :class="{ current: floor.floorLogicalId === currentFloorId }"
          @click="$emit('switch-floor', floor.floorLogicalId)"
        >
          <span>{{ floor.floorCode }} · {{ floor.floorName }}</span>
          <span>{{ formatPercent(floor.occupiedLocationRatePercent) }}</span>
        </button>
      </div>
    </template>
  </section>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { dataSourceLabel } from '@/types/space/dataSource'
import type { SpaceWarehouseOverviewResponse } from '@/types/space/runtime'

const props = defineProps<{
  loading: boolean
  response: SpaceWarehouseOverviewResponse | null
  abcOverlayOn: boolean
  currentFloorId: string
}>()

const emit = defineEmits<{
  (event: 'refresh', windowDays: number): void
  (event: 'toggle-abc', enabled: boolean): void
  (event: 'switch-floor', floorId: string): void
  (event: 'close'): void
}>()

const { t } = useI18n()
const windowDays = ref(props.response?.abc.windowDays ?? 90)

watch(
  () => props.response?.abc.windowDays,
  (value) => { if (value) windowDays.value = value },
)

const anomalyCount = computed(() => {
  if (!props.response) return 0
  const value = props.response.anomalies
  return value.activeDeviceAlarmCount
    + (value.codeMismatchLocationCount ?? 0)
    + (value.overAllocatedInventoryLineCount ?? 0)
    + value.areaMissingFloorCount
    + (value.unclassifiedAbcMaterialCount ?? 0)
})

function refresh(): void {
  const normalized = Math.min(365, Math.max(1, Math.trunc(Number(windowDays.value) || 90)))
  windowDays.value = normalized
  emit('refresh', normalized)
}

function toggleAbc(event: Event): void {
  emit('toggle-abc', (event.target as HTMLInputElement).checked)
}

function formatCount(value: number | null): string {
  return value === null ? '—' : value.toLocaleString()
}

function formatNumber(value: number | null): string {
  return value === null ? '—' : value.toLocaleString(undefined, { maximumFractionDigits: 2 })
}

function formatPercent(value: number | null): string {
  return value === null ? '—' : `${value.toFixed(1)}%`
}

function formatTime(value: string): string {
  const parsed = new Date(value)
  return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString()
}
</script>

<style scoped>
.warehouse-overview {
  position: absolute;
  top: 62px;
  right: 16px;
  width: min(480px, calc(100% - 32px));
  max-height: calc(100% - 78px);
  overflow: auto;
  z-index: 20;
  padding: 14px;
  color: #e0e7ee;
  background: rgba(8, 13, 26, .97);
  border: 1px solid rgba(79, 195, 247, .42);
  border-radius: 8px;
  box-shadow: 0 12px 36px rgba(0, 0, 0, .42);
  font-size: 12px;
}
.overview-header,
.overview-controls,
.section-title,
.source-row { display: flex; align-items: center; justify-content: space-between; gap: 8px; }
.overview-header strong { font-size: 15px; }
.overview-header span { margin-left: 8px; font-size: 11px; }
.complete, .source-real { color: #66bb6a; }
.partial, .source-simulated { color: #ffb74d; }
.source-unavailable, .unavailable { color: #ef5350; }
.close { border: 0; background: transparent; color: #90a4ae; font-size: 20px; cursor: pointer; }
.overview-controls { margin: 12px 0 8px; }
.overview-controls input { width: 58px; margin-left: 6px; }
.overview-controls button,
.floor-list button {
  color: #b3e5fc;
  background: rgba(79, 195, 247, .08);
  border: 1px solid rgba(79, 195, 247, .28);
  border-radius: 4px;
  cursor: pointer;
}
.overview-controls button:disabled { cursor: wait; opacity: .55; }
.empty-state { padding: 24px 4px; text-align: center; color: #90a4ae; }
.snapshot-meta { margin: 4px 0 10px; color: #90a4ae; }
.kpi-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 7px; }
.kpi-grid article { padding: 9px; background: rgba(255, 255, 255, .035); border-radius: 5px; }
.kpi-grid article > span, .kpi-grid small { display: block; color: #90a4ae; }
.kpi-grid strong { display: block; margin: 3px 0; color: #e3f2fd; font-size: 16px; }
.source-row { justify-content: flex-start; flex-wrap: wrap; margin: 10px 0; }
.source-row span { padding: 2px 6px; border: 1px solid currentColor; border-radius: 10px; }
.abc-section, .anomaly-section { margin-top: 9px; padding: 10px; background: rgba(255, 255, 255, .035); border-radius: 5px; }
.abc-section p { margin: 6px 0; color: #90a4ae; }
.abc-counts { display: grid; grid-template-columns: repeat(4, 1fr); gap: 5px; margin: 7px 0; }
.abc-counts span { padding: 5px; text-align: center; border-radius: 3px; font-weight: 700; color: #101820; }
.rank-a { background: #ef5350; }
.rank-b { background: #ffb74d; }
.rank-c { background: #42a5f5; }
.rank-u { background: #607d8b; }
.anomaly-section { display: flex; flex-wrap: wrap; gap: 6px 12px; }
.anomaly-section strong { width: 100%; color: #ffcc80; }
.floor-list { margin-top: 9px; display: grid; gap: 4px; }
.floor-list button { display: flex; justify-content: space-between; padding: 6px 8px; text-align: left; }
.floor-list button.current { border-color: #4fc3f7; background: rgba(79, 195, 247, .18); }
</style>
