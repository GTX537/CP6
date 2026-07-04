<template>
  <el-popover
    v-model:visible="popoverOpen"
    placement="bottom-end"
    :width="360"
    trigger="click"
    popper-class="notify-popper"
    @show="onPopoverShow"
  >
    <template #reference>
      <el-badge :value="unreadCount" :hidden="unreadCount === 0" :max="99" class="bell-badge">
        <el-button link class="bell-btn" :title="t('oa.notify.title')">
          <el-icon :size="20"><Bell /></el-icon>
        </el-button>
      </el-badge>
    </template>

    <!-- Popover 内容 -->
    <div class="notify-panel">
      <!-- 头部 -->
      <div class="notify-panel-header">
        <span class="notify-panel-title">{{ t('oa.notify.title') }}</span>
        <el-button link size="small" :disabled="unreadCount === 0" @click="handleReadAll">
          {{ t('oa.notify.markAllRead') }}
        </el-button>
      </div>
      <el-divider style="margin: 8px 0" />

      <!-- 空状态 -->
      <div v-if="!loading && notifications.length === 0" class="notify-empty">
        <el-icon :size="28" class="notify-empty-icon"><Bell /></el-icon>
        <span>{{ t('oa.notify.empty') }}</span>
      </div>

      <!-- 加载中 -->
      <div v-if="loading" class="notify-loading">
        <el-icon class="is-loading" :size="20"><Loading /></el-icon>
      </div>

      <!-- 列表 -->
      <ul v-if="!loading && notifications.length > 0" class="notify-list">
        <li
          v-for="item in notifications"
          :key="item.id"
          class="notify-item"
          :class="{ 'is-unread': !item.isRead }"
          @click="handleItemClick(item)"
        >
          <div class="notify-item-dot" v-if="!item.isRead" />
          <div class="notify-item-body">
            <div class="notify-item-title">{{ item.title }}</div>
            <div class="notify-item-time">{{ relativeTime(item.createDate) }}</div>
          </div>
        </li>
      </ul>
    </div>
  </el-popover>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { Bell, Loading } from '@element-plus/icons-vue'
import { ElNotification } from 'element-plus'
import { notificationApi } from '@/api/oa/notification'
import type { NotificationItem } from '@/types/oa/notification'
import { getConnection } from '@/utils/signalr'

const { t } = useI18n()
const router = useRouter()

const unreadCount = ref(0)
const popoverOpen  = ref(false)
const notifications = ref<NotificationItem[]>([])
const loading = ref(false)

// ── 数据刷新 ──────────────────────────────────────────────────────────

async function refreshUnread() {
  try {
    const res = await notificationApi.unreadCount()
    // 后端返回 { code, message, data: { count } }；http 拦截器已解到 body，
    // 故未读数在 res.data.count（不是 res.data 本身——那是对象，直接绑到 :value 会渲染 [object Object]）
    const count = (res as any)?.data?.count
    unreadCount.value = typeof count === 'number' ? count : 0
  } catch {
    // best-effort，网络失败静默忽略
  }
}

async function refreshList() {
  loading.value = true
  try {
    const res = await notificationApi.list(false, 1, 10)
    notifications.value = (res as any)?.data ?? (res as unknown as NotificationItem[]) ?? []
  } catch {
    // best-effort
  } finally {
    loading.value = false
  }
}

// Popover 打开时拉最新列表
async function onPopoverShow() {
  await refreshList()
}

// ── 用户操作 ──────────────────────────────────────────────────────────

async function handleItemClick(item: NotificationItem) {
  popoverOpen.value = false
  if (!item.isRead) {
    try {
      await notificationApi.read(item.id)
      item.isRead = true
      if (unreadCount.value > 0) unreadCount.value--
    } catch {
      // 标记失败不阻断导航
    }
  }
  // 跳转到信箱（若有 instanceId 可带，按 inbox 路由实际支持）
  if (item.instanceId) {
    router.push({ path: '/oa/inbox', query: { instanceId: item.instanceId } })
  } else {
    router.push('/oa/inbox')
  }
}

