<!-- cp6.web/src/views/space/viewer/StockLegend.vue -->
<template>
  <div class="stock-legend">
    <div class="legend-modes">
      <button :class="{ on: mode === 'structure' }" @click="$emit('mode', 'structure')">{{ t('结构') }}</button>
      <button :class="{ on: mode === 'status' }" @click="$emit('mode', 'status')">{{ t('状态') }}</button>
      <button :class="{ on: mode === 'utilization' }" @click="$emit('mode', 'utilization')">{{ t('利用率') }}</button>
      <button :class="{ on: mode === 'storageType' }" @click="$emit('mode', 'storageType')">{{ t('存储类型') }}</button>
      <button :class="{ on: mode === 'abc' }" @click="$emit('mode', 'abc')">ABC</button>
    </div>
    <button class="legend-refresh" @click="$emit('refresh')">{{ t('刷新库存') }}</button>
    <label class="legend-poll"><input type="checkbox" :checked="polling" @change="$emit('toggle-poll')" />{{ t('自动刷新') }}</label>
    <span class="legend-live" :class="live ? 'ok' : 'ng'">{{ live ? '● LIVE' : '○ OFFLINE' }}</span>
    <span v-if="warningCount" class="legend-warning">⚠ {{ warningCount }}</span>
    <div v-if="ts" class="legend-ts">{{ t('数据时间') }} {{ ts }}</div>
    <ul v-if="mode === 'status'" class="legend-items">
      <li><i class="sw" style="background:#4caf50" />{{ t('空') }}</li>
      <li><i class="sw" style="background:#2196f3" />{{ t('有货') }}</li>
      <li><i class="sw" style="background:#f44336" />{{ t('满') }}</li>
      <li><i class="sw" style="background:#9e9e9e" />{{ t('锁定') }}</li>
      <li><i class="sw" style="background:#ffc107" />{{ t('在拣') }}</li>
    </ul>
    <div v-else-if="mode === 'utilization'" class="legend-grad">{{ t('低') }} <i class="grad" /> {{ t('高') }}</div>
    <ul v-else-if="mode === 'storageType'" class="legend-items legend-grid">
      <li v-for="item in storageLegend" :key="item.key"><i class="sw" :style="{ background: item.color }" />{{ t(item.key) }}</li>
    </ul>
    <ul v-else-if="mode === 'abc'" class="legend-items">
      <li><i class="sw" style="background:#e11d48" />A · {{ t('高频') }}</li>
      <li><i class="sw" style="background:#f59e0b" />B · {{ t('中频') }}</li>
      <li><i class="sw" style="background:#64748b" />C · {{ t('低频') }}</li>
    </ul>
    <div v-if="stats.length" class="legend-stats">
      <div v-for="item in stats" :key="item.label" class="stat-row">
        <span>{{ item.label }}</span><strong>{{ item.value }}</strong>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import type { OverlayMode } from '@/types/space/overlay'
const { t } = useI18n()
defineProps<{
  mode: OverlayMode
  polling: boolean
  ts: string
  live: boolean
  warningCount: number
  stats: Array<{ label: string; value: string }>
}>()
defineEmits<{ (e: 'mode', m: OverlayMode): void; (e: 'refresh'): void; (e: 'toggle-poll'): void }>()

const storageLegend = [
  { key: 'storage', color: '#3b82f6' }, { key: 'receiving', color: '#22c55e' },
  { key: 'shipping', color: '#f97316' }, { key: 'picking', color: '#a855f7' },
  { key: 'passage', color: '#64748b' }, { key: 'inspection', color: '#06b6d4' },
  { key: 'return', color: '#f43f5e' }, { key: 'frozen', color: '#0ea5e9' },
]
</script>

<style scoped>
.stock-legend { position: absolute; left: 16px; bottom: 16px; width: 286px; max-height: 50vh; overflow: auto; background: rgba(12,12,28,.94);
  border: 1px solid rgba(79,195,247,.35); border-radius: 8px; color: #e0e0e0; font-size: 12px; padding: 8px 10px; z-index: 10; }
.legend-modes { display: flex; flex-wrap: wrap; }
.legend-modes button, .legend-refresh { background: transparent; color: #9fb3c8; border: 1px solid #37474f; border-radius: 4px; margin: 2px; cursor: pointer; }
.legend-modes button.on { color: #4fc3f7; border-color: #4fc3f7; }
.legend-items { list-style: none; padding: 4px 0 0; margin: 0; }
.legend-items li { display: flex; align-items: center; gap: 6px; line-height: 1.6; }
.legend-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 0 8px; }
.sw { width: 12px; height: 12px; display: inline-block; border-radius: 2px; }
.grad { display: inline-block; width: 80px; height: 10px; background: linear-gradient(90deg,#2196f3,#ffc107,#f44336); vertical-align: middle; }
.legend-ts { color: #78909c; margin-top: 4px; }
.legend-live { margin-left: 6px; font-size: 10px; font-weight: 700; }
.legend-live.ok { color: #22c55e; }
.legend-live.ng { color: #ef4444; }
.legend-warning { margin-left: 6px; color: #f59e0b; }
.legend-stats { border-top: 1px solid rgba(148,163,184,.18); margin-top: 6px; padding-top: 4px; }
.stat-row { display: flex; justify-content: space-between; gap: 8px; color: #9fb3c8; line-height: 1.7; }
.stat-row strong { color: #e2e8f0; font-family: Consolas, monospace; }
</style>
