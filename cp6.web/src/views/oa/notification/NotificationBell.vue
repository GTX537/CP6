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
        <el-button v-permission="'oa-inbox:read'" link size="small" :disabled="unreadCount === 0 || !watermark || loading" @click="handleReadAll">
          {{ t('oa.notify.markAllRead') }}
        </el-button>
      </div>
      <el-divider style="margin: 8px 0" />

      <p v-if="error" role="alert">{{ error }}</p>

      <!-- 空状态 -->
      <div v-if="!error && !loading && notifications.length === 0" class="notify-empty">
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
          :data-notification-id="item.id"
          class="notify-item"
          :class="{ 'is-unread': !item.isRead }"
          tabindex="0"
          @click="handleItemClick(item)"
          @keydown.enter.prevent="handleItemClick(item)"
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
import { AUTH_EPOCH, AUTH_EVENT, AUTH_RECORD, IMPERSONATION, identityStamp } from '@/auth/identity'
import type { NotificationItem } from '@/types/oa/notification'
import { getConnection } from '@/utils/signalr'

const { t } = useI18n()
const router = useRouter()
const unreadCount = ref(0)
const popoverOpen = ref(false)
const notifications = ref<NotificationItem[]>([])
const watermark = ref('')
const loading = ref(false)
const error = ref('')
let controller = new AbortController()
let sequence = 0
let countSequence = 0
let pollTimer: ReturnType<typeof setInterval> | undefined

function clear() {
  controller.abort()
  controller = new AbortController()
  sequence++
  countSequence++
  notifications.value = []
  watermark.value = ''
  unreadCount.value = 0
  loading.value = false
}

function identityChanged() {
  clear()
  popoverOpen.value = false
  error.value = ''
  void refreshUnread()
}

function storageChanged(event: StorageEvent) {
  if (!event.key || [AUTH_EPOCH, AUTH_RECORD, IMPERSONATION].includes(event.key)) identityChanged()
}

function active(stamp: string, signal: AbortSignal) {
  return !signal.aborted && stamp === identityStamp()
}

async function refreshUnread() {
  const stamp = identityStamp(), signal = controller.signal, at = ++countSequence
  try {
    const count = await notificationApi.count(signal)
    if (active(stamp, signal) && at === countSequence) unreadCount.value = count
  } catch {
    if (active(stamp, signal) && at === countSequence) unreadCount.value = 0
  }
}

async function refreshList() {
  const stamp = identityStamp(), signal = controller.signal, at = ++sequence
  loading.value = true
  error.value = ''
  try {
    const page = await notificationApi.page(10, signal)
    if (!active(stamp, signal) || at !== sequence) return
    notifications.value = page.items
    watermark.value = page.watermark
  } catch {
    if (!active(stamp, signal) || at !== sequence) return
    notifications.value = []
    watermark.value = ''
    error.value = '当前通知不可读取。'
  } finally {
    if (active(stamp, signal) && at === sequence) loading.value = false
  }
}

async function onPopoverShow() {
  notifications.value = []
  watermark.value = ''
  await refreshList()
}

async function handleItemClick(item: NotificationItem) {
  const stamp = identityStamp(), signal = controller.signal
  if (item.taskId) {
    try {
      const context = await notificationApi.openTask(item.taskId, signal)
      if (!active(stamp, signal)) return
      popoverOpen.value = false
      const query = context.contextKind === 'APP' ? { appTask: item.taskId } : { personalTask: item.taskId }
      await router.push({ path: '/oa/inbox', query })
    } catch {
      if (!active(stamp, signal)) return
      clear()
      error.value = '此通知关联的任务当前不可查看。'
      const current = router.currentRoute.value
      if (current.path === '/oa/inbox' &&
          (current.query.appTask === item.taskId || current.query.personalTask === item.taskId)) {
        await router.replace({ path: '/oa/inbox' })
      }
      void refreshUnread()
      return
    }
  }
  try {
    if (!item.isRead && active(stamp, signal)) await notificationApi.mark(item.id, signal)
    if (active(stamp, signal)) await Promise.all([refreshUnread(), refreshList()])
  } catch {
    if (!active(stamp, signal)) return
    clear()
    error.value = '当前通知的已读状态未确认。'
    void refreshUnread()
  }
}

async function handleReadAll() {
  const stamp = identityStamp(), signal = controller.signal, observed = watermark.value
  if (!observed) return
  try {
    await notificationApi.readThrough(observed, signal)
    if (active(stamp, signal)) await Promise.all([refreshUnread(), refreshList()])
  } catch {
    if (!active(stamp, signal)) return
    notifications.value = []
    watermark.value = ''
    error.value = '请重新读取通知后再标记已读。'
  }
}

function refreshNotifications() {
  void refreshUnread()
  if (popoverOpen.value) void refreshList()
}
function wfNotificationHandler() {
  refreshNotifications()
  ElNotification({
    title: t('oa.notify.title'),
    message: t('oa.notify.newArrived'),
    type: 'info',
    duration: 3000,
    position: 'bottom-right',
  })
}

function relativeTime(dateStr: string): string {
  const minutes = Math.max(0, Math.floor((Date.now() - new Date(dateStr).getTime()) / 60000))
  if (minutes < 1) return '刚刚'
  if (minutes < 60) return minutes + ' 分钟前'
  if (minutes < 1440) return Math.floor(minutes / 60) + ' 小时前'
  return Math.floor(minutes / 1440) + ' 天前'
}

onMounted(() => {
  void refreshUnread()
  window.addEventListener(AUTH_EVENT, identityChanged)
  window.addEventListener('storage', storageChanged)
  getConnection().on('WfNotification', wfNotificationHandler)
  pollTimer = setInterval(refreshNotifications, 60_000)
})

onUnmounted(() => {
  controller.abort()
  window.removeEventListener(AUTH_EVENT, identityChanged)
  window.removeEventListener('storage', storageChanged)
  getConnection().off('WfNotification', wfNotificationHandler)
  if (pollTimer !== undefined) clearInterval(pollTimer)
})
</script>

<style scoped>
.bell-badge {
  display: flex;
  align-items: center;
}
.bell-btn {
  padding: 4px 6px;
  color: var(--cp-muted);
}
.bell-btn:hover {
  color: var(--cp-brand);
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
  color: var(--cp-ink);
}

/* 空状态 */
.notify-empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 24px 0;
  color: var(--cp-muted);
  font-size: 13px;
  gap: 8px;
}
.notify-empty-icon {
  color: var(--cp-faint);
}

/* 加载 */
.notify-loading {
  display: flex;
  justify-content: center;
  padding: 24px 0;
  color: var(--cp-brand);
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
  border-radius: var(--cp-r-sm);
  cursor: pointer;
  transition: background 0.15s;
}
.notify-item:hover {
  background: var(--cp-bg-hover);
}
.notify-item + .notify-item {
  border-top: 1px solid var(--cp-line-soft);
}
.notify-item.is-unread {
  background: var(--cp-brand-bg);
}
.notify-item.is-unread:hover {
  background: color-mix(in srgb, var(--cp-brand) 14%, var(--cp-card));
}

/* 未读小圆点 */
.notify-item-dot {
  flex-shrink: 0;
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: var(--cp-brand);
  margin-top: 5px;
}

.notify-item-body {
  flex: 1;
  min-width: 0;
}
.notify-item-title {
  font-size: 13px;
  color: var(--cp-ink);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.notify-item-time {
  font-size: 11px;
  color: var(--cp-muted);
  margin-top: 2px;
}
</style>
