<!-- cp6.web/src/views/space/viewer/StockLegend.vue -->
<template>
  <div class="stock-legend">
    <div class="source-badge" :class="`source-${source.kind.toLowerCase()}`">
      {{ dataSourceLabel(source) }} · {{ source.dataSourceId }}
    </div>
    <div class="legend-modes">
      <button :class="{ on: mode === 'status' }" @click="$emit('mode', 'status')">{{ t('状态') }}</button>
      <button :class="{ on: mode === 'utilization' }" @click="$emit('mode', 'utilization')">{{ t('利用率') }}</button>
      <button :class="{ on: mode === 'off' }" @click="$emit('mode', 'off')">{{ t('关闭') }}</button>
    </div>
    <button class="legend-refresh" @click="$emit('refresh')">{{ t('刷新库存') }}</button>
    <label class="legend-poll"><input type="checkbox" :checked="polling" @change="$emit('toggle-poll')" />{{ t('自动刷新') }}</label>
    <div v-if="ts" class="legend-ts">{{ t('数据时间') }} {{ ts }}</div>
    <ul v-if="mode === 'status'" class="legend-items">
      <li><i class="sw" style="background:#4caf50" />{{ t('空') }}</li>
      <li><i class="sw" style="background:#2196f3" />{{ t('有货') }}</li>
      <li><i class="sw" style="background:#f44336" />{{ t('满') }}</li>
      <li><i class="sw" style="background:#9e9e9e" />{{ t('锁定') }}</li>
      <li><i class="sw" style="background:#ffc107" />{{ t('在拣') }}</li>
    </ul>
    <div v-else-if="mode === 'utilization'" class="legend-grad">{{ t('低') }} <i class="grad" /> {{ t('高') }}</div>
  </div>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import type { OverlayMode } from '@/types/space/overlay'
import type { SpaceDataSource } from '@/types/space/dataSource'
import { dataSourceLabel } from '@/types/space/dataSource'
const { t } = useI18n()
defineProps<{ mode: OverlayMode; polling: boolean; ts: string; source: SpaceDataSource }>()
defineEmits<{ (e: 'mode', m: OverlayMode): void; (e: 'refresh'): void; (e: 'toggle-poll'): void }>()
</script>

<style scoped>
.stock-legend { position: absolute; left: 16px; bottom: 16px; background: rgba(12,12,28,.92);
  border: 1px solid rgba(79,195,247,.35); border-radius: 6px; color: #e0e0e0; font-size: 12px; padding: 8px 10px; z-index: 10; }
.legend-modes button, .legend-refresh { background: transparent; color: #9fb3c8; border: 1px solid #37474f; border-radius: 4px; margin: 2px; cursor: pointer; }
.legend-modes button.on { color: #4fc3f7; border-color: #4fc3f7; }
.legend-items { list-style: none; padding: 4px 0 0; margin: 0; }
.legend-items li { display: flex; align-items: center; gap: 6px; line-height: 1.6; }
.sw { width: 12px; height: 12px; display: inline-block; border-radius: 2px; }
.grad { display: inline-block; width: 80px; height: 10px; background: linear-gradient(90deg,#2196f3,#ffc107,#f44336); vertical-align: middle; }
.legend-ts { color: #78909c; margin-top: 4px; }
.source-badge { font-weight: 700; margin-bottom: 5px; letter-spacing: .04em; }
.source-real { color: #66bb6a; }
.source-simulated { color: #ffb74d; }
.source-unavailable { color: #ef5350; }
</style>
