<template>
  <div class="search-box">
    <el-input
      v-model="query"
      :placeholder="t('搜索库位编码')"
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
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { Search } from '@element-plus/icons-vue'
import { locateApi } from '@/api/space/locate'
import type { LocateResult } from '@/types/space/viewer'

const emit = defineEmits<{ (e: 'locate', code: string): void }>()

const { t } = useI18n()
const query = ref('')
const candidates = ref<LocateResult[]>([])
let debounceTimer = 0

function onInput(): void {
  clearTimeout(debounceTimer)
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

/** Enter key: treat current input as exact code and locate immediately. */
function onEnter(): void {
  const q = query.value.trim()
  if (!q) return
  candidates.value = []
  emit('locate', q)
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
