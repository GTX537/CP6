<template>
  <div style="padding: 20px">
    <h2>部门管理</h2>

    <div style="margin-bottom: 16px">
      <el-button type="primary" :icon="Plus" @click="handleAdd(null)">新增根部门</el-button>
      <el-button :icon="Refresh" @click="loadData">刷新</el-button>
    </div>

    <el-table :data="tree" row-key="id" border default-expand-all
              :tree-props="{ children: 'children' }">
      <el-table-column prop="deptCode" label="部门编码" width="160" />
      <el-table-column prop="deptName" label="部门名称" min-width="200" />
      <el-table-column prop="leaderName" label="部门负责人" width="160">
        <template #default="{ row }">{{ row.leaderName || '—' }}</template>
      </el-table-column>
      <el-table-column prop="sort" label="排序" width="80" />
      <el-table-column label="状态" width="90">
        <template #default="{ row }">
          <el-tag :type="row.enable ? 'success' : 'info'">{{ row.enable ? '启用' : '停用' }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="操作" width="280">
        <template #default="{ row }">
          <el-button link type="primary" @click="handleAdd(row)">新增子部门</el-button>
          <el-button link type="primary" @click="handleEdit(row)">编辑</el-button>
          <el-button link type="primary" @click="handleMove(row)">移动</el-button>
          <el-button link type="danger" @click="handleDelete(row)">删除</el-button>
        </template>
      </el-table-column>
    </el-table>

    <!-- 新增 / 编辑 弹窗 -->
    <el-dialog v-model="dialogVisible" :title="formTitle" width="480px">
      <el-form ref="formRef" :model="form" label-width="90px">
        <el-form-item label="上级部门">
          <span>{{ parentName || '（根部门）' }}</span>
        </el-form-item>
        <el-form-item label="部门编码" prop="deptCode"
                      :rules="[{ required: true, message: '请输入部门编码' }]">
          <el-input v-model="form.deptCode" :disabled="isEdit" placeholder="建后不可改" />
        </el-form-item>
        <el-form-item label="部门名称" prop="deptName"
                      :rules="[{ required: true, message: '请输入部门名称' }]">
          <el-input v-model="form.deptName" />
        </el-form-item>
        <el-form-item label="部门负责人">
          <el-select v-model="form.leaderId" clearable filterable placeholder="选择负责人" style="width: 100%">
            <el-option v-for="u in users" :key="u.id" :label="u.label" :value="u.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="排序">
          <el-input-number v-model="form.sort" :min="0" />
        </el-form-item>
        <el-form-item label="启用">
          <el-switch v-model="form.enable" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" @click="handleSubmit">确定</el-button>
      </template>
    </el-dialog>

    <!-- 移动 弹窗 -->
    <el-dialog v-model="moveVisible" title="移动部门" width="420px">
      <el-form label-width="90px">
        <el-form-item label="移动部门">
          <span>{{ moveSource?.deptName }}</span>
        </el-form-item>
        <el-form-item label="新上级">
          <el-tree-select v-model="moveTargetId" :data="tree" clearable check-strictly
                          :props="{ label: 'deptName', value: 'id', children: 'children' }"
                          placeholder="清空 = 设为根部门" style="width: 100%" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="moveVisible = false">取消</el-button>
        <el-button type="primary" @click="handleMoveSubmit">确定</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { Plus, Refresh } from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox, type FormInstance } from 'element-plus'
import { deptApi } from '@/api/sys/dept'
import { userApi } from '@/api/sys/user'
import type { DeptTreeNode, DeptForm } from '@/types/sys/dept'

const tree = ref<DeptTreeNode[]>([])
const users = ref<{ id: string; label: string }[]>([])

const dialogVisible = ref(false)
const formTitle = ref('')
const formRef = ref<FormInstance>()
const isEdit = ref(false)
const parentId = ref<string | null>(null)
const parentName = ref<string>('')
const form = ref<DeptForm>(emptyForm())

const moveVisible = ref(false)
const moveSource = ref<DeptTreeNode | null>(null)
const moveTargetId = ref<string | null>(null)

function emptyForm(): DeptForm {
  return { deptCode: '', deptName: '', leaderId: null, sort: 0, enable: true }
}

async function loadData() {
  tree.value = await deptApi.tree()
}

async function loadUsers() {
  const r: any = await userApi.getList({ page: 1, pageSize: 1000 })
  const rows: any[] = r.rows ?? r.data ?? r ?? []
  users.value = rows.map((u) => ({ id: u.id, label: u.nickName || u.userName }))
}

function handleAdd(parent: DeptTreeNode | null) {
  isEdit.value = false
  parentId.value = parent?.id ?? null
  parentName.value = parent?.deptName ?? ''
  formTitle.value = parent ? '新增子部门' : '新增根部门'
  form.value = emptyForm()
  dialogVisible.value = true
}

function handleEdit(row: DeptTreeNode) {
  isEdit.value = true
  parentId.value = row.parentId ?? null
  parentName.value = ''
  formTitle.value = '编辑部门'
  form.value = {
    id: row.id, deptCode: row.deptCode, deptName: row.deptName,
    leaderId: row.leaderId ?? null, sort: row.sort, enable: row.enable,
  }
  dialogVisible.value = true
}

async function handleSubmit() {
  if (!formRef.value) return
  await formRef.value.validate()
  if (isEdit.value && form.value.id) {
    await deptApi.update(form.value.id, form.value)
    ElMessage.success('已保存')
  } else {
    await deptApi.create({ ...form.value, parentId: parentId.value })
    ElMessage.success('已新增')
  }
  dialogVisible.value = false
  loadData()
}

function handleMove(row: DeptTreeNode) {
  moveSource.value = row
  moveTargetId.value = row.parentId ?? null
  moveVisible.value = true
}

async function handleMoveSubmit() {
  if (!moveSource.value) return
  await deptApi.move(moveSource.value.id, moveTargetId.value)
  ElMessage.success('已移动')
  moveVisible.value = false
  loadData()
}

async function handleDelete(row: DeptTreeNode) {
  await ElMessageBox.confirm(`删除部门「${row.deptName}」？`, '提示', { type: 'warning' })
  await deptApi.remove(row.id)
  ElMessage.success('已删除')
  loadData()
}

onMounted(() => {
  loadData()
  loadUsers()
})
</script>
