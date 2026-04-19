<template>
  <div style="padding: 20px">
    <h2>{{ $t('menu.title') }}</h2>

    <div style="margin-bottom: 16px">
      <el-button type="primary" :icon="Plus" @click="handleAdd(null)">{{ $t('menu.addTop') }}</el-button>
    </div>

    <el-table :data="menuTree" row-key="menuId" border default-expand-all>
      <el-table-column prop="menuId" :label="$t('menu.menuId')" width="100" />
      <el-table-column prop="menuName" :label="$t('menu.menuName')" width="200" />
      <el-table-column prop="routePath" :label="$t('menu.routePath')" width="200" />
      <el-table-column prop="icon" :label="$t('menu.icon')" width="120" />
      <el-table-column prop="orderNo" :label="$t('menu.orderNo')" width="80" />
      <el-table-column prop="enable" :label="$t('menu.status')" width="80">
        <template #default="{ row }">
          <el-tag :type="row.enable ? 'success' : 'info'">{{ row.enable ? $t('menu.enabled') : $t('menu.disabled') }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column :label="$t('menu.operation')" width="220">
        <template #default="{ row }">
          <el-button link type="primary" @click="handleAdd(row.menuId)">{{ $t('menu.addChild') }}</el-button>
          <el-button link type="primary" @click="handleEdit(row)">{{ $t('table.edit') }}</el-button>
          <el-button link type="danger" @click="handleDelete(row)">{{ $t('table.delete') }}</el-button>
        </template>
      </el-table-column>
    </el-table>

    <!-- 编辑弹窗 -->
    <el-dialog v-model="dialogVisible" :title="formTitle" width="500px">
      <el-form ref="formRef" :model="form" label-width="80px">
        <el-form-item :label="$t('menu.menuId')" prop="menuId" :rules="[{ required: true, message: $t('menu.menuIdRequired') }]">
          <el-input-number v-model="form.menuId" :min="1" :disabled="isEdit" style="width: 100%" />
        </el-form-item>
        <el-form-item :label="$t('menu.menuName')" prop="menuName" :rules="[{ required: true, message: $t('menu.menuNameRequired') }]">
          <el-input v-model="form.menuName" />
        </el-form-item>
        <el-form-item :label="$t('menu.routePath')">
          <el-input v-model="form.routePath" :placeholder="$t('menu.routePathPlaceholder')" />
        </el-form-item>
        <el-form-item :label="$t('menu.icon')">
          <el-input v-model="form.icon" :placeholder="$t('menu.iconPlaceholder')" />
        </el-form-item>
        <el-form-item :label="$t('menu.orderNo')">
          <el-input-number v-model="form.orderNo" :min="0" />
        </el-form-item>
        <el-form-item :label="$t('menu.enabled')">
          <el-switch v-model="form.enable" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">{{ $t('table.cancel') }}</el-button>
        <el-button type="primary" @click="handleSubmit">{{ $t('table.confirm') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { Plus } from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import type { FormInstance } from 'element-plus'
import { menuApi } from '@/api/menu'

const { t } = useI18n()

const menuTree = ref<any[]>([])
const dialogVisible = ref(false)
const formTitle = ref('')
const formRef = ref<FormInstance>()
const form = ref<any>({})
const isEdit = ref(false)

function buildTree(list: any[], parentId: number | null = null): any[] {
  return list
    .filter((item) => item.parentId === parentId)
    .map((item) => ({
      ...item,
      children: buildTree(list, item.menuId)
    }))
}

async function loadData() {
  const res: any = await menuApi.getAll()
  menuTree.value = buildTree(res)
}

function handleAdd(parentId: number | null) {
  formTitle.value = parentId ? t('menu.addSubMenu') : t('menu.addTopMenu')
  form.value = { parentId, orderNo: 0, enable: true }
  isEdit.value = false
  dialogVisible.value = true
}

function handleEdit(row: any) {
  formTitle.value = t('menu.editMenu')
  form.value = { ...row, children: undefined }
  isEdit.value = true
  dialogVisible.value = true
}

async function handleSubmit() {
  if (!formRef.value) return
  await formRef.value.validate()
  if (isEdit.value) {
    await menuApi.update(form.value)
    ElMessage.success(t('table.editSuccess'))
  } else {
    await menuApi.add(form.value)
    ElMessage.success(t('table.addSuccess'))
  }
  dialogVisible.value = false
  loadData()
}

async function handleDelete(row: any) {
  await ElMessageBox.confirm(t('menu.confirmDelete'), t('table.tip'), { type: 'warning' })
  await menuApi.del([row.menuId])
  ElMessage.success(t('table.deleteSuccess'))
  loadData()
}

onMounted(loadData)
</script>
