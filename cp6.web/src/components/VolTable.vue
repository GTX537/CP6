<template>
  <div class="vol-table" :class="{ 'is-mobile': isMobile }">
    <!-- 顶部操作区：搜索 + 按钮 -->
    <div class="table-header" :class="{ 'is-mobile': isMobile }">
      <div class="search-area">
        <el-input
          v-model="keyword"
          :placeholder="$t('table.search')"
          clearable
          :prefix-icon="Search"
          :style="{ width: isMobile ? '100%' : '250px' }"
          @keyup.enter="search"
        >
          <template v-if="!isMobile" #append>
            <el-button @click="search" :icon="Search" />
          </template>
        </el-input>
      </div>
      <!-- 桌面端：新增/删除按钮在顶部 -->
      <div v-if="!isMobile" class="button-area">
        <el-button v-permission="addPermission" type="primary" :icon="Plus" @click="handleAdd">{{ $t('table.add') }}</el-button>
        <el-button v-permission="deletePermission" type="danger" :icon="Delete" @click="handleBatchDelete">{{ $t('table.delete') }}</el-button>
      </div>
    </div>

    <!-- 桌面端：表格 -->
    <el-table
      v-if="!isMobile"
      :data="tableData"
      v-loading="loading"
      @selection-change="handleSelectionChange"
      stripe
      border
      style="width: 100%"
    >
      <el-table-column type="selection" width="50" />
      <el-table-column
        v-for="col in columns.filter(c => !c.tableHidden)"
        :key="col.prop"
        :prop="col.prop"
        :label="col.label"
        :width="col.width"
        show-overflow-tooltip
      >
        <template #default="{ row }">
          <el-tag v-if="col.type === 'switch'" :type="row[col.prop] ? 'success' : 'info'">
            {{ row[col.prop] ? $t('common.yes') : $t('common.no') }}
          </el-tag>
          <span v-else-if="col.options">
            {{ col.options.find(o => o.value === row[col.prop])?.label || row[col.prop] }}
          </span>
          <span v-else>{{ row[col.prop] }}</span>
        </template>
      </el-table-column>
      <el-table-column :label="$t('table.operation')" :width="$slots['extra-actions'] ? 220 : 150">
        <template #default="{ row }">
          <slot name="extra-actions" :row="row" />
          <el-button v-permission="editPermission" link type="primary" @click="handleEdit(row)">{{ $t('table.edit') }}</el-button>
          <el-button v-permission="deletePermission" link type="danger" @click="handleDelete(row)">{{ $t('table.delete') }}</el-button>
        </template>
      </el-table-column>
    </el-table>

    <!-- 手机端：卡片列表 -->
    <div v-else class="card-list" v-loading="loading">
      <!-- 选择模式工具栏 -->
      <div v-if="selectionMode" class="selection-bar">
        <el-checkbox
          :model-value="allSelected"
          :indeterminate="someSelected && !allSelected"
          @change="toggleAll"
        >{{ $t('table.selectAll') || '全选' }}</el-checkbox>
        <span class="selection-count">{{ selectedRows.length }} / {{ tableData.length }}</span>
        <div class="selection-actions">
          <el-button size="small" @click="exitSelectionMode">{{ $t('table.cancel') }}</el-button>
          <el-button v-permission="deletePermission" size="small" type="danger" :disabled="!selectedRows.length" @click="handleBatchDelete">
            {{ $t('table.delete') }}
          </el-button>
        </div>
      </div>

      <el-empty v-if="!tableData.length && !loading" :image-size="80" :description="$t('table.noData') || '暂无数据'" />

      <div
        v-for="row in tableData"
        :key="row[idField]"
        class="row-card"
        :class="{ 'is-selected': selectionMode && isRowSelected(row) }"
        @click="onCardClick(row, $event)"
      >
        <!-- 顶部：标题（首列）+ 操作 -->
        <div class="row-card-top">
          <el-checkbox
            v-if="selectionMode"
            :model-value="isRowSelected(row)"
            class="row-checkbox"
            @click.stop
            @change="(v: any) => toggleRowSelected(row, !!v)"
          />
          <div class="row-card-title">{{ row[firstVisibleCol.prop] || '—' }}</div>

          <!-- 状态字段（switch 类型）显示在右上角 -->
          <el-tag
            v-if="statusCol"
            :type="row[statusCol.prop] ? 'success' : 'info'"
            size="small"
            class="row-status-tag"
          >
            {{ row[statusCol.prop] ? $t('common.yes') : $t('common.no') }}
          </el-tag>

          <!-- 更多菜单 -->
          <el-dropdown
            v-if="!selectionMode && (hasDefaultMobileActions || $slots['mobile-extra-actions'])"
            trigger="click"
            placement="bottom-end"
            @command="(cmd: string) => onCardCommand(cmd, row)"
            @click.stop
          >
            <el-button link class="more-btn" @click.stop>
              <el-icon :size="20"><MoreFilled /></el-icon>
            </el-button>
            <template #dropdown>
              <el-dropdown-menu>
                <el-dropdown-item v-if="canUsePermission(editPermission)" command="edit" :icon="Edit">{{ $t('table.edit') }}</el-dropdown-item>
                <slot name="mobile-extra-actions" :row="row" />
                <el-dropdown-item v-if="canUsePermission(deletePermission)" command="select" :icon="Select" divided>{{ $t('table.batchSelect') || '批量选择' }}</el-dropdown-item>
                <el-dropdown-item v-if="canUsePermission(deletePermission)" command="delete" :icon="Delete">
                  <span style="color: #f56c6c;">{{ $t('table.delete') }}</span>
                </el-dropdown-item>
              </el-dropdown-menu>
            </template>
          </el-dropdown>
        </div>

        <!-- 字段列表 -->
        <div class="row-card-body">
          <div
            v-for="col in cardColumns"
            :key="col.prop"
            class="row-card-field"
          >
            <span class="row-card-label">{{ col.label }}</span>
            <span class="row-card-value">
              <template v-if="col.options">
                {{ col.options.find(o => o.value === row[col.prop])?.label || row[col.prop] }}
              </template>
              <template v-else>{{ row[col.prop] || '—' }}</template>
            </span>
          </div>
        </div>

        <!-- 自定义额外操作（保持桌面端 slot 兼容） -->
        <div v-if="$slots['extra-actions'] && !selectionMode" class="row-card-extra-slot">
          <slot name="extra-actions" :row="row" />
        </div>
      </div>
    </div>

    <!-- 分页 -->
    <div class="pagination" v-if="total > 0">
      <el-pagination
        v-model:current-page="page"
        v-model:page-size="pageSize"
        :total="total"
        :page-sizes="[10, 20, 50]"
        :layout="isMobile ? 'prev, pager, next' : 'total, sizes, prev, pager, next'"
        :pager-count="isMobile ? 5 : 7"
        :small="isMobile"
        background
        @current-change="loadData"
        @size-change="loadData"
      />
    </div>

    <!-- 手机端：浮动新增按钮 -->
    <button
      v-if="isMobile && !selectionMode"
      v-permission="addPermission"
      class="cp6-fab"
      :aria-label="$t('table.add')"
      @click="handleAdd"
    >
      <el-icon><Plus /></el-icon>
    </button>

    <!-- 编辑弹窗 -->
    <VolForm
      v-model:visible="formVisible"
      :title="formTitle"
      :columns="columns"
      :form-data="formData"
      @submit="handleSubmit"
    />
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { Search, Plus, Delete, MoreFilled, Edit, Select } from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import VolForm from './VolForm.vue'
import { useBreakpoint } from '@/composables/useBreakpoint'
import { usePermissionStore } from '@/stores/permission'