async function handleReadAll() {
  try {
    await notificationApi.readAll()
    unreadCount.value = 0
    notifications.value = notifications.value.map(n => ({ ...n, isRead: true }))
  } catch {
    // best-effort
  }
}

// ── SignalR 接线 ───────────────────────────────────────────────────────
// 保留 handler 引用，卸载时 off() 移除，防止内存泄漏

const wfNotificationHandler = () => {
  // 不依赖客户端 userId 过滤——后端已按 auth scope 推送到本人
  refreshUnread()
  if (popoverOpen.value) {
    refreshList()
  }
  // 轻量弹窗提示（可选）
  ElNotification({
    title: t('oa.notify.title'),
    message: t('oa.notify.newArrived'),
    type: 'info',
    duration: 3000,
    position: 'bottom-right',
  })
}

// ── 60s 轮询兜底（防 SignalR 掉线后角标失效）────────────────────────────
let pollTimer: ReturnType<typeof setInterval> | null = null

// ── 相对时间格式化 ─────────────────────────────────────────────────────

function relativeTime(dateStr: string): string {
  const diff = Math.floor((Date.now() - new Date(dateStr).getTime()) / 1000)
  if (diff < 0)    return ''
  if (diff < 60)   return `${diff}s`
  if (diff < 3600) return `${Math.floor(diff / 60)}m`
  if (diff < 86400) return `${Math.floor(diff / 3600)}h`
  return `${Math.floor(diff / 86400)}d`
}

// ── 生命周期 ──────────────────────────────────────────────────────────

onMounted(() => {
  refreshUnread()
  getConnection().on('WfNotification', wfNotificationHandler)
  pollTimer = setInterval(refreshUnread, 60_000)
})

onUnmounted(() => {
  getConnection().off('WfNotification', wfNotificationHandler)
  if (pollTimer !== null) {
    clearInterval(pollTimer)
    pollTimer = null
  }
})
</script>

<style scoped>
.bell-badge {
  display: flex;
  align-items: center;
}
.bell-btn {
  padding: 4px 6px;
  color: #606266;
}
.bell-btn:hover {
  color: #409eff;
}

/* Popover 内容 */
.notify-panel {
  padding: 4px 0;
  min-height: 80px;
}
.notify-panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 4px;
}
.notify-panel-title {
  font-weight: 600;
  font-size: 14px;
  color: #303133;
}

/* 空状态 */
.notify-empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 24px 0;
  color: #909399;
  font-size: 13px;
  gap: 8px;
}
.notify-empty-icon {
  color: #dcdfe6;
}

/* 加载 */
.notify-loading {
  display: flex;
  justify-content: center;
  padding: 24px 0;
  color: #409eff;
}
.is-loading {
  animation: rotating 1.5s linear infinite;
}
@keyframes rotating {
  from { transform: rotate(0deg); }
  to   { transform: rotate(360deg); }
}

/* 通知列表 */
.notify-list {
  list-style: none;
  margin: 0;
  padding: 0;
  max-height: 320px;
  overflow-y: auto;
}
.notify-item {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  padding: 10px 4px;
  border-radius: 6px;
  cursor: pointer;
  transition: background 0.15s;
}
.notify-item:hover {
  background: #f5f7fa;
}
.notify-item + .notify-item {
  border-top: 1px solid #f0f0f0;
}
.notify-item.is-unread {
  background: #f0f7ff;
}
.notify-item.is-unread:hover {
  background: #e8f3ff;
}

/* 未读小圆点 */
.notify-item-dot {
  flex-shrink: 0;
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: #409eff;
  margin-top: 5px;
}

.notify-item-body {
  flex: 1;
  min-width: 0;
}
.notify-item-title {
  font-size: 13px;
  color: #303133;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.notify-item-time {
  font-size: 11px;
  color: #909399;
  margin-top: 2px;
}
</style>
