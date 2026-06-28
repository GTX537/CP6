<template>
  <el-dialog
    :model-value="modelValue"
    :title="t('oa.transfer.title')"
    width="440px"
    @close="onClose"
  >
    <el-form label-width="80px">
      <el-form-item :label="t('oa.transfer.toUser')">
        <el-select
          v-model="toUserId"
          filterable
          remote
          :remote-method="searchUsers"
          :loading="userSearchLoading"
          :placeholder="t('oa.transfer.userHint')"
          style="width: 100%"
          clearable
        >
          <el-option
            v-for="u in userOptions"
            :key="u.value"
            :label="u.label"
            :value="u.value"
          />
        </el-select>
      </el-form-item>
      <el-form-item :label="t('oa.transfer.comment')">
        <el-input
          v-model="comment"
          type="textarea"
          :rows="3"
          :placeholder="t('oa.transfer.commentHint')"
        />
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="onClose">{{ t('common.cancel') }}</el-button>
      <el-button
        type="warning"
        :loading="submitting"
        :disabled="!toUserId"
        @click="onConfirm"
      >
        {{ t('oa.transfer.confirm') }}
      </el-button>
    </template>
  </el-dialog>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { transferApi } from '@/api/oa/transfer'
import { userApi } from '@/api/sys/user'

const props = defineProps<{ taskId: string; modelValue: boolean }>()
const emit = defineEmits<{ done: []; 'update:modelValue': [val: boolean] }>()

const { t } = useI18n()

const toUserId = ref('')
const comment = ref('')
const submitting = ref(false)

interface UserOption {
  label: string
  value: string
}
const userOptions = ref<UserOption[]>([])
const userSearchLoading = ref(false)

async function searchUsers(keyword: string) {
  if (!keyword) {
    userOptions.value = []
    return
  }
  userSearchLoading.value = true
  try {
    const res: any = await userApi.getList({ page: 1, pageSize: 20, keyword })
    const rows: any[] = res.rows ?? []
    userOptions.value = rows.map((u: any) => ({
      label: u.nickName || u.userName,
      value: u.id,
    }))
  } catch {
    // HTTP interceptor already toasts the error
  } finally {
    userSearchLoading.value = false
  }
}

async function onConfirm() {
  if (!toUserId.value) return
  submitting.value = true
  try {
    await transferApi.transfer(props.taskId, toUserId.value, comment.value || undefined)
    ElMessage.success(t('oa.transfer.ok'))
    emit('done')
    closeDialog()
  } catch {
    // HTTP interceptor auto-toasts (e.g. E-WF-002); keep dialog open so user can cancel
  } finally {
    submitting.value = false
  }
}

function closeDialog() {
  emit('update:modelValue', false)
  toUserId.value = ''
  comment.value = ''
  userOptions.value = []
}

function onClose() {
  closeDialog()
}
</script>
