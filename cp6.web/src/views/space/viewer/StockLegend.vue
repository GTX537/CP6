<template>
  <div class="stock-legend">
    <div class="source-badge" :class="`source-${source.kind.toLowerCase()}`">
      {{ dataSourceLabel(source) }} · {{ source.dataSourceId }}
    </div>

    <dl class="trust-grid">
      <dt>{{ t('来源系统') }}</dt>
      <dd>{{ source.dataSourceId }}</dd>
      <dt>{{ t('运行连接') }}</dt>
      <dd>{{ source.adapterId }}</dd>
      <dt>{{ t('数据时间') }}</dt>
      <dd>{{ formatTime(ts || source.observedAtUtc) }}</dd>
      <dt>{{ t('系统接收') }}</dt>
      <dd>{{ formatTime(source.receivedAtUtc) }}</dd>
      <dt>{{ t('快照延迟') }}</dt>
      <dd>{{ formatDuration(source.delayMilliseconds) }}</dd>
      <template v-if="source.clockSkewMilliseconds > 0">
        <dt class="trust-warning">{{ t('时钟超前') }}</dt>
        <dd class="trust-warning">{{ formatDuration(source.clockSkewMilliseconds) }}</dd>
      </template>
      <dt>{{ t('最近成功') }}</dt>
      <dd>{{ refreshState.lastSuccessfulAtUtc ? formatTime(refreshState.lastSuccessfulAtUtc) : t('本次会话尚无') }}</dd>
      <dt>{{ t('最近失败') }}</dt>
      <dd :class="`failure-${refreshState.failureState}`">{{ failureText }}</dd>
    </dl>

    <div class="legend-modes">
      <button :class="{ on: mode === 'status' }" @click="$emit('mode', 'status')">{{ t('占用状态') }}</button>
      <button :class="{ on: mode === 'utilization' }" @click="$emit('mode', 'utilization')">{{ t('占用估算') }}</button>
      <button :class="{ on: mode === 'off' }" @click="$emit('mode', 'off')">{{ t('关闭') }}</button>
    </div>
    <button class="legend-refresh" @click="$emit('refresh')">{{ t('刷新库存') }}</button>
    <label class="legend-poll">
      <input type="checkbox" :checked="polling" @change="$emit('toggle-poll')" />
      {{ t('自动刷新') }}
    </label>

    <ul v-if="mode === 'status'" class="legend-items">
      <li><i class="sw" style="background:#4caf50" />{{ t('空') }}</li>
      <li><i class="sw" style="background:#2196f3" />{{ t('有库存') }}</li>
    </ul>
    <div v-else-if="mode === 'utilization'" class="legend-estimate">
      {{ t('统一运行源暂无容量；颜色表示空/有货的占用估算。') }}
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { OverlayMode } from '@/types/space/overlay'
import type { SpaceRuntimeSource } from '@/types/space/runtime'
import { dataSourceLabel } from '@/types/space/dataSource'
import type { RuntimeRefreshState } from '@/space-viewer/overlay/runtimeRefreshState'
import { formatDateTime } from '@/utils/format'

const props = defineProps<{
  mode: OverlayMode
  polling: boolean
  ts: string
  source: SpaceRuntimeSource
  refreshState: RuntimeRefreshState
}>()

defineEmits<{
  (event: 'mode', mode: OverlayMode): void
  (event: 'refresh'): void
  (event: 'toggle-poll'): void
}>()

const { t } = useI18n()

const failureText = computed(() => {
  const state = props.refreshState
  if (state.failureState === 'never') return t('本次会话未发生')
  const status = state.failureState === 'active' ? t('当前失败') : t('已恢复')
  const time = formatTime(state.lastFailureAtUtc)
  const code = state.lastFailureCode ? ` · ${state.lastFailureCode}` : ''
  return `${status} · ${time}${code}`
})

function formatTime(value?: string | null): string {
  return formatDateTime(value) || '—'
}

function formatDuration(milliseconds: number): string {
  if (milliseconds < 1000) return `${milliseconds} ms`
  if (milliseconds < 60_000) return `${(milliseconds / 1000).toFixed(1)} s`
  return `${(milliseconds / 60_000).toFixed(1)} min`
}
</script>

<style scoped>
.stock-legend {
  position: absolute;
  left: 16px;
  bottom: 16px;
  width: 330px;
  background: rgba(12, 12, 28, 0.94);
  border: 1px solid rgba(79, 195, 247, 0.35);
  border-radius: 6px;
  color: #e0e0e0;
  font-size: 12px;
  padding: 10px 12px;
  z-index: 10;
}
.source-badge { font-weight: 700; margin-bottom: 7px; letter-spacing: .04em; }
.source-real { color: #66bb6a; }
.source-simulated { color: #ffb74d; }
.source-unavailable { color: #ef5350; }
.trust-grid {
  display: grid;
  grid-template-columns: 68px minmax(0, 1fr);
  gap: 3px 8px;
  margin: 0 0 8px;
  padding: 0 0 8px;
  border-bottom: 1px solid rgba(255, 255, 255, .1);
}
.trust-grid dt { color: #78909c; }
.trust-grid dd { margin: 0; overflow-wrap: anywhere; color: #cfd8dc; }
.trust-grid .trust-warning { color: #ffb74d; }
.failure-active { color: #ef5350 !important; }
.failure-recovered { color: #66bb6a !important; }
.failure-never { color: #90a4ae !important; }
.legend-modes { display: flex; flex-wrap: wrap; }
.legend-modes button,
.legend-refresh {
  background: transparent;
  color: #9fb3c8;
  border: 1px solid #37474f;
  border-radius: 4px;
  margin: 2px;
  cursor: pointer;
}
.legend-modes button.on { color: #4fc3f7; border-color: #4fc3f7; }
.legend-poll { margin-left: 5px; color: #9fb3c8; }
.legend-items { list-style: none; padding: 4px 0 0; margin: 0; }
.legend-items li { display: flex; align-items: center; gap: 6px; line-height: 1.6; }
.sw { width: 12px; height: 12px; display: inline-block; border-radius: 2px; }
.legend-estimate { color: #90a4ae; margin-top: 6px; max-width: 300px; }
</style>
