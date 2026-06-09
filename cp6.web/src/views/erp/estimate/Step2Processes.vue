<template>
  <el-card shadow="never">
    <template #header>
      <div class="card-header">
        <span class="title">工程明細</span>
        <div class="header-actions">
          <el-button type="primary" :icon="Plus" size="small" :disabled="isPageReadOnly" @click="onAdd">
            行追加
          </el-button>
          <el-button type="danger" :icon="Delete" size="small" :disabled="isPageReadOnly || !selected.length" @click="onRemoveSelected">
            選択削除
          </el-button>
          <el-divider direction="vertical" />
          <el-button size="small" @click="openRecycleA" :disabled="isPageReadOnly">リサイクル法A</el-button>
          <el-button size="small" @click="openRecycleB" :disabled="isPageReadOnly">リサイクル法B</el-button>
          <el-button size="small" @click="openRecycleC" :disabled="isPageReadOnly">リサイクル法C</el-button>
        </div>
      </div>
    </template>

    <!-- 工程表 -->
    <el-table
      :data="rows"
      border
      stripe
      size="small"
      style="width: 100%"
      @selection-change="onSelectionChange"
    >
      <el-table-column type="selection" width="40" />
      <el-table-column prop="seqNo" label="順番" width="60" align="center" />
      <el-table-column label="工程" width="160">
        <template #default="{ row }">
          <el-select v-model="row.processCd" :disabled="isPageReadOnly" clearable size="small" @change="onProcessChange(row)">
            <el-option v-for="o in processCodes" :key="o.code" :value="o.code" :label="`${o.code} ${o.name}`" />
          </el-select>
        </template>
      </el-table-column>
      <el-table-column label="作業" width="160">
        <template #default="{ row }">
          <el-input v-model="row.taskName" :disabled="isPageReadOnly" size="small" />
        </template>
      </el-table-column>
      <el-table-column label="WG" width="100">
        <template #default="{ row }">
          <el-input v-model="row.wgCd" :disabled="isPageReadOnly" size="small" />
        </template>
      </el-table-column>
      <el-table-column label="製造拠点" width="100">
        <template #default="{ row }">
          <el-input v-model="row.mfgLocation" :disabled="isPageReadOnly" size="small" />
        </template>
      </el-table-column>
      <el-table-column v-for="i in 3" :key="`spec${i}`" :label="`規格${i}`" width="180">
        <template #default="{ row }">
          <div class="spec-cell">
            <el-input
              :model-value="(row as any)[`spec${i}Label`]"
              @update:model-value="(v: string) => ((row as any)[`spec${i}Label`] = v)"
              :disabled="isPageReadOnly"
              size="small"
              placeholder="Label"
              style="width: 50%"
            />
            <el-input
              :model-value="(row as any)[`spec${i}Val`]"
              @update:model-value="(v: string) => ((row as any)[`spec${i}Val`] = v)"
              :disabled="isPageReadOnly"
              size="small"
              placeholder="Value"
              style="width: 50%"
            />
          </div>
        </template>
      </el-table-column>
      <el-table-column label="版No" width="100">
        <template #default="{ row }">
          <el-input v-model="row.plateNo" :disabled="isPageReadOnly" size="small" />
        </template>
      </el-table-column>
      <el-table-column label="工程備考" min-width="220">
        <template #default="{ row }">
          <el-input v-model="row.procNote1" :disabled="isPageReadOnly" size="small" placeholder="備考1" />
          <el-input v-model="row.procNote2" :disabled="isPageReadOnly" size="small" placeholder="備考2" style="margin-top: 4px" />
        </template>
      </el-table-column>
      <el-table-column label="操作" width="100" fixed="right">
        <template #default="{ row, $index }">
          <el-button link type="danger" size="small" :disabled="isPageReadOnly" @click="onRemoveRow($index)">削除</el-button>
        </template>
      </el-table-column>
    </el-table>

    <div v-if="!rows.length" class="empty-hint">
      <el-empty description="まだ工程がありません。『行追加』で追加してください" :image-size="80" />
    </div>

    <!-- リサイクル法 弹窗 -->
    <RecycleLawDialog v-model="recycleVisible" :type="recycleType" @confirm="onRecycleConfirm" />
  </el-card>
