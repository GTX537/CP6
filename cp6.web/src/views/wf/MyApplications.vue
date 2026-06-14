<template>
  <div class="my-applications">
    <div class="page-header"><h2>我的申请</h2></div>

    <el-card shadow="never" class="table-card">
      <div class="table-toolbar">
        <el-tag size="small">共 {{ rows.length }} 条</el-tag>
        <el-button :icon="Refresh" circle size="small" :loading="loading" @click="load" />
      </div>

      <el-table :data="rows" border stripe size="small" max-height="620" v-loading="loading">
        <el-table-column prop="flowKey" label="流程" width="180" />
        <el-table-column prop="currentNode" label="当前节点" width="150" />
        <el-table-column label="状态" width="110">
          <template #default="{ row }">
            <el-tag :type="statusTag(row.status)" size="small">{{ statusText(row.status) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="发起时间" width="170">
          <template #default="{ row }">{{ formatTime(row.createDate) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="180" fixed="right">
          <template #default="{ row }">
            <el-button type="primary" link size="small" @click="openTrace(row)">痕迹</el-button>
            <el-button
              v-if="row.status === 0"
              type="warning"
              link
              size="small"
              @click="withdraw(row)"
            >撤回</el-button>
          </template>
        </el-table-column>
      </el-table>
      <el-empty v-if="!rows.length && !loading" description="暂无申请" :image-size="80" />
    </el-card>

    <el-dialog v-model="traceVisible" title="审批痕迹" width="520px">
      <FlowTrace v-if="traceVisible" :instance-id="traceId" />
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Refresh } from '@element-plus/icons-vue'
import FlowTrace from './FlowTrace.vue'
import { flowApi } from '@/api/wf/flow'
import {
  FLOW_INSTANCE_STATUS,
  FLOW_INSTANCE_STATUS_TAG,
  type ElTagType,
  type MyApplicationItem,
} from '@/types/wf/wf'

const rows = ref<MyApplicationItem[]>([])
const loading = ref(false)
const traceVisible = ref(false)
const traceId = ref('')

async function load() {
  loading.value = true
  try {
    const res = await flowApi.myApplications()
    rows.value = res.data || []
  } finally {
    loading.value = false
  }
}

function openTrace(row: MyApplicationItem) {
  traceId.value = row.instanceId
  traceVisible.value = true
}

async function withdraw(row: MyApplicationItem) {
  try {
    await ElMessageBox.confirm('确认撤回该申请？', '撤回', { type: 'warning' })
  } catch {
    return // 用户取消
  }
  await flowApi.withdraw(row.instanceId)
  ElMessage.success('已撤回')
  await load()
}

function statusText(s: number): string {
  return FLOW_INSTANCE_STATUS[s] || String(s)
}
function statusTag(s: number): ElTagType {
  return FLOW_INSTANCE_STATUS_TAG[s] || 'info'
}
function formatTime(t: string): string {
  return t ? t.replace('T', ' ').slice(0, 19) : ''
}

onMounted(load)
</script>

<style scoped>
.my-applications {
  padding: 16px;
}
.page-header {
  margin-bottom: 12px;
}
.page-header h2 {
  margin: 0;
  font-size: 20px;
  font-weight: 650;
  color: #303133;
}
.table-toolbar {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 8px;
}
</style>
