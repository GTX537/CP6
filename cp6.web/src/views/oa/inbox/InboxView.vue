<template>
  <el-container class="oa-inbox" direction="vertical">
    <!-- 顶部工具栏 -->
    <el-header class="inbox-header">
      <span class="inbox-title">{{ t('oa.inbox.title') }}</span>
      <el-button type="primary" size="small" @click="openNewDialog">
        {{ t('oa.inbox.newBtn') }}
      </el-button>
    </el-header>

    <!-- 左侧菜单 + 右侧内容 -->
    <el-container class="inbox-body">
      <el-aside width="200px" class="inbox-aside">
        <el-menu
          :default-active="folder"
          class="inbox-menu"
          @select="onSelect"
        >
          <el-menu-item index="dashboard">
            {{ t('oa.inbox.dashboard') }}
          </el-menu-item>

          <el-menu-item index="pending">
            <span class="menu-item-row">
              <span>{{ t('oa.inbox.pending') }}</span>
              <el-badge
                v-if="stats?.pendingCount"
                :value="stats.pendingCount"
                class="menu-badge"
              />
            </span>
          </el-menu-item>

          <el-menu-item index="running">{{ t('oa.inbox.running') }}</el-menu-item>
          <el-menu-item index="done">{{ t('oa.inbox.done') }}</el-menu-item>
          <el-menu-item index="draft">{{ t('oa.inbox.draft') }}</el-menu-item>

          <el-menu-item index="flow-admin" class="flow-admin-item">
            {{ t('oa.inbox.flowAdmin') }}
          </el-menu-item>
        </el-menu>
      </el-aside>

      <el-main class="inbox-main">
        <component :is="currentComp" @open-detail="openDetail" />
      </el-main>
    </el-container>

    <!-- 表单详情抽屉 -->
    <el-drawer
      v-model="drawerVisible"
      size="60%"
      :title="t('oa.inbox.detailTitle')"
      destroy-on-close
    >
      <FormDetail v-if="detail.id" :instance-id="detail.id" />
    </el-drawer>

    <!-- 新建/填单对话框（占位；起草功能后续实现） -->
    <el-dialog
      v-model="newDialogVisible"
      :title="t('oa.inbox.newBtn')"
      width="480px"
    >
      <div v-loading="flowsLoading">
        <el-table :data="flows" size="small" stripe border>
          <el-table-column prop="flowKey" label="流程编号" width="160" />
          <el-table-column prop="flowName" label="流程名称" />
          <el-table-column prop="enable" label="状态" width="80">
            <template #default="{ row }">
              <el-tag :type="row.enable ? 'success' : 'info'" size="small">
                {{ row.enable ? '启用' : '停用' }}
              </el-tag>
            </template>
          </el-table-column>
        </el-table>
        <div class="new-dialog-note">起草功能后续版本实现</div>
      </div>
      <template #footer>
        <el-button @click="newDialogVisible = false">{{ t('oa.inbox.close') }}</el-button>
      </template>
    </el-dialog>
  </el-container>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref, type Component } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { inboxApi } from '@/api/oa/inbox'
import { flowAdminApi } from '@/api/oa/flowAdmin'
import type { InboxStats, FlowAdminItem } from '@/types/oa/inbox'
import InboxDashboard from './InboxDashboard.vue'
import InboxPending from './InboxPending.vue'
import InboxRunning from './InboxRunning.vue'
import InboxDone from './InboxDone.vue'
import InboxDraft from './InboxDraft.vue'
import FormDetail from './FormDetail.vue'

const { t } = useI18n()
const router = useRouter()

// ── 文件夹切换 ──────────────────────────────────────────────────
const folder = ref<string>('dashboard')

const compMap: Record<string, Component> = {
  dashboard: InboxDashboard,
  pending:   InboxPending,
  running:   InboxRunning,
  done:      InboxDone,
  draft:     InboxDraft,
}

const currentComp = computed<Component>(() => compMap[folder.value] ?? InboxDashboard)

function onSelect(key: string) {
  if (key === 'flow-admin') {
    router.push('/oa/flow-admin')
    return
  }
  folder.value = key
}

// ── 未处理徽标（统计） ──────────────────────────────────────────
const stats = ref<InboxStats | null>(null)

onMounted(async () => {
  try {
    const res = await inboxApi.stats()
    stats.value = (res as any).data as InboxStats
  } catch {
    // 忽略错误，徽标不显示
  }
})

// ── 详情抽屉 ────────────────────────────────────────────────────
const drawerVisible = ref(false)
const detail = reactive({ id: '' })

function openDetail(id: string) {
  detail.id = id
  drawerVisible.value = true
}

// ── 新建/填单对话框（占位） ─────────────────────────────────────
const newDialogVisible = ref(false)
const flowsLoading = ref(false)
const flows = ref<FlowAdminItem[]>([])

async function openNewDialog() {
  newDialogVisible.value = true
  if (flows.value.length) return
  flowsLoading.value = true
  try {
    const res = await flowAdminApi.list()
    flows.value = ((res as any).data as FlowAdminItem[]) || []
  } catch {
    // 忽略
  } finally {
    flowsLoading.value = false
  }
}
</script>

<style scoped>
.oa-inbox {
  height: 100%;
  background: #f5f7fa;
}

.inbox-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 16px;
  background: #fff;
  border-bottom: 1px solid var(--el-border-color-light);
  flex-shrink: 0;
}

.inbox-title {
  font-size: 16px;
  font-weight: 650;
  color: #303133;
}

.inbox-body {
  flex: 1;
  overflow: hidden;
}

.inbox-aside {
  background: #fff;
  border-right: 1px solid var(--el-border-color-light);
  overflow-y: auto;
}

.inbox-menu {
  border-right: none;
}

.inbox-main {
  padding: 16px;
  overflow-y: auto;
}

/* 未处理菜单项：文字+徽标横排 */
.menu-item-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
}

.menu-badge {
  margin-left: 6px;
}

/* 流程管理分隔线效果 */
.flow-admin-item {
  border-top: 1px solid var(--el-border-color-light);
  margin-top: 4px;
}

.new-dialog-note {
  margin-top: 12px;
  font-size: 12px;
  color: var(--el-text-color-placeholder);
}
</style>