</template>

<script setup lang="ts">
import { ref, onMounted, watch } from 'vue'
import { ElMessage } from 'element-plus'
import { Plus, Delete } from '@element-plus/icons-vue'
import { storeToRefs } from 'pinia'
import { useEstimateStore } from '@/stores/estimate'
import { useFieldControl } from '@/composables/useFieldControl'
import { masterApi } from '@/api/erp/master'
import type { EstimateCalcProcessDto, MasterGenericCode } from '@/types/erp/estimateCalc'
import RecycleLawDialog from '@/components/estimate/RecycleLawDialog.vue'

const store = useEstimateStore()
const { processRows } = storeToRefs(store)
const { isPageReadOnly } = useFieldControl()

// 与 store 的 processRows 共享引用
const rows = processRows
const selected = ref<EstimateCalcProcessDto[]>([])

const processCodes = ref<MasterGenericCode[]>([])

function newRow(seq: number): EstimateCalcProcessDto {
  return {
    seqNo: seq,
    processCd: '',
    processName: '',
    taskCd: '',
    taskName: '',
    wgCd: '',
    mfgLocation: '',
    spec1Label: '',
    spec1Val: '',
    spec2Label: '',
    spec2Val: '',
    spec3Label: '',
    spec3Val: '',
    plateNo: '',
    procNote1: '',
    procNote2: '',
  }
}

function onAdd() {
  const maxSeq = rows.value.reduce((m, r) => Math.max(m, r.seqNo), 0)
  rows.value.push(newRow(maxSeq + 1))
  store.markDirty()
}

function onRemoveRow(idx: number) {
  rows.value.splice(idx, 1)
  resequence()
  store.markDirty()
}

function onSelectionChange(sel: EstimateCalcProcessDto[]) {
  selected.value = sel
}

function onRemoveSelected() {
  if (!selected.value.length) return
  const toRemove = new Set(selected.value)
  rows.value = rows.value.filter((r) => !toRemove.has(r))
  resequence()
  store.markDirty()
}

/** 重新编号 1..N */
function resequence() {
  rows.value.forEach((r, i) => (r.seqNo = i + 1))
}

/** 工程代码变更时回填工程名 */
function onProcessChange(row: EstimateCalcProcessDto) {
  const m = processCodes.value.find((c) => c.code === row.processCd)
  row.processName = m?.name ?? ''
  store.markDirty()
}

// ============ リサイクル法 对接 ============
const recycleVisible = ref(false)
const recycleType = ref<'A' | 'B' | 'C'>('A')

function openRecycleA() {
  recycleType.value = 'A'
  recycleVisible.value = true
}
function openRecycleB() {
  recycleType.value = 'B'
  recycleVisible.value = true
}
function openRecycleC() {
  recycleType.value = 'C'
  recycleVisible.value = true
}

/** 弹窗确认后写回 store + 添加工程行 */
function onRecycleConfirm(payload: any) {
  store.recycleResult[recycleType.value.toLowerCase() as 'a' | 'b' | 'c'] = payload
  // 将弹窗选择的项目追加为工程行（示意）
  if (payload?.items?.length) {
    payload.items.forEach((item: any) => {
      const maxSeq = rows.value.reduce((m, r) => Math.max(m, r.seqNo), 0)
      const r = newRow(maxSeq + 1)
      r.processName = `ﾘｻｲｸﾙ法${recycleType.value} - ${item.name}`
      r.procNote1 = item.note ?? ''
      rows.value.push(r)
    })
    store.markDirty()
  }
  ElMessage.success(`リサイクル法${recycleType.value} 適用完了`)
}

onMounted(async () => {
  const res = await masterApi.getGenericCodes('M038')
  processCodes.value = res.data ?? []
})

// 跨 Step 切换时，确保 seqNo 连续
watch(rows, resequence, { deep: false })
</script>

<style scoped>
.card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}
.title {
  font-weight: 600;
  color: #409eff;
}
.header-actions {
  display: flex;
  align-items: center;
  gap: 6px;
}
.spec-cell {
  display: flex;
  gap: 4px;
}
.empty-hint {
  padding: 12px 0;
}
</style>
