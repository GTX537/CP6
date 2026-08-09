<template>
  <section class="diagnostic-panel" aria-label="operations diagnostics">
    <header class="diagnostic-header">
      <div>
        <strong>{{ t('运营诊断') }}</strong>
        <span v-if="result">{{ result.warehouseCode || '—' }} · {{ result.definitionVersion }}</span>
      </div>
      <button class="close" :aria-label="t('关闭')" @click="$emit('close')">×</button>
    </header>

    <div class="diagnostic-controls">
      <select v-model.number="hours" :disabled="loading" aria-label="diagnostic window">
        <option :value="1">{{ t('最近 1 小时') }}</option>
        <option :value="8">{{ t('最近 8 小时') }}</option>
        <option :value="24">{{ t('最近 24 小时') }}</option>
      </select>
      <button class="run" :disabled="loading" @click="$emit('run', hours)">
        {{ loading ? t('分析中') : t('分析') }}
      </button>
    </div>

    <p v-if="error" class="diagnostic-error">{{ error }}</p>
    <div v-if="!result" class="diagnostic-state">
      {{ loading ? t('正在计算运营诊断') : t('尚无诊断结果') }}
    </div>

    <template v-else>
      <p v-if="loading" class="refreshing">{{ t('正在更新，当前显示上次成功结果') }}</p>
      <div class="diagnostic-grid">
        <article>
          <span>{{ t('观测路径') }}</span>
          <strong>{{ formatNumber(result.path.observedDistanceMeters) }} m</strong>
          <small>
            {{ result.path.knownDistanceSegmentCount }} {{ t('已知段') }} ·
            {{ result.path.unknownDistanceSegmentCount }} {{ t('未知段') }}
          </small>
        </article>
        <article :class="{ alert: result.path.backtrackCount > 0 }">
          <span>{{ t('折返') }}</span>
          <strong>{{ result.path.backtrackCount }}</strong>
          <small>{{ formatNumber(result.path.backtrackDistanceMeters) }} m</small>
        </article>
        <article :class="{ alert: result.congestion.locationCount > 0 }">
          <span>{{ t('拥堵观测') }}</span>
          <strong>{{ result.congestion.locationCount }}</strong>
          <small>
            {{ t('峰值') }} {{ result.congestion.peakConcurrentPeople }} ·
            {{ formatDuration(result.congestion.concurrentSeconds) }}
          </small>
        </article>
        <article>
          <span>{{ t('停留') }}</span>
          <strong>{{ result.dwell.episodeCount }}</strong>
          <small>
            {{ result.dwell.locationCount }} {{ t('库位') }} ·
            {{ formatDuration(result.dwell.totalDwellSeconds) }}
          </small>
        </article>
        <article :class="`pressure-${result.capacity.locationOccupancyPressure.toLowerCase()}`">
          <span>{{ t('库位占用压力') }}</span>
          <strong>{{ formatPercent(result.capacity.locationOccupancyPercent) }}</strong>
          <small>
            {{ formatCount(result.capacity.occupiedLocationCount) }}/{{ result.capacity.locationCount }} ·
            {{ result.capacity.locationOccupancyPressure }}
          </small>
        </article>
        <article class="capacity-unavailable">
          <span>{{ t('真实容量利用率') }}</span>
          <strong>{{ formatPercent(result.capacity.capacityUtilizationPercent) }}</strong>
          <small>{{ result.capacity.capacityUtilizationReason }}</small>
        </article>
      </div>

      <section class="evidence-section">
        <div class="section-title">
          <strong>{{ t('人员证据') }}</strong>
          <span>{{ result.personnelSource.personCount }} {{ t('人') }}</span>
        </div>
        <p>
          {{ result.personnelSource.eligibleRealEventCount }} {{ t('有效真实事件') }} ·
          {{ result.personnelSource.sourceCount }} {{ t('来源') }}
        </p>
        <p class="excluded">
          {{ t('排除模拟事件') }} {{ result.personnelSource.excludedSimulatedEventCount }} ·
          {{ t('排除当前模型外事件') }}
          {{ result.personnelSource.excludedOutsidePublishedModelEventCount }}
        </p>
        <p>
          {{ t('人员最后观测') }}：{{ formatTime(result.personnelSource.lastObservedAtUtc) }}
        </p>
        <p>
          {{ t('库存最后观测') }}：{{ formatTime(result.capacity.source?.observedAtUtc ?? null) }}
        </p>
      </section>

      <section class="hotspot-section">
        <div class="section-title">
          <strong>{{ t('折返证据') }}</strong>
          <span>≥ {{ result.thresholds.backtrackAngleDegrees }}°</span>
        </div>
        <div v-if="result.path.backtracks.length" class="hotspot-list backtrack-list">
          <button
            v-for="item in result.path.backtracks.slice(0, 5)"
            :key="`backtrack-${item.occurredAtUtc}-${item.floorLogicalId}`"
            :disabled="!item.spaceLocationCode"
            @click="selectLocation(item.spaceLocationCode)"
          >
            <span>{{ item.spaceLocationCode || item.floorCode || item.floorLogicalId }}</span>
            <small>{{ item.turnAngleDegrees }}° · {{ formatNumber(item.returnSegmentMeters) }} m</small>
          </button>
        </div>
        <p v-else class="empty">—</p>
      </section>

      <section class="hotspot-section">
        <div class="section-title">
          <strong>{{ t('拥堵热点') }}</strong>
          <span>≥ {{ result.thresholds.congestionMinimumConcurrentPeople }} {{ t('人重叠观测') }}</span>
        </div>
        <div v-if="result.congestion.hotspots.length" class="hotspot-list">
          <button
            v-for="item in result.congestion.hotspots.slice(0, 5)"
            :key="`congestion-${item.locationLogicalId}`"
            :disabled="!item.spaceLocationCode"
            @click="selectLocation(item.spaceLocationCode)"
          >
            <span>{{ item.spaceLocationCode || item.locationLogicalId }}</span>
            <small>
              {{ t('峰值') }} {{ item.peakConcurrentPeople }} ·
              {{ formatDuration(item.concurrentSeconds) }}
            </small>
          </button>
        </div>
        <p v-else class="empty">{{ t('窗口内无重叠库位观测') }}</p>
      </section>

      <section class="hotspot-section">
        <div class="section-title">
          <strong>{{ t('停留热点') }}</strong>
          <span>≥ {{ formatDuration(result.thresholds.dwellThresholdSeconds) }}</span>
        </div>
        <div v-if="result.dwell.hotspots.length" class="hotspot-list">
          <button
            v-for="item in result.dwell.hotspots.slice(0, 5)"
            :key="`dwell-${item.locationLogicalId}`"
            :disabled="!item.spaceLocationCode"
            @click="selectLocation(item.spaceLocationCode)"
          >
            <span>{{ item.spaceLocationCode || item.locationLogicalId }}</span>
            <small>
              {{ item.episodeCount }} {{ t('次') }} ·
              {{ formatDuration(item.totalDwellSeconds) }}
            </small>
          </button>
        </div>
        <p v-else class="empty">{{ t('窗口内无达到阈值的停留') }}</p>
      </section>

      <section v-if="result.capacity.floors.length" class="hotspot-section">
        <div class="section-title">
          <strong>{{ t('分层占用') }}</strong>
          <span>{{ result.capacity.occupancyBasis }}</span>
        </div>
        <div class="hotspot-list floor-occupancy-list">
          <button
            v-for="floor in result.capacity.floors"
            :key="floor.floorLogicalId"
            @click="$emit('switch-floor', floor.floorLogicalId)"
          >
            <span>{{ floor.floorCode }} · {{ floor.floorName }}</span>
            <small>
              {{ formatPercent(floor.locationOccupancyPercent) }} ·
              {{ floor.locationOccupancyPressure }}
            </small>
          </button>
        </div>
      </section>

      <footer>
        <span>{{ formatTime(result.calculatedAtUtc) }}</span>
        <span>{{ t('库位占用不等于体积、重量或托盘容量') }}</span>
      </footer>
    </template>
  </section>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { SpaceOperationsDiagnosticResponse } from '@/types/space/runtime'