const { t } = useI18n()
const { isMobile } = useBreakpoint()
const permissionStore = usePermissionStore()

// 定义列配置的类型
export interface ColumnConfig {
  prop: string
  label: string
  width?: number
  type?: string
  formType?: string
  required?: boolean
  hidden?: boolean
  options?: { label: string; value: any }[]
  tableHidden?: boolean
}

const props = withDefaults(defineProps<{
  columns: ColumnConfig[]
  idField?: string
  addPermission?: string
  editPermission?: string
  deletePermission?: string
  api: {
    getList: (params: any) => Promise<any>
    add: (data: any) => Promise<any>
    update: (data: any) => Promise<any>
    del: (ids: any[]) => Promise<any>
  }
}>(), {
  idField: 'id'
})

const tableData = ref<any[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(10)
const keyword = ref('')
const loading = ref(false)
const selectedRows = ref<any[]>([])
const selectionMode = ref(false)

const formVisible = ref(false)
const formTitle = ref('')
const formData = ref<any>({})

const visibleCols = computed(() => props.columns.filter(c => !c.tableHidden))
const firstVisibleCol = computed(() => visibleCols.value[0] || { prop: props.idField, label: '' })
// status 列：取第一个 type='switch' 的列
const statusCol = computed(() => visibleCols.value.find(c => c.type === 'switch'))
// 卡片正文显示的字段：去掉首列（标题）和 status 列
const cardColumns = computed(() =>
  visibleCols.value.filter(c => c.prop !== firstVisibleCol.value.prop && c.prop !== statusCol.value?.prop)
)

const allSelected = computed(() =>
  tableData.value.length > 0 && selectedRows.value.length === tableData.value.length
)
const someSelected = computed(() => selectedRows.value.length > 0)
const hasDefaultMobileActions = computed(() =>
  canUsePermission(props.editPermission) || canUsePermission(props.deletePermission)
)

function canUsePermission(key?: string) {
  return !key || !permissionStore.loaded || permissionStore.has(key)
}

function isRowSelected(row: any) {
  return selectedRows.value.some(r => r[props.idField] === row[props.idField])
}
function toggleRowSelected(row: any, checked: boolean) {
  const id = row[props.idField]
  if (checked) {
    if (!isRowSelected(row)) selectedRows.value.push(row)
  } else {
    selectedRows.value = selectedRows.value.filter(r => r[props.idField] !== id)
  }
}
function toggleAll(checked: boolean) {
  selectedRows.value = checked ? [...tableData.value] : []
}
function exitSelectionMode() {
  selectionMode.value = false
  selectedRows.value = []
}
function onCardClick(row: any, _ev: Event) {
  if (selectionMode.value) {
    toggleRowSelected(row, !isRowSelected(row))
  } else {
    if (canUsePermission(props.editPermission)) {
      handleEdit(row)
    }
  }
}
async function onCardCommand(cmd: string, row: any) {
  if (cmd === 'edit' && canUsePermission(props.editPermission)) handleEdit(row)
  else if (cmd === 'delete' && canUsePermission(props.deletePermission)) handleDelete(row)
  else if (cmd === 'select' && canUsePermission(props.deletePermission)) {
    selectionMode.value = true
    toggleRowSelected(row, true)
  }
}

async function loadData() {
  loading.value = true
  try {
    const res = await props.api.getList({ page: page.value, pageSize: pageSize.value, keyword: keyword.value })
    tableData.value = res.rows
    total.value = res.total
  } finally {
    loading.value = false
  }
}

function search() {
  page.value = 1
  loadData()
}

function handleSelectionChange(rows: any[]) {
  selectedRows.value = rows
}

const isEdit = ref(false)

async function handleSubmit(data: any) {
  if (isEdit.value) {
    await props.api.update(data)
    ElMessage.success(t('table.editSuccess'))
  } else {
    await props.api.add(data)
    ElMessage.success(t('table.addSuccess'))
  }
  formVisible.value = false
  loadData()
}

function handleAdd() {
  formTitle.value = t('table.add')
  formData.value = {}
  isEdit.value = false
  formVisible.value = true
}

function handleEdit(row: any) {
  formTitle.value = t('table.edit')
  formData.value = { ...row }
  isEdit.value = true
  formVisible.value = true
}

async function handleDelete(row: any) {
  await ElMessageBox.confirm(t('table.confirmDelete'), t('table.tip'), { type: 'warning' })
  await props.api.del([row[props.idField]])
  ElMessage.success(t('table.deleteSuccess'))
  loadData()
}

async function handleBatchDelete() {
  if (selectedRows.value.length === 0) {
    ElMessage.warning(t('table.selectFirst'))
    return
  }
  await ElMessageBox.confirm(t('table.confirmBatchDelete', { count: selectedRows.value.length }), t('table.tip'), { type: 'warning' })
  await props.api.del(selectedRows.value.map((r) => r[props.idField]))
  ElMessage.success(t('table.deleteSuccess'))
  exitSelectionMode()
  loadData()
}

onMounted(() => loadData())
</script>

<style scoped>
.vol-table.is-mobile {
  padding-bottom: 80px; /* 给 FAB 留空间 */
}

.table-header {
  display: flex;
  justify-content: space-between;
  margin-bottom: 16px;
  gap: 12px;
}
.table-header.is-mobile {
  position: sticky;
  top: 0;
  z-index: 5;
  background: #f5f7fa;
  margin: -12px -12px 12px;
  padding: 10px 12px;
  border-bottom: 1px solid #ebeef5;
}
.pagination {
  display: flex;
  justify-content: flex-end;
  margin-top: 16px;
}

/* 卡片列表 */
.card-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
  min-height: 120px;
}

