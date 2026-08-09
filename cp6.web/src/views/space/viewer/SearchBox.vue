<template>
  <div class="search-box">
    <select v-model="mode" class="sb-mode">
      <option value="code">{{ t('按编码') }}</option>
      <option value="material">{{ t('按物料') }}</option>
      <option value="lot">{{ t('按批次') }}</option>
      <option value="container">{{ t('按容器') }}</option>
    </select>
    <el-input
      v-model="query"
      :placeholder="placeholder"
      clearable
      size="small"
      @input="onInput"
      @keydown.enter.prevent="onEnter"
      @blur="onBlur"
      @clear="onClear"
    >
      <template #prefix>
        <el-icon><Search /></el-icon>
      </template>
    </el-input>

    <div v-if="candidates.length > 0" class="search-candidates">
      <div
        v-for="c in candidates"
        :key="c.locationId"
        class="search-candidate-item"
        @mousedown.prevent="onSelectCandidate(c)"
      >
        <span class="search-candidate-code">{{ c.locationCode ?? c.locationId }}</span>
        <span v-if="!c.placed" class="search-candidate-tag">{{ t('未放置') }}</span>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { Search } from '@element-plus/icons-vue'
import { locateApi } from '@/api/space/locate'
import type { LocateResult } from '@/types/space/viewer'
import type { SpaceRuntimeInventoryLocateQuery } from '@/types/space/runtime'

const emit = defineEmits<{
  (e: 'locate', code: string): void
  (e: 'locate-stock', criteria: SpaceRuntimeInventoryLocateQuery): void
}>()

const { t } = useI18n()
const mode = ref<'code' | 'material' | 'lot' | 'container'>('code')
const query = ref('')
const candidates = ref<LocateResult[]>([])
const placeholder = computed(() => ({
  code: t('搜索库位编码'),
  material: t('输入物料号'),
  lot: t('输入批次号'),
  container: t('输入容器号'),
})[mode.value])
let debounceTimer = 0

function onInput(): void {
  clearTimeout(debounceTimer)
  if (mode.value !== 'code') {
    candidates.value = []
    return
  }
  const q = query.value.trim()
  if (q.length < 2) {
    candidates.value = []
    return
  }
  debounceTimer = window.setTimeout(() => { fetchCandidates(q) }, 300)
}

async function fetchCandidates(prefix: string): Promise<void> {
  try {
    const env = await locateApi.search(prefix)
    candidates.value = env.data ?? []
  } catch {
    candidates.value = []
  }
}

/** Enter key: emit a Space code or an exact unified-runtime stock criterion. */
function onEnter(): void {
  const q = query.value.trim()
  if (!q) return
  candidates.value = []
  if (mode.value === 'code') {
    emit('locate', q)
    return
  }
  const criteria: SpaceRuntimeInventoryLocateQuery = {}
  if (mode.value === 'material') criteria.materialNumber = q
  if (mode.value === 'lot') criteria.lotNumber = q
  if (mode.value === 'container') criteria.containerNumber = q
  emit('locate-stock', criteria)
}

function onBlur(): void {
  // Delay to allow mousedown on candidates to fire first
  setTimeout(() => { candidates.value = [] }, 200)
}

function onClear(): void {
  candidates.value = []
  clearTimeout(debounceTimer)
}

/** Click a candidate: set its code in the input and locate it. */
function onSelectCandidate(c: LocateResult): void {
  const code = c.locationCode ?? ''
  if (!code) return
  query.value = code
  candidates.value = []
  emit('locate', code)
}
</script>

<style scoped>
.search-box {
  position: relative;
}

.sb-mode {
  width: 100%;
  margin-bottom: 4px;
  background: rgba(10, 10, 25, 0.88);
  border: 1px solid rgba(79, 195, 247, 0.3);
  border-radius: 4px;
  color: #e0e0e0;
  font-size: 12px;
  padding: 2px 6px;
  cursor: pointer;
  outline: none;
}

.sb-mode:hover,
.sb-mode:focus {
  border-color: rgba(79, 195, 247, 0.7);
}

.search-box :deep(.el-input__wrapper) {
  background: rgba(10, 10, 25, 0.88);
  border-color: rgba(79, 195, 247, 0.3);
  box-shadow: none;
}

.search-box :deep(.el-input__wrapper):hover,
.search-box :deep(.el-input__wrapper.is-focus) {
  border-color: rgba(79, 195, 247, 0.7);
}

.search-box :deep(.el-input__inner) {
  color: #e0e0e0;
}

.search-box :deep(.el-input__inner::placeholder) {
  color: #546e7a;
}

.search-candidates {
  position: absolute;
  top: calc(100% + 2px);
  left: 0;
  right: 0;
  background: rgba(12, 12, 28, 0.96);
  border: 1px solid rgba(79, 195, 247, 0.25);
  border-radius: 4px;
  max-height: 200px;
  overflow-y: auto;
  z-index: 100;
}

.search-candidate-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  padding: 6px 10px;
  cursor: pointer;
  font-size: 12px;
  color: #b0bec5;
  transition: background 0.1s;
}

.search-candidate-item:hover {
  background: rgba(79, 195, 247, 0.1);
  color: #e0f7fa;
}

.search-candidate-code {
  font-family: monospace;
  font-size: 12px;
}

.search-candidate-tag {
  font-size: 10px;
  color: #ff8a65;
  flex-shrink: 0;
}
</style>