defineProps<{
  result: SpaceOperationsDiagnosticResponse | null
  loading: boolean
  error: string
}>()

const emit = defineEmits<{
  (event: 'run', hours: number): void
  (event: 'select-location', locationCode: string): void
  (event: 'switch-floor', floorId: string): void
  (event: 'close'): void
}>()

const { t } = useI18n()
const hours = ref(8)

function selectLocation(value: string | null): void {
  if (value) emit('select-location', value)
}

function formatNumber(value: number): string {
  return value.toLocaleString(undefined, { maximumFractionDigits: 3 })
}

function formatCount(value: number | null): string {
  return value === null ? '—' : value.toLocaleString()
}

function formatPercent(value: number | null): string {
  return value === null ? '—' : `${value.toFixed(1)}%`
}

function formatDuration(seconds: number): string {
  if (seconds < 60) return `${seconds}s`
  if (seconds < 3600) return `${formatNumber(seconds / 60)}m`
  return `${formatNumber(seconds / 3600)}h`
}

function formatTime(value: string | null): string {
  if (!value) return '—'
  const parsed = new Date(value)
  return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString()
}
</script>

<style scoped>
.diagnostic-panel {
  position: absolute;
  top: 62px;
  right: 16px;
  z-index: 20;
  width: min(480px, calc(100% - 32px));
  max-height: calc(100% - 78px);
  overflow: auto;
  padding: 14px;
  border: 1px solid rgba(179, 157, 219, .5);
  border-radius: 8px;
  background: rgba(8, 13, 26, .97);
  box-shadow: 0 12px 36px rgba(0, 0, 0, .42);
  color: #e0e7ee;
  font-size: 12px;
}
.diagnostic-header,
.diagnostic-controls,
.section-title,
.hotspot-list button,
footer { display: flex; align-items: center; justify-content: space-between; gap: 8px; }
.diagnostic-header strong { font-size: 15px; }
.diagnostic-header span { display: block; margin-top: 2px; color: #90a4ae; font-size: 10px; }
.close { border: 0; background: transparent; color: #90a4ae; font-size: 20px; cursor: pointer; }
.diagnostic-controls { margin: 12px 0 8px; justify-content: flex-start; }
.diagnostic-controls select,
.diagnostic-controls button {
  padding: 4px 8px;
  border: 1px solid rgba(179, 157, 219, .42);
  border-radius: 4px;
  background: rgba(126, 87, 194, .1);
  color: #d1c4e9;
}
.diagnostic-controls button { cursor: pointer; }
.diagnostic-controls button:disabled { cursor: wait; opacity: .55; }
.diagnostic-error,
.refreshing { margin: 7px 0; padding: 6px 8px; border-radius: 4px; }
.diagnostic-error { background: rgba(198, 40, 40, .14); color: #ff8a80; }
.refreshing { background: rgba(79, 195, 247, .08); color: #81d4fa; }
.diagnostic-state { padding: 24px 4px; color: #90a4ae; text-align: center; }
.diagnostic-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 7px; }
.diagnostic-grid article { min-width: 0; padding: 9px; border-radius: 5px; background: rgba(255, 255, 255, .035); }
.diagnostic-grid span,
.diagnostic-grid small { display: block; overflow-wrap: anywhere; color: #90a4ae; }
.diagnostic-grid strong { display: block; margin: 3px 0; color: #e3f2fd; font-size: 16px; }
.diagnostic-grid .alert strong,
.pressure-critical strong,
.capacity-unavailable strong { color: #ff8a80; }
.pressure-watch strong { color: #ffcc80; }
.pressure-normal strong { color: #80cbc4; }
.evidence-section,
.hotspot-section { margin-top: 9px; padding: 10px; border-radius: 5px; background: rgba(255, 255, 255, .035); }
.evidence-section p { margin: 5px 0 0; color: #90a4ae; }
.evidence-section .excluded { color: #ffb74d; }
.section-title span { color: #90a4ae; font-size: 10px; }
.hotspot-list { display: grid; gap: 3px; margin-top: 6px; }
.hotspot-list button {
  width: 100%;
  padding: 5px 7px;
  border: 0;
  border-radius: 3px;
  background: transparent;
  color: #cfd8dc;
  cursor: pointer;
  text-align: left;
}
.hotspot-list button:hover { background: rgba(126, 87, 194, .16); }
.hotspot-list button:disabled { cursor: default; opacity: .65; }
.hotspot-list small { color: #b39ddb; }
.empty { margin: 6px 0 0; color: #607d8b; }
footer { align-items: flex-start; flex-direction: column; margin-top: 10px; color: #78909c; font-size: 10px; }
</style>
