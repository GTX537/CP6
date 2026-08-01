<!-- cp6.web/src/views/space/viewer/AdvancedPanel.vue -->
<template>
  <div class="advanced-panel">
    <div class="ap-sources">
      <span :class="`source-${taskSource.kind.toLowerCase()}`">TASK {{ dataSourceLabel(taskSource) }}</span>
      <span :class="`source-${workloadSource.kind.toLowerCase()}`">WORK {{ dataSourceLabel(workloadSource) }}</span>
      <span :class="`source-${deviceSource.kind.toLowerCase()}`">DEVICE {{ dataSourceLabel(deviceSource) }}</span>
    </div>
    <div class="ap-section">
      <div class="ap-title">{{ t('拣货路径') }}</div>
      <div class="ap-row">
        <input v-model="taskNo" class="ap-input" :placeholder="t('拣货单号')" />
        <button class="ap-btn" :disabled="pathLoading" @click="$emit('load-path', taskNo)">
          {{ pathLoading ? t('查询中') : t('加载') }}
        </button>
      </div>
      <div v-if="taskPath" class="ap-task-summary">
        <div v-if="!taskPath.source.isAvailable" class="ap-state-error">
          {{ t('任务数据源不可用，不能判定任务是否存在') }}
        </div>
        <div v-else-if="taskPath.stopCount === 0" class="ap-state-empty">
          {{ t('可用数据源中没有找到该任务') }}
        </div>
        <template v-else>
        <div class="ap-task-id">
          {{ taskPath.taskId }} · {{ taskPath.actualStops[0]?.taskType }} / {{ taskPath.actualStops[0]?.status }}
        </div>
        <div class="ap-info">
          {{ taskPath.stopCount }} {{ t('停靠点') }} ·
          {{ taskPath.floorCount }} {{ t('层') }} ·
          {{ taskPath.zoneCount }} {{ t('区') }} ·
          {{ t('工作量') }} {{ formatQuantity(taskPath.totalQuantity) }}
        </div>
        <div v-if="taskPath.locatedStopCount < taskPath.stopCount" class="ap-state-error">
          {{ taskPath.stopCount - taskPath.locatedStopCount }} {{ t('个停靠点缺少坐标，未生成不完整优化路径') }}
        </div>
        <div class="ap-badges">
          <span :class="taskPath.crossFloor ? 'ap-badge-warn' : 'ap-badge-ok'">
            {{ taskPath.crossFloor ? t('跨层') : t('同层') }}
            <template v-if="taskPath.crossFloor"> ×{{ taskPath.floorTransitionCount }}</template>
          </span>
          <span :class="taskPath.crossZone ? 'ap-badge-warn' : 'ap-badge-ok'">
            {{ taskPath.crossZone ? t('跨区') : t('同区') }}
            <template v-if="taskPath.crossZone"> ×{{ taskPath.zoneTransitionCount }}</template>
          </span>
          <span>{{ taskPath.source.delayMilliseconds }}ms</span>
        </div>
        <div v-if="taskPath.workloads.length" class="ap-workloads">
          <div v-for="item in taskPath.workloads" :key="`${item.floorLogicalId}:${item.zoneLogicalId ?? 'none'}`">
            {{ item.floorCode }}/{{ item.zoneCode ?? t('未分区') }}:
            {{ item.stopCount }} {{ t('点') }} / {{ formatQuantity(item.totalQuantity) }}
          </div>
        </div>
        <div v-if="taskPath.actualStops.length" class="ap-order-block">
          <div class="ap-order-title">{{ t('实际顺序（WMS）') }}</div>
          <button
            v-for="stop in taskPath.actualStops"
            :key="`actual:${stop.sequenceNo}:${stop.locationLogicalId}`"
            class="ap-stop"
            @click="$emit('locate-task-stop', stop)"
          >
            #{{ stop.sequenceNo }} {{ stop.floorCode }}/{{ stop.zoneCode ?? t('未分区') }} ·
            {{ stop.spaceLocationCode }} · {{ formatQuantity(stop.quantity) }}
            <template v-if="!stop.codeMatches"> · ⚠ WMS {{ stop.wmsLocationCode }}</template>
          </button>
        </div>
        <div v-if="optimizedStops.length" class="ap-order-block">
          <div class="ap-order-title">{{ t('优化顺序（仅演示，不回写 WMS）') }}</div>
          <div
            v-for="(stop, index) in optimizedStops"
            :key="`optimized:${stop.sequenceNo}:${stop.locationLogicalId}`"
            class="ap-stop ap-stop-static"
          >
            #{{ index + 1 }} ← WMS #{{ stop.sequenceNo }} · {{ stop.floorCode }}/{{ stop.zoneCode ?? t('未分区') }} ·
            {{ stop.spaceLocationCode }}
          </div>
        </div>
        </template>
      </div>
      <div class="ap-row" v-if="pathLoaded">
        <button class="ap-btn" @click="$emit('play')">▶</button>
        <button class="ap-btn" @click="$emit('pause')">⏸</button>
        <button class="ap-btn" @click="$emit('step')">⏭</button>
        <button class="ap-btn" @click="$emit('replay')">↺</button>
        <select class="ap-input" @change="onSpeed">
          <option value="2000">0.5x</option>
          <option value="4000" selected>1x</option>
          <option value="8000">2x</option>
        </select>
      </div>
      <div class="ap-info" v-if="pathInfo">{{ pathInfo }}</div>
      <div class="ap-info" v-if="pathLoaded && compareInfo">{{ compareInfo }}</div>
      <label class="ap-check" v-if="pathLoaded">
        <input type="checkbox" :checked="showOptimized" @change="$emit('toggle-optimized')" />{{ t('显示优化路径') }}
      </label>
    </div>

    <div class="ap-section">
      <div class="ap-title">{{ t('作业热图') }}</div>
      <div class="ap-row">
        <label class="ap-check"><input type="checkbox" :checked="workloadOn" @change="$emit('toggle-workload')" />{{ t('开启') }}</label>
        <input type="date" v-model="from" class="ap-input" />
        <input type="date" v-model="to" class="ap-input" />
        <button class="ap-btn" @click="$emit('apply-workload', { from, to })">{{ t('应用') }}</button>
      </div>
    </div>

    <div class="ap-section">
      <div class="ap-title">{{ t('设备示意') }}</div>
      <label class="ap-check"><input type="checkbox" :checked="deviceOn" @change="$emit('toggle-device')" />{{ t('显示设备') }}</label>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { SpaceDataSource } from '@/types/space/dataSource'
