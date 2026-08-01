<template>
  <section class="locate-results" aria-label="inventory-locate-results">
    <header class="locate-header">
      <strong>{{ t('库存定位结果') }}</strong>
      <button type="button" class="locate-close" :title="t('关闭')" @click="emit('close')">×</button>
    </header>

    <div class="locate-criteria">{{ criteriaText }}</div>
    <div class="locate-source">
      {{ response.source.dataSourceId }} · {{ formatTime(response.source.observedAtUtc) }}
      <span v-if="response.source.delayMilliseconds > 0">
        · {{ t('延迟') }} {{ response.source.delayMilliseconds }} ms
      </span>
      <span v-if="response.source.clockSkewMilliseconds > 0">
        · {{ t('来源时钟超前') }} {{ response.source.clockSkewMilliseconds }} ms
      </span>
    </div>

    <div v-if="!response.source.isAvailable" class="locate-state locate-unavailable">
      {{ t('库存数据源不可用，不能判定是否存在匹配库存') }}
    </div>
    <div v-else-if="response.locationCount === 0" class="locate-state locate-empty">
      {{ t('没有库位匹配当前物料、批次或容器条件') }}
    </div>
    <template v-else>
      <div class="locate-summary">
        {{ t('找到 {locations} 个库位，分布在 {floors} 个楼层')
          .replace('{locations}', String(response.locationCount))
          .replace('{floors}', String(response.floorCount)) }}
      </div>
      <div class="locate-groups">
        <section v-for="group in floorGroups" :key="group.floorLogicalId" class="locate-floor">
          <h4>{{ group.floorName }} · {{ group.floorCode }} ({{ group.items.length }})</h4>
          <button
            v-for="hit in group.items"
            :key="hit.locationLogicalId"
            type="button"
            class="locate-hit"
            @click="emit('select', hit)"
          >
            <span class="locate-code">{{ hit.spaceLocationCode }}</span>
            <span class="locate-qty">{{ hit.physicalQuantity }}</span>
            <span v-if="!hit.codeMatches" class="locate-warning">{{ t('WMS 编码不一致') }}</span>
            <small>{{ factText(hit) }}</small>
          </button>
        </section>
      </div>
    </template>
  </section>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type {
  SpaceRuntimeInventoryLocateHit,
  SpaceRuntimeInventoryLocateResponse,
} from '@/types/space/runtime'

const props = defineProps<{
  response: SpaceRuntimeInventoryLocateResponse
}>()
const emit = defineEmits<{
  (event: 'select', hit: SpaceRuntimeInventoryLocateHit): void
  (event: 'close'): void
}>()
const { t } = useI18n()

const criteriaText = computed(() => {
  const criteria = props.response.criteria
  return [
    criteria.materialNumber && `${t('物料')}=${criteria.materialNumber}`,
    criteria.lotNumber && `${t('批次')}=${criteria.lotNumber}`,
    criteria.containerNumber && `${t('容器')}=${criteria.containerNumber}`,
  ].filter(Boolean).join(' · ')
})

const floorGroups = computed(() => {
  const groups = new Map<string, {
    floorLogicalId: string
    floorCode: string
    floorName: string
    items: SpaceRuntimeInventoryLocateHit[]
  }>()
  for (const hit of props.response.items) {
    const existing = groups.get(hit.floorLogicalId)
    if (existing) existing.items.push(hit)
    else groups.set(hit.floorLogicalId, {
      floorLogicalId: hit.floorLogicalId,
      floorCode: hit.floorCode,
      floorName: hit.floorName,
      items: [hit],
    })
  }
  return [...groups.values()]
})

function factText(hit: SpaceRuntimeInventoryLocateHit): string {
  return [
    hit.materialNumbers.length && `${t('物料')} ${hit.materialNumbers.join(', ')}`,
    hit.lotNumbers.length && `${t('批次')} ${hit.lotNumbers.join(', ')}`,
    hit.containerNumbers.length && `${t('容器')} ${hit.containerNumbers.join(', ')}`,
  ].filter(Boolean).join(' · ')
}

function formatTime(value: string): string {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
</script>

<style scoped>
.locate-results {
  width: 360px;
  max-height: min(62vh, 560px);
  overflow: hidden;
  color: #dbe9f4;
  background: rgba(10, 15, 29, 0.96);
  border: 1px solid rgba(79, 195, 247, 0.35);
  border-radius: 7px;
  box-shadow: 0 8px 28px rgba(0, 0, 0, 0.35);
  font-size: 12px;
}
.locate-header,
.locate-hit {
  display: flex;
  align-items: center;
}
.locate-header {
  justify-content: space-between;
  padding: 9px 11px;
  border-bottom: 1px solid rgba(79, 195, 247, 0.18);
}
.locate-close {
  color: #90a4ae;
  background: none;
  border: 0;
  cursor: pointer;
  font-size: 18px;
}
.locate-criteria,
.locate-source,
.locate-summary,
.locate-state {
  padding: 7px 11px 0;
}
.locate-source { color: #78909c; }
.locate-state { padding-bottom: 12px; }
.locate-unavailable { color: #ffab91; }
.locate-empty { color: #b0bec5; }
.locate-groups {
  max-height: min(47vh, 430px);
  padding: 4px 8px 10px;
  overflow-y: auto;
}
.locate-floor h4 {
  margin: 9px 3px 5px;
  color: #81d4fa;
  font-size: 11px;
  font-weight: 600;
}
.locate-hit {
  width: 100%;
  flex-wrap: wrap;
  gap: 4px 9px;
  margin: 3px 0;
  padding: 7px 8px;
  color: #dbe9f4;
  text-align: left;
  background: rgba(255, 255, 255, 0.035);
  border: 1px solid transparent;
  border-radius: 4px;
  cursor: pointer;
}
.locate-hit:hover,
.locate-hit:focus-visible {
  background: rgba(79, 195, 247, 0.1);
  border-color: rgba(79, 195, 247, 0.35);
}
.locate-code { font-family: monospace; font-weight: 700; }
.locate-qty { margin-left: auto; color: #a5d6a7; }
.locate-warning { color: #ffab91; }
.locate-hit small { width: 100%; color: #90a4ae; }
</style>
