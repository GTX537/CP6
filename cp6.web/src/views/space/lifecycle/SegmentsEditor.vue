<!--
  SegmentsEditor —— 编码规则「段」编辑器（波3 生命周期）。CodeRuleVO.segments 的 v-model 子组件。
  列内编辑：key/name/source(12 值域分组下拉)/width/pad/start/step/sep/upper/fixedValue/optional。
  字段启用联动（codeRuleValidate）：width·pad·start·step 仅序号源；upper 仅码源(fixed 无效)；
  fixedValue 仅 fixed。切到巷道源(aisle-*)自动置 optional=true。行操作 上移/下移/删除；末尾「段追加」。
  页脚黄条：本地镜像校验 E-303/305/306（不阻断保存——权威口径以后端 preview.precheck 为准）。
  内部持本地 list（自 modelValue 同步 + 变更即 emit），既支持父级回写亦支持脱离父级独立渲染。
-->
<template>
  <div class="seg-editor">
    <div class="seg-bar">
      <span class="seg-title">{{ t('space.rule.seg.title') }}</span>
      <el-button size="small" @click="addSeg">{{ t('space.rule.seg.add') }}</el-button>
    </div>

    <el-table :data="list" size="small" border>
      <el-table-column type="index" width="44" />
      <el-table-column :label="t('space.rule.seg.key')" width="120">
        <template #default="{ row }">
          <el-input v-model="row.key" size="small" maxlength="20" @change="emitChange" />
        </template>
      </el-table-column>
      <el-table-column :label="t('space.rule.seg.name')" width="120">
        <template #default="{ row }">
          <el-input v-model="row.name" size="small" maxlength="30" @change="emitChange" />
        </template>
      </el-table-column>
      <el-table-column :label="t('space.rule.seg.source')" width="160">
        <template #default="{ row }">
          <el-select v-model="row.source" size="small" @change="onSourceChange(row)">
            <el-option-group v-for="g in sourceGroups" :key="g.label" :label="g.label">
              <el-option v-for="s in g.items" :key="s" :label="srcLabel(s)" :value="s" />
            </el-option-group>
          </el-select>
        </template>
      </el-table-column>
      <el-table-column :label="t('space.rule.seg.width')" width="90">
        <template #default="{ row }">
          <el-input-number v-model="row.width" size="small" :min="0" :max="20" :precision="0"
            controls-position="right" :disabled="!seqFieldsEnabled(row.source)" @change="emitChange" />
        </template>
      </el-table-column>
      <el-table-column :label="t('space.rule.seg.pad')" width="72">
        <template #default="{ row }">
          <el-input v-model="row.pad" size="small" maxlength="1"
            :disabled="!seqFieldsEnabled(row.source)" @change="emitChange" />
        </template>
      </el-table-column>
      <el-table-column :label="t('space.rule.seg.start')" width="90">
        <template #default="{ row }">
          <el-input-number v-model="row.start" size="small" :precision="0" controls-position="right"
            :disabled="!seqFieldsEnabled(row.source)" @change="emitChange" />
        </template>
      </el-table-column>
      <el-table-column :label="t('space.rule.seg.step')" width="90">
        <template #default="{ row }">
          <el-input-number v-model="row.step" size="small" :precision="0" controls-position="right"
            :disabled="!seqFieldsEnabled(row.source)" @change="emitChange" />
        </template>
      </el-table-column>
      <el-table-column :label="t('space.rule.seg.sep')" width="72">
        <template #default="{ row }">
          <el-input v-model="row.sep" size="small" maxlength="1" @change="emitChange" />
        </template>
      </el-table-column>
      <el-table-column :label="t('space.rule.seg.upper')" width="72" align="center">
        <template #default="{ row }">
          <el-checkbox v-model="row.upper" :disabled="!upperEnabled(row.source)" @change="emitChange" />
        </template>
      </el-table-column>
      <el-table-column :label="t('space.rule.seg.fixedValue')" width="130">
        <template #default="{ row }">
          <el-input v-model="row.fixedValue" size="small" maxlength="20"
            :disabled="!fixedValueEnabled(row.source)" @change="emitChange" />
        </template>
      </el-table-column>
      <el-table-column :label="t('space.rule.seg.optional')" width="80" align="center">
        <template #default="{ row }">
          <el-checkbox v-model="row.optional" @change="emitChange" />
        </template>
      </el-table-column>
      <el-table-column :label="t('space.rule.seg.op')" width="120" fixed="right">
        <template #default="{ $index }">
          <el-button link size="small" :disabled="$index === 0" @click="move($index, -1)">↑</el-button>
          <el-button link size="small" :disabled="$index === list.length - 1" @click="move($index, 1)">↓</el-button>
          <el-button link type="danger" size="small" @click="remove($index)">{{ t('space.rule.seg.del') }}</el-button>
        </template>
      </el-table-column>

      <template #empty>
        <span class="seg-empty">{{ t('space.rule.seg.empty') }}</span>
      </template>
    </el-table>

    <div v-if="warnings.length" class="seg-warn">
      <span class="seg-warn-ico">⚠</span>
      <ul>
        <li v-for="code in warnings" :key="code">{{ t(`space.rule.err.${code}`) }}</li>
      </ul>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import type { CodeSegmentDef } from '@/types/space/scene'
