<template>
  <el-container class="oa-inbox" direction="vertical">
    <!-- act-as 横幅（代理身份时显示） -->
    <ActingAsBanner @cleared="reloadList" />

    <!-- 顶部工具栏 -->
    <el-header class="inbox-header">
      <span class="inbox-title">{{ t('oa.inbox.title') }}</span>

      <!-- 代理身份下拉 -->
      <el-dropdown
        v-if="canActAsList.length > 0 || actingAs"
        trigger="click"
        class="acting-as-dropdown"
        @command="handleActingAsCmd"
      >
        <el-button size="small" :type="actingAs ? 'warning' : 'default'">
          <el-icon><UserFilled /></el-icon>
          {{ actingAs ? actingAs.userName : '代理身份' }}
          <el-icon class="el-icon--right"><ArrowDown /></el-icon>
        </el-button>
        <template #dropdown>
          <el-dropdown-menu>
            <el-dropdown-item
              v-for="u in canActAsList"
              :key="u.userId"
              :command="{ type: 'set', user: u }"
            >
              以 {{ u.userName }} 身份处理
            </el-dropdown-item>
            <el-dropdown-item
              v-if="actingAs"
              :command="{ type: 'clear' }"
              divided
            >
              切回本人
            </el-dropdown-item>
          </el-dropdown-menu>
        </template>
      </el-dropdown>

      <el-button type="primary" size="small" @click="openNewDialog">
        {{ t('oa.inbox.newBtn') }}
      </el-button>
    </el-header>

    <!-- 移动端文件夹横滑条（替代左侧菜单） -->
    <div v-if="isMobile" class="mobile-folder-bar">
      <el-button
        v-for="f in folderList"
        :key="f.key"
        size="small"
        round
        :type="folder === f.key ? 'primary' : 'default'"
        @click="onSelect(f.key)"
      >
        {{ f.label }}<template v-if="f.key === 'pending' && stats?.pendingCount"> ({{ stats.pendingCount }})</template>
      </el-button>
    </div>

    <!-- 左侧菜单 + 右侧内容 -->
    <el-container class="inbox-body">
      <el-aside v-if="!isMobile" width="200px" class="inbox-aside">
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
      :size="isMobile ? '100%' : '60%'"
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
              <CpTag :tone="row.enable ? 'ok' : 'muted'">
                {{ row.enable ? '启用' : '停用' }}
              </CpTag>
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
import { UserFilled, ArrowDown } from '@element-plus/icons-vue'
import { inboxApi } from '@/api/oa/inbox'
import { flowAdminApi } from '@/api/oa/flowAdmin'
import { delegateApi } from '@/api/oa/delegate'
import { getActingAs, setActingAs, clearActingAs } from '@/stores/oaActingAs'
import type { ActingAs } from '@/stores/oaActingAs'
import type { GrantUser } from '@/types/oa/advanced'
import type { InboxStats, FlowAdminItem } from '@/types/oa/inbox'
import ActingAsBanner from '@/components/oa/ActingAsBanner.vue'
import InboxDashboard from './InboxDashboard.vue'
import InboxPending from './InboxPending.vue'
import InboxRunning from './InboxRunning.vue'
import InboxDone from './InboxDone.vue'
import InboxDraft from './InboxDraft.vue'
import FormDetail from './FormDetail.vue'
import CpTag from '@/components/base/CpTag.vue'
import { useBreakpoint } from '@/composables/useBreakpoint'

const { t } = useI18n()
const router = useRouter()
const { isMobile } = useBreakpoint()

/** 移动端文件夹横滑条（含流程管理路由项） */
const folderList = computed(() => [
  { key: 'dashboard',  label: t('oa.inbox.dashboard') },
  { key: 'pending',    label: t('oa.inbox.pending') },
  { key: 'running',    label: t('oa.inbox.running') },
  { key: 'done',       label: t('oa.inbox.done') },
  { key: 'draft',      label: t('oa.inbox.draft') },
  { key: 'flow-admin', label: t('oa.inbox.flowAdmin') },
])

// ── act-as 态机 ──────────────────────────────────────────────────
const actingAs = ref<ActingAs | null>(getActingAs())
const canActAsList = ref<GrantUser[]>([])

async function loadGrants() {
  try {
    const res = await delegateApi.myGrants()
    const data = (res as any)?.data ?? res
    canActAsList.value = (data?.iCanActAs as GrantUser[]) || []
  } catch {
    // 无代理权限时静默忽略
  }
}

type ActingAsCmd = { type: 'set'; user: GrantUser } | { type: 'clear' }

function handleActingAsCmd(cmd: ActingAsCmd) {
  if (cmd.type === 'set') {
    setActingAs({ userId: cmd.user.userId, userName: cmd.user.userName })
    actingAs.value = getActingAs()
    reloadList()
  } else {
    clearActingAs()
    actingAs.value = null
    reloadList()
  }
}

function reloadList() {
  // 重新触发当前文件夹列表刷新：通过强制路由重载最简单
  window.location.reload()
}

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
  // 加载可代理用户列表（T10 act-as 入口）
  await loadGrants()
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
  background: var(--cp-bg);
}

.inbox-header {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 0 16px;
  background: var(--cp-card);
  border-bottom: 1px solid var(--cp-line-soft);
  flex-shrink: 0;
}

.acting-as-dropdown {
  margin-left: auto;
}

.inbox-title {
  font-size: 16px;
  font-weight: 650;
  color: var(--cp-ink);
}

.inbox-body {
  flex: 1;
  overflow: hidden;
}

.inbox-aside {
  background: var(--cp-card);
  border-right: 1px solid var(--cp-line-soft);
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
  border-top: 1px solid var(--cp-line-soft);
  margin-top: 4px;
}

.new-dialog-note {
  margin-top: 12px;
  font-size: 12px;
  color: var(--cp-faint);
}

.mobile-folder-bar {
  display: flex;
  gap: 6px;
  padding: 8px 12px;
  overflow-x: auto;
  background: var(--cp-card);
  border-bottom: 1px solid var(--cp-line-soft);
  flex-shrink: 0;
  -webkit-overflow-scrolling: touch;
}

.mobile-folder-bar .el-button {
  flex-shrink: 0;
  margin-left: 0;
}

@media (max-width: 767px) {
  .inbox-header {
    padding: 0 12px;
  }

  .inbox-title {
    font-size: 14px;
  }

  .inbox-main {
    padding: 10px;
  }
}
</style>
