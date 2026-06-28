<template>
  <div v-if="locationId" class="info-card">
    <div class="info-card__header">
      <span class="info-card__code">{{ detail?.locationCode ?? locationId }}</span>
      <button class="info-card__close" @click="$emit('close')">✕</button>
    </div>
    <div v-if="loading" class="info-card__row info-card__loading">{{ t('加载中') }}</div>
    <template v-else-if="detail">
      <div class="info-card__row">
        <span class="info-card__label">{{ t('路径') }}</span>
        <span class="info-card__value">{{ pathText }}</span>
      </div>
      <div class="info-card__row">
        <span class="info-card__label">{{ t('状态') }}</span>
        <span class="info-card__value">{{ detail.status }}</span>
      </div>
      <div class="info-card__row">
        <span class="info-card__label">{{ t('编码来源') }}</span>
        <span class="info-card__value">{{ detail.codeOrigin }}</span>
      </div>
      <div class="info-card__row">
        <span class="info-card__label">{{ t('坐标(mm)') }}</span>
        <span class="info-card__value">{{ detail.absX }}, {{ detail.absY }}, {{ detail.absZ }}</span>
      </div>
      <template v-if="stock">
        <div class="info-card__row info-card__sep">{{ t('库存') }}</div>
        <div class="info-card__row">
          <span class="info-card__label">{{ t('库位状态') }}</span>
          <span class="info-card__value">{{ binStatusText }}</span>
        </div>
        <div class="info-card__row">
          <span class="info-card__label">{{ t('库存量') }}</span>
          <span class="info-card__value">{{ stock.qty }}<template v-if="stock.capacity"> / {{ stock.capacity }}（{{ utilPct }}%）</template></span>
        </div>
        <div class="info-card__row" v-if="stock.topMaterial">
          <span class="info-card__label">{{ t('主物料') }}</span>
          <span class="info-card__value">{{ stock.topMaterial }}</span>
        </div>
      </template>
    </template>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { locateApi } from '@/api/space/locate'
import type { LocationDetail } from '@/types/space/viewer'
import { locationUtilization } from '@/space-viewer/overlay/stockModel'

const props = defineProps<{ locationId: string | null; stock?: import('@/types/space/overlay').WmsStockDto | null }>()
defineEmits<{ (e: 'close'): void }>()

const { t } = useI18n()
const detail = ref<LocationDetail | null>(null)
const loading = ref(false)

const pathText = computed(() => {
  if (!detail.value) return ''
  const p = detail.value.path
  return [
    p.siteCode,
    `F${p.floorLevel}`,
    p.zoneCode,
    p.aisleCode ?? undefined,
    p.rackCode,
    `C${p.col}L${p.level}D${p.depth}`,
  ]
    .filter(Boolean)
    .join(' / ')
})

const binStatusText = computed(() => {
  const m = ['空', '有货', '满', '锁定', '在拣']
  return props.stock ? t(m[props.stock.binStatus] ?? '无数据') : ''
})
const utilPct = computed(() =>
  props.stock ? Math.round(locationUtilization(props.stock) * 100) : 0)

watch(
  () => props.locationId,
  async (id) => {
    detail.value = null
    if (!id) return
    loading.value = true
    try {
      const env = await locateApi.detail(id)
      detail.value = env.data
    } catch {
      // silently fail — header shows locationId as fallback
    } finally {
      loading.value = false
    }
  },
  { immediate: true },
)
</script>

<style scoped>
.info-card {
  position: absolute;
  right: 16px;
  top: 16px;
  width: 280px;
  background: rgba(12, 12, 28, 0.92);
  border: 1px solid rgba(79, 195, 247, 0.35);
  border-radius: 6px;
  color: #e0e0e0;
  font-size: 13px;
  z-index: 10;
  user-select: none;
}
.info-card__header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 10px 12px 8px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
}
.info-card__code { font-weight: 600; color: #4fc3f7; font-size: 14px; }
.info-card__close {
  background: none;
  border: none;
  color: #90a4ae;
  cursor: pointer;
  font-size: 14px;
  line-height: 1;
  padding: 0 2px;
}
.info-card__close:hover { color: #e0e0e0; }
.info-card__row {
  display: flex;
  gap: 8px;
  padding: 5px 12px;
  line-height: 1.4;
}
.info-card__loading { color: #90caf9; justify-content: center; }
.info-card__label { color: #78909c; min-width: 64px; flex-shrink: 0; }
.info-card__value { word-break: break-all; }
</style>
