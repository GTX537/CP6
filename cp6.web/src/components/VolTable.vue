<template>
  <div class="vol-table">
    <!-- 顶部操作区：搜索 + 按钮 -->
    <div class="table-header">
      <div class="search-area">
        <el-input
          v-model="keyword"
          :placeholder="$t('table.search')"
          clearable
          style="width: 250px"
          @keyup.enter="search"
        >
          <template #append>
            <el-button @click="search" :icon="Search" />
          </template>
        </el-input>
      </div>
      <div class="button-area">
        <el-button type="primary" :icon="Plus" @click="handleAdd">{{ $t('table.add') }}</el-button>
        <el-button type="danger" :icon="Delete" @click="handleBatchDelete">{{ $t('table.delete') }}</el-button>
      </div>
    </div>

    <!-- 表格 -->
    <el-table
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
          <el-button link type="primary" @click="handleEdit(row)">{{ $t('table.edit') }}</el-button>
          <el-button link type="danger" @click="handleDelete(row)">{{ $t('table.delete') }}</el-button>
        </template>
      </el-table-column>
    </el-table>

    <!-- 分页 -->
    <div class="pagination">
      <el-pagination
        v-model:current-page="page"
        v-model:page-size="pageSize"
        :total="total"
        :page-sizes="[10, 20, 50]"
        layout="total, sizes, prev, pager, next"
        @current-change="loadData"
        @size-change="loadData"
      />
    </div>

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
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { Search, Plus, Delete } from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import VolForm from './VolForm.vue'

const { t } = useI18n()

// 定义列配置的类型
export interface ColumnConfig {
  prop: string       // 字段名
  label: string      // 显示标题
  width?: number     // 列宽
  type?: string      // 特殊类型：switch
  formType?: string  // 表单类型：input/textarea/switch/select/password
  required?: boolean // 是否必填
  hidden?: boolean   // 是否在表单中隐藏
  options?: { label: string; value: any }[] // 下拉选项
  tableHidden?: boolean // 是否在表格中隐藏（但表单中显示）
}

// 组件接收的参数
const props = withDefaults(defineProps<{
  columns: ColumnConfig[]
  idField?: string  // 主键字段名，默认 'id'
  api: {
    getList: (params: any) => Promise<any>
    add: (data: any) => Promise<any>
    update: (data: any) => Promise<any>
    del: (ids: any[]) => Promise<any>
  }
}>(), {
  idField: 'id'
})

// 表格状态
const tableData = ref<any[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(10)
const keyword = ref('')
const loading = ref(false)
const selectedRows = ref<any[]>([])

// 表单状态
const formVisible = ref(false)
const formTitle = ref('')
const formData = ref<any>({})

// 加载数据
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

// 判断是编辑还是新增
const isEdit = ref(false)

// 提交（新增或编辑）
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

// 新增
function handleAdd() {
  formTitle.value = t('table.add')
  formData.value = {}
  isEdit.value = false
  formVisible.value = true
}

// 编辑
function handleEdit(row: any) {
  formTitle.value = t('table.edit')
  formData.value = { ...row }
  isEdit.value = true
  formVisible.value = true
}

// 单条删除
async function handleDelete(row: any) {
  await ElMessageBox.confirm(t('table.confirmDelete'), t('table.tip'), { type: 'warning' })
  await props.api.del([row[props.idField]])
  ElMessage.success(t('table.deleteSuccess'))
  loadData()
}

// 批量删除
async function handleBatchDelete() {
  if (selectedRows.value.length === 0) {
    ElMessage.warning(t('table.selectFirst'))
    return
  }
  await ElMessageBox.confirm(t('table.confirmBatchDelete', { count: selectedRows.value.length }), t('table.tip'), { type: 'warning' })
  await props.api.del(selectedRows.value.map((r) => r[props.idField]))
  ElMessage.success(t('table.deleteSuccess'))
  loadData()
}

onMounted(() => loadData())
</script>

<style scoped>
.table-header {
  display: flex;
  justify-content: space-between;
  margin-bottom: 16px;
}
.pagination {
  display: flex;
  justify-content: flex-end;
  margin-top: 16px;
}
</style>
