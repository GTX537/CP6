<template>
  <div class="floor-list">
    <div class="floor-list__header">{{ t('楼层') }}</div>
    <div class="floor-list__items">
      <div
        v-for="f in floors"
        :key="f.id"
        class="floor-list__item"
        :class="{ 'floor-list__item--active': f.id === currentFloorId }"
        :data-floor-id="f.id"
        @click="onFloorClick(f.id)"
      >
        <span class="floor-list__level">F{{ f.level }}</span>
        <span class="floor-list__name" :title="f.floorName || f.floorCode">
          {{ f.floorName || f.floorCode }}
        </span>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onBeforeUnmount } from 'vue'
import { useI18n } from 'vue-i18n'
import type { FloorVO } from '@/types/space/scene'

const props = defineProps<{
  floors: readonly FloorVO[]
  currentFloorId: string
}>()
const emit = defineEmits<{ (e: 'switch-floor', floorId: string): void }>()

const { t } = useI18n()
let debounceTimer = 0

/** Debounce rapid clicks — only emit the last floor clicked within 250 ms. */
function onFloorClick(floorId: string): void {
  clearTimeout(debounceTimer)
  debounceTimer = window.setTimeout(() => {
    if (floorId !== props.currentFloorId) {
      emit('switch-floor', floorId)
    }
  }, 250)
}

onBeforeUnmount(() => { clearTimeout(debounceTimer) })
</script>

<style scoped>
.floor-list {
  display: flex;
  flex-direction: column;
  height: 100%;
  color: #b0bec5;
  font-size: 12px;
  user-select: none;
}

.floor-list__header {
  padding: 10px 12px 6px;
  font-size: 11px;
  font-weight: 600;
  color: #4fc3f7;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  border-bottom: 1px solid rgba(255, 255, 255, 0.06);
}

.floor-list__items {
  flex: 1;
  overflow-y: auto;
}

.floor-list__item {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 7px 12px;
  cursor: pointer;
  border-left: 2px solid transparent;
  transition: background 0.12s, border-color 0.12s;
  overflow: hidden;
}

.floor-list__item:hover {
  background: rgba(79, 195, 247, 0.08);
}

.floor-list__item--active {
  background: rgba(79, 195, 247, 0.12);
  border-left-color: #4fc3f7;
  color: #e0f7fa;
}

.floor-list__level {
  flex-shrink: 0;
  font-weight: 600;
  color: #78909c;
  min-width: 24px;
}

.floor-list__item--active .floor-list__level {
  color: #4fc3f7;
}

.floor-list__name {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