import { dataSourceLabel } from '@/types/space/dataSource'
import type { SpaceRuntimeTaskItem, SpaceRuntimeTaskPathResponse } from '@/types/space/runtime'
const { t } = useI18n()
defineProps<{
  pathLoaded: boolean
  pathLoading: boolean
  pathInfo: string
  compareInfo: string
  taskPath: SpaceRuntimeTaskPathResponse | null
  optimizedStops: SpaceRuntimeTaskItem[]
  showOptimized: boolean
  workloadOn: boolean
  deviceOn: boolean
  taskSource: SpaceDataSource
  workloadSource: SpaceDataSource
  deviceSource: SpaceDataSource
}>()
const emit = defineEmits<{
  (e: 'load-path', taskNo: string): void
  (e: 'locate-task-stop', stop: SpaceRuntimeTaskItem): void
  (e: 'toggle-optimized'): void
  (e: 'play'): void; (e: 'pause'): void; (e: 'step'): void; (e: 'replay'): void
  (e: 'speed', v: number): void
  (e: 'toggle-workload'): void
  (e: 'apply-workload', win: { from: string; to: string }): void
  (e: 'toggle-device'): void
}>()
const taskNo = ref('')
const today = new Date().toISOString().slice(0, 10)
const from = ref(today)
const to = ref(today)
function formatQuantity(value: number | null): string {
  return value == null ? '—' : new Intl.NumberFormat(undefined, { maximumFractionDigits: 3 }).format(value)
}
function onSpeed(ev: Event): void {
  emit('speed', Number((ev.target as HTMLSelectElement).value))
}
</script>

<style scoped>
.advanced-panel { position: absolute; right: 16px; bottom: 16px; background: rgba(12,12,28,.92);
  border: 1px solid rgba(126,87,194,.4); border-radius: 6px; color: #e0e0e0; font-size: 12px; padding: 8px 10px; z-index: 10; width: 340px; max-height: calc(100vh - 32px); overflow-y: auto; }
.ap-section { margin-bottom: 8px; }
.ap-title { color: #b39ddb; font-weight: 600; margin-bottom: 4px; }
.ap-row { display: flex; align-items: center; gap: 4px; flex-wrap: wrap; margin-bottom: 4px; }
.ap-input { background: #1a1a2e; color: #e0e0e0; border: 1px solid #37474f; border-radius: 4px; padding: 2px 4px; width: 70px; }
.ap-btn { background: transparent; color: #b39ddb; border: 1px solid #5e35b1; border-radius: 4px; cursor: pointer; padding: 2px 6px; }
.ap-btn:hover { background: rgba(126,87,194,.2); }
.ap-btn:disabled { opacity: .55; cursor: wait; }
.ap-check { display: flex; align-items: center; gap: 4px; }
.ap-info { color: #80cbc4; margin-top: 2px; }
.ap-task-summary { border: 1px solid rgba(128,203,196,.22); border-radius: 4px; padding: 5px; margin: 4px 0; }
.ap-task-id { color: #e0f2f1; font-weight: 700; margin-bottom: 2px; }
.ap-state-error { color: #ef9a9a; margin: 3px 0; }
.ap-state-empty { color: #b0bec5; margin: 3px 0; }
.ap-badges { display: flex; flex-wrap: wrap; gap: 4px; margin: 4px 0; color: #90a4ae; font-size: 10px; }
.ap-badges span { border: 1px solid #37474f; border-radius: 999px; padding: 1px 5px; }
.ap-badge-ok { color: #81c784; }
.ap-badge-warn { color: #ffb74d; }
.ap-workloads { color: #b0bec5; font-size: 10px; line-height: 1.45; margin: 4px 0; }
.ap-order-block { margin-top: 5px; }
.ap-order-title { color: #ce93d8; font-size: 10px; font-weight: 700; margin-bottom: 2px; }
.ap-stop { display: block; width: 100%; background: rgba(38,50,56,.58); color: #e0f2f1; border: 0; border-left: 2px solid #26c6da; text-align: left; padding: 3px 5px; margin: 2px 0; cursor: pointer; font-size: 10px; }
.ap-stop:hover { background: rgba(38,198,218,.16); }
.ap-stop-static { border-left-color: #76ff03; cursor: default; color: #dcedc8; }
.ap-sources { display: flex; justify-content: space-between; gap: 4px; margin-bottom: 6px;
  font-size: 9px; font-weight: 700; letter-spacing: .03em; }
.source-real { color: #66bb6a; }
.source-simulated { color: #ffb74d; }
.source-unavailable { color: #ef5350; }
</style>
