<script setup lang="ts">
import { computed, reactive, watch } from 'vue'
import type {
  AlignmentMode,
  DistributionMode,
  GenerateRackArrayPayload,
} from '@/modules/space-design/commands/editorBatchCommands'

const props = defineProps<{
  selectedCount: number
  selectionBounds?: string
  selectedRackCode?: string
  busy?: boolean
  readonly?: boolean
  canUndo?: boolean
  canRedo?: boolean
}>()

const emit = defineEmits<{
  align: [mode: AlignmentMode]
  distribute: [mode: DistributionMode]
  rotate: [degrees: number]
  remove: []
  array: [payload: GenerateRackArrayPayload]
  undo: []
  redo: []
}>()

const array = reactive<GenerateRackArrayPayload>({
  rows: 1,
  columns: 2,
  rowGap: 1000,
  columnGap: 500,
  staggerOffset: 0,
  codePrefix: 'R-',
  startNumber: 1,
  codeDigits: 3,
})

watch(
  () => props.selectedRackCode,
  (rackCode) => {
    if (!rackCode) return
    const match = /^(.*?)(\d+)$/.exec(rackCode)
    array.codePrefix = match?.[1] || `${rackCode}-`
    array.startNumber = match ? Number(match[2]) + 1 : 1
    array.codeDigits = Math.max(1, Math.min(8, match?.[2]?.length ?? 3))
  },
  { immediate: true },
)

const generatedCount = computed(
  () => Math.max(0, array.rows * array.columns - 1),
)
const validArray = computed(
  () =>
    generatedCount.value >= 1 &&
    generatedCount.value <= 99 &&
    array.rows >= 1 &&
    array.columns >= 1 &&
    array.rowGap >= 0 &&
    array.columnGap >= 0 &&
    array.staggerOffset >= 0 &&
    Boolean(array.codePrefix.trim()),
)
const codePreview = computed(() => {
  if (!validArray.value) return '—'
  const format = (value: number) =>
    `${array.codePrefix}${String(value).padStart(array.codeDigits, '0')}`
  return `${format(array.startNumber)} … ${format(
    array.startNumber + generatedCount.value - 1,
  )}`
})
const disabled = computed(() => props.busy || props.readonly)
</script>

<template>
  <section class="batch-tools" data-test="design-batch-tools">
    <div class="selection-summary">
      <strong>批量编辑</strong>
      <el-tag :type="selectedCount ? 'primary' : 'info'">
        已选 {{ selectedCount }} 个
      </el-tag>
      <span class="hint">Ctrl / Shift 点击或拖框多选</span>
      <span v-if="selectionBounds" class="bounds-preview">
        写入前边界 {{ selectionBounds }}
      </span>
    </div>

    <div class="button-group">
      <el-button
        v-permission="'space:model:edit'"
        :disabled="disabled || !canUndo"
        @click="emit('undo')"
      >
        撤销
      </el-button>
      <el-button
        v-permission="'space:model:edit'"
        :disabled="disabled || !canRedo"
        @click="emit('redo')"
      >
        重做
      </el-button>
    </div>

    <div class="button-group">
      <span>对齐</span>
      <el-button
        v-for="item in [
          ['left', '左'],
          ['centerX', '水平中'],
          ['right', '右'],
          ['top', '上'],
          ['centerY', '垂直中'],
          ['bottom', '下'],
        ]"
        :key="item[0]"
        size="small"
        :disabled="disabled || selectedCount < 2"
        @click="emit('align', item[0] as AlignmentMode)"
      >
        {{ item[1] }}
      </el-button>
    </div>

    <div class="button-group">
      <span>等距</span>
      <el-button
        size="small"
        :disabled="disabled || selectedCount < 3"
        @click="emit('distribute', 'horizontal')"
      >
        水平
      </el-button>
      <el-button
        size="small"
        :disabled="disabled || selectedCount < 3"
        @click="emit('distribute', 'vertical')"
      >
        垂直
      </el-button>
      <el-button
        size="small"
        :disabled="disabled || selectedCount < 1"
        @click="emit('rotate', -90)"
      >
        左转 90°
      </el-button>
      <el-button
        size="small"
        :disabled="disabled || selectedCount < 1"
        @click="emit('rotate', 90)"
      >
        右转 90°
      </el-button>
      <el-button
        type="danger"
        plain
        size="small"
        :disabled="disabled || selectedCount < 1"
        @click="emit('remove')"
      >
        删除
      </el-button>
    </div>

    <details class="array-tools" :class="{ unavailable: !selectedRackCode }">
      <summary>货架阵列</summary>
      <div class="array-fields">
        <label>行 <el-input-number v-model="array.rows" :min="1" :max="100" /></label>
        <label>列 <el-input-number v-model="array.columns" :min="1" :max="100" /></label>
        <label>行距 mm <el-input-number v-model="array.rowGap" :min="0" :step="100" /></label>
        <label>列距 mm <el-input-number v-model="array.columnGap" :min="0" :step="100" /></label>
        <label>错列 mm <el-input-number v-model="array.staggerOffset" :min="0" :step="100" /></label>
        <label>编码前缀 <el-input v-model="array.codePrefix" maxlength="90" /></label>
        <label>起号 <el-input-number v-model="array.startNumber" :min="0" /></label>
        <label>位数 <el-input-number v-model="array.codeDigits" :min="1" :max="8" /></label>
      </div>
      <div class="array-preview">
        模板计入阵列；将新增 {{ generatedCount }} 个货架，编码 {{ codePreview }}
        <el-button
          v-permission="'space:model:edit'"
          type="primary"
          size="small"
          :disabled="disabled || !selectedRackCode || !validArray"
          @click="emit('array', { ...array, codePrefix: array.codePrefix.trim() })"
        >
          生成阵列
        </el-button>
      </div>
    </details>
  </section>
</template>

<style scoped>
.batch-tools {
  display:flex;
  flex-direction:column;
  align-items:stretch;
  gap: 12px;
  padding:16px;
  color:var(--space-studio-text, #0f172a);
  background:var(--space-studio-panel, #f8fafc);
}

.selection-summary,
.button-group,
.array-preview {
  display: flex;
  flex-wrap:wrap;
  align-items: center;
  gap: 6px;
}

.hint {
  color:var(--space-studio-muted, #64748b);
  font-size:13px;
}

.bounds-preview {
  color: #0f766e;
  font-family: monospace;
  font-size:13px;
}

.button-group > span {
  color:var(--space-studio-muted, #475569);
  font-size:13px;
}

.array-tools {
  padding: 5px 8px;
  border:1px solid var(--space-studio-border, #cbd5e1);
  border-radius: 6px;
}

.array-tools.unavailable {
  opacity: 0.65;
}

.array-tools summary {
  cursor: pointer;
  font-size:14px;
  font-weight: 650;
}

.array-fields {
  display: grid;
  grid-template-columns:1fr;
  gap: 6px;
  margin-top: 8px;
}

.array-fields label {
  display: flex;
  align-items: center;
  gap: 4px;
  justify-content:space-between;
  color:var(--space-studio-muted, #475569);
  font-size:13px;
}

.array-preview {
  justify-content:flex-start;
  margin-top: 6px;
  color:var(--space-studio-muted, #334155);
  font-size:13px;
}
</style>