.row-card {
  background: #fff;
  border-radius: 10px;
  padding: 14px;
  box-shadow: 0 1px 2px rgba(0,0,0,0.04);
  border: 1px solid #ebeef5;
  cursor: pointer;
  transition: box-shadow 0.15s ease, transform 0.1s ease, border-color 0.15s ease;
  position: relative;
}
.row-card:active {
  transform: scale(0.995);
  box-shadow: 0 2px 8px rgba(64, 158, 255, 0.15);
}
.row-card.is-selected {
  border-color: #409eff;
  background: #ecf5ff;
}

.row-card-top {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 10px;
}
.row-checkbox {
  flex-shrink: 0;
}
.row-card-title {
  font-weight: 600;
  font-size: 16px;
  color: #303133;
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  line-height: 1.4;
}
.row-status-tag {
  flex-shrink: 0;
}
.more-btn {
  padding: 4px 8px;
  margin: -4px -8px -4px 0;
}

.row-card-body {
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.row-card-field {
  display: flex;
  gap: 10px;
  font-size: 13px;
  line-height: 1.5;
}
.row-card-label {
  color: #909399;
  min-width: 80px;
  flex-shrink: 0;
}
.row-card-value {
  color: #303133;
  word-break: break-all;
  flex: 1;
  font-size: 14px;
}
.row-card-extra-slot {
  margin-top: 10px;
  padding-top: 10px;
  border-top: 1px dashed #ebeef5;
  display: flex;
  justify-content: flex-end;
  gap: 6px;
}

/* 选择模式工具栏 */
.selection-bar {
  position: sticky;
  top: 60px;
  z-index: 4;
  background: #fff;
  border: 1px solid #ebeef5;
  border-radius: 10px;
  padding: 10px 14px;
  display: flex;
  align-items: center;
  gap: 12px;
  box-shadow: 0 2px 8px rgba(0,0,0,0.04);
  margin-bottom: 4px;
}
.selection-count {
  color: #606266;
  font-size: 13px;
  flex: 1;
}
.selection-actions {
  display: flex;
  gap: 6px;
}

@media (max-width: 767px) {
  .pagination {
    justify-content: center;
  }
}
</style>
