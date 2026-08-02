<template>
  <section class="inventory-filter" aria-label="inventory-spatial-filter">
    <header>
      <strong>{{ t('库存空间筛选') }}</strong>
      <span v-if="response" class="source" :class="`source-${response.source.kind.toLowerCase()}`">
        {{ dataSourceLabel(response.source) }}
      </span>
    </header>

    <div class="filter-grid">
      <label>
        <span>{{ t('货主') }}</span>
        <input v-model="ownerId" :placeholder="t('精确货主编码')" @keydown.enter.prevent="apply" />
      </label>
      <label>
        <span>{{ t('SKU') }}</span>
        <input v-model="materialNumber" :placeholder="t('精确物料号')" @keydown.enter.prevent="apply" />
      </label>
      <label>
        <span>{{ t('批次') }}</span>
        <input v-model="lotNumber" :placeholder="t('精确批次号')" @keydown.enter.prevent="apply" />
      </label>
      <label>
        <span>{{ t('容器') }}</span>
        <input v-model="containerNumber" :placeholder="t('精确容器号')" @keydown.enter.prevent="apply" />
      </label>
    </div>

    <div class="actions">
      <button :disabled="loading || !hasCriteria" @click="apply">
        {{ loading ? t('筛选中') : t('应用筛选') }}
      </button>
      <button class="secondary" @click="clear">
        {{ t('清除') }}
      </button>
      <span class="and-hint">{{ t('多个条件按精确 AND 匹配') }}</span>
    </div>

    <div v-if="response" class="result">
      <div v-if="!response.source.isAvailable" class="unavailable">
        {{ t('库存数据源不可用，不能判定筛选结果') }}
      </div>
      <template v-else>
        <div class="summary">
          {{ t('本层 {current} / 全站 {total} 个库位 / {floors} 个楼层')
            .replace('{current}', String(currentFloorCount))
            .replace('{total}', String(response.locationCount))
            .replace('{floors}', String(response.floorCount)) }}
        </div>
        <div class="legend">
          <span><i class="match" />{{ t('命中') }}</span>
          <span><i class="excluded" />{{ t('未命中') }}</span>
        </div>
        <div v-if="floorGroups.length" class="floor-groups">
          <button
            v-for="floor in floorGroups"
            :key="floor.floorLogicalId"
            :class="{ current: floor.floorLogicalId === currentFloorId }"
            @click="$emit('switch-floor', floor.floorLogicalId)"
          >
            {{ floor.floorName }} · {{ floor.count }}
          </button>
        </div>
        <div v-else class="empty">{{ t('没有库位匹配当前筛选条件') }}</div>
      </template>
      <div class="provenance">
        {{ response.source.dataSourceId }} · {{ response.source.observedAtUtc }}
      </div>
    </div>
  </section>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { dataSourceLabel } from '@/types/space/dataSource'
import type {
  SpaceRuntimeInventoryLocateQuery,
  SpaceRuntimeInventoryLocateResponse,
} from '@/types/space/runtime'

const props = defineProps<{
  loading: boolean
  response: SpaceRuntimeInventoryLocateResponse | null
  currentFloorId: string
}>()

const emit = defineEmits<{
  (event: 'apply', criteria: SpaceRuntimeInventoryLocateQuery): void
  (event: 'clear'): void
  (event: 'switch-floor', floorLogicalId: string): void
}>()

const { t } = useI18n()
const ownerId = ref('')
const materialNumber = ref('')
const lotNumber = ref('')
const containerNumber = ref('')

const criteria = computed<SpaceRuntimeInventoryLocateQuery>(() => ({
  ownerId: ownerId.value.trim().toUpperCase() || undefined,
  materialNumber: materialNumber.value.trim() || undefined,
  lotNumber: lotNumber.value.trim() || undefined,
  containerNumber: containerNumber.value.trim() || undefined,
}))

const hasCriteria = computed(() => Object.values(criteria.value).some(Boolean))
const currentFloorCount = computed(() => props.response?.items.filter(
  (item) => item.floorLogicalId === props.currentFloorId,
).length ?? 0)
const floorGroups = computed(() => {
  if (!props.response?.source.isAvailable) return []
  const groups = new Map<string, { floorLogicalId: string; floorName: string; floorLevel: number; count: number }>()
  for (const item of props.response.items) {
    const group = groups.get(item.floorLogicalId)
    if (group) group.count++
    else groups.set(item.floorLogicalId, {
      floorLogicalId: item.floorLogicalId,
      floorName: item.floorName,
      floorLevel: item.floorLevel,
      count: 1,
    })
  }
  return [...groups.values()].sort((left, right) =>
    left.floorLevel - right.floorLevel || left.floorName.localeCompare(right.floorName))
})

function apply(): void {
  if (!hasCriteria.value || props.loading) return
  emit('apply', criteria.value)
}

function clear(): void {
  ownerId.value = ''
  materialNumber.value = ''
  lotNumber.value = ''
  containerNumber.value = ''
  emit('clear')
}
</script>

<style scoped>
.inventory-filter {
  width: 300px;
  padding: 10px 12px;
  color: #dce8ef;
  background: rgba(10, 15, 29, .94);
  border: 1px solid rgba(255, 193, 7, .35);
  border-radius: 6px;
  font-size: 12px;
}
header, .actions, .legend, .floor-groups { display: flex; align-items: center; gap: 7px; }
header { justify-content: space-between; margin-bottom: 8px; }
.source { font-size: 10px; letter-spacing: .04em; }
.source-real { color: #66bb6a; }
.source-simulated { color: #ffb74d; }
.source-unavailable { color: #ef5350; }
.filter-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 6px; }
label span { display: block; margin-bottom: 2px; color: #90a4ae; }
input {
  box-sizing: border-box;
  width: 100%;
  padding: 5px 6px;
  color: #e0f7fa;
  background: rgba(0, 0, 0, .2);
  border: 1px solid #37474f;
  border-radius: 4px;
  outline: none;
}
input:focus { border-color: #ffc107; }
.actions { flex-wrap: wrap; margin-top: 8px; }
button {
  padding: 4px 8px;
  color: #16120a;
  background: #ffc107;
  border: 1px solid #ffc107;
  border-radius: 4px;
  cursor: pointer;
}
button.secondary, .floor-groups button {
  color: #b0bec5;
  background: transparent;
  border-color: #455a64;
}
button:disabled { cursor: not-allowed; opacity: .45; }
.and-hint { color: #78909c; font-size: 10px; }
.result { margin-top: 8px; padding-top: 7px; border-top: 1px solid rgba(255,255,255,.1); }
.summary { color: #ffe082; }
.legend { margin-top: 5px; color: #90a4ae; }
.legend i { display: inline-block; width: 10px; height: 10px; margin-right: 3px; border-radius: 2px; }
.legend .match { background: #ffc107; }
.legend .excluded { background: #263238; border: 1px solid #607d8b; }
.floor-groups { flex-wrap: wrap; max-height: 74px; margin-top: 6px; overflow-y: auto; }
.floor-groups button { padding: 3px 6px; font-size: 11px; }
.floor-groups button.current { color: #ffc107; border-color: #ffc107; }
.unavailable { color: #ef9a9a; }
.empty { margin-top: 5px; color: #90a4ae; }
.provenance { margin-top: 6px; color: #607d8b; font-size: 10px; overflow-wrap: anywhere; }
</style>
