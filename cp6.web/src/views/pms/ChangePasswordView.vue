<template>
  <div class="change-pwd-shell">
    <el-card class="change-pwd-card" shadow="never">
      <h2 class="change-pwd-title">{{ t('sec.changePwd.title') }}</h2>

      <!-- 强制改密提示 -->
      <el-alert
        v-if="mustChange"
        :title="t('sec.changePwd.required')"
        type="warning"
        :closable="false"
        show-icon
        style="margin-bottom: 18px"
      />

      <el-form ref="formRef" :model="form" :rules="rules" label-position="top">
        <el-form-item :label="t('sec.changePwd.current')" prop="currentPassword">
          <el-input
            v-model="form.currentPassword"
            type="password"
            show-password
            size="large"
          />
        </el-form-item>
        <el-form-item :label="t('sec.changePwd.new')" prop="newPassword">
          <el-input
            v-model="form.newPassword"
            type="password"
            show-password
            size="large"
          />
        </el-form-item>
        <el-form-item :label="t('sec.changePwd.confirm')" prop="confirmPassword">
          <el-input
            v-model="form.confirmPassword"
            type="password"
            show-password
            size="large"
            @keyup.enter="handleSubmit"
          />
        </el-form-item>
        <el-form-item>
          <el-button
            type="primary"
            size="large"
            style="width: 100%"
            :loading="loading"
            @click="handleSubmit"
          >
            {{ t('sec.changePwd.title') }}
          </el-button>
        </el-form-item>
      </el-form>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { ElMessage, type FormInstance, type FormRules } from 'element-plus'
import { authApi } from '@/api/sys/auth'

const { t } = useI18n()
const router = useRouter()
const formRef = ref<FormInstance>()
const loading = ref(false)

// 是否强制改密进来（登录后 mustChangePassword）
const mustChange = computed(() => localStorage.getItem('cp6_mustChangePwd') === '1')

const form = ref({
  currentPassword: '',
  newPassword: '',
  confirmPassword: ''
})

// 前端校验：新密码与确认必须一致
const validateConfirm = (_rule: any, value: string, callback: (err?: Error) => void) => {
  if (value !== form.value.newPassword) {
    callback(new Error(t('sec.changePwd.mismatch')))
  } else {
    callback()
  }
}

const rules = computed<FormRules>(() => ({
  currentPassword: [{ required: true, message: t('sec.changePwd.required'), trigger: 'blur' }],
  newPassword: [{ required: true, message: t('sec.changePwd.required'), trigger: 'blur' }],
  confirmPassword: [{ validator: validateConfirm, trigger: 'blur' }]
}))

async function handleSubmit() {
  if (!formRef.value) return
  await formRef.value.validate()

  loading.value = true
  try {
    await authApi.changePassword({
      currentPassword: form.value.currentPassword,
      newPassword: form.value.newPassword
    })
    ElMessage.success(t('sec.changePwd.success'))
    // 改密后后端吊销所有 refresh，且当前 token 仍带旧 must_change → 必须重新登录
    localStorage.removeItem('cp6_authed')
    localStorage.removeItem('cp6_mustChangePwd')
    localStorage.removeItem('menus')
    localStorage.removeItem('userName')
    localStorage.removeItem('nickName')
    router.push('/login')
  } catch {
    // 错误（E-SEC-004/005/006）由 http.ts 拦截器统一提示，此处不重复提示
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.change-pwd-shell {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 100vh;
  min-height: 100dvh;
  padding: 2rem;
  background: linear-gradient(135deg, #07111f 0%, #0b1d34 42%, #12345d 100%);
}
.change-pwd-card {
  width: min(100%, 420px);
  border-radius: 18px;
}
.change-pwd-title {
  margin: 0 0 1.4rem;
  font-size: 1.5rem;
  color: #303133;
}
</style>