import {
  FIXED_SOURCE, CODE_SOURCES, SEQ_SOURCES, newSegment, validateSegmentsLocal,
  seqFieldsEnabled, upperEnabled, fixedValueEnabled, isAisleSource,
} from './codeRuleValidate'

const props = defineProps<{ modelValue: CodeSegmentDef[] }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: CodeSegmentDef[]): void }>()
const { t } = useI18n()

// 本地工作副本：自 modelValue 同步；变更即 emit。父级回写同一引用时 watch 跳过（防抖动）。
const list = ref<CodeSegmentDef[]>([...(props.modelValue || [])])
watch(
  () => props.modelValue,
  (nv) => { if (nv !== list.value) list.value = [...(nv || [])] },
)

function emitChange() { emit('update:modelValue', list.value) }

// el-select 含 t() 的选项 → computed 包裹（避免运行期告警/失响应）
const sourceGroups = computed(() => [
  { label: t('space.rule.src.group.fixed'), items: [FIXED_SOURCE] },
  { label: t('space.rule.src.group.code'), items: [...CODE_SOURCES] },
  { label: t('space.rule.src.group.seq'), items: [...SEQ_SOURCES] },
])
const srcLabel = (s: string) => t(`space.rule.src.${s}`)

const warnings = computed(() => validateSegmentsLocal(list.value))

function onSourceChange(row: CodeSegmentDef) {
  if (isAisleSource(row.source)) row.optional = true // 巷道段 optional 应为 true（E-305）
  emitChange()
}
function addSeg() { list.value.push(newSegment()); emitChange() }
function remove(i: number) { list.value.splice(i, 1); emitChange() }
function move(i: number, dir: -1 | 1) {
  const j = i + dir
  const arr = list.value
  const a = arr[i]
  const b = arr[j]
  if (a === undefined || b === undefined) return // 越界守卫（同时满足 noUncheckedIndexedAccess）
  arr[i] = b
  arr[j] = a
  emitChange()
}

defineExpose({ seqFieldsEnabled, upperEnabled, fixedValueEnabled })
</script>

<style scoped>
.seg-editor { display: flex; flex-direction: column; gap: 10px; }
.seg-bar { display: flex; align-items: center; justify-content: space-between; }
.seg-title { font-weight: 700; color: var(--cp-ink); }
.seg-empty { color: var(--cp-muted); font-size: var(--cp-fs-sm); }
.seg-warn { display: flex; gap: 8px; padding: 8px 12px; border-radius: var(--cp-r-sm, 6px);
  background: #fff7e6; border: 1px solid #ffd591; color: #ad6800; font-size: var(--cp-fs-sm); }
.seg-warn-ico { line-height: 1.5; }
.seg-warn ul { margin: 0; padding-left: 4px; list-style: none; }
</style>
