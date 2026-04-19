<template>
  <div class="login-container">
    <el-card class="login-card">
      <h2 class="login-title">{{ $t('login.title') }}</h2>
      <el-form ref="formRef" :model="form" :rules="rules" label-width="0">
        <el-form-item prop="userName">
          <el-input
            v-model="form.userName"
            :placeholder="$t('login.username')"
            prefix-icon="User"
            size="large"
          />
        </el-form-item>
        <el-form-item prop="password">
          <el-input
            v-model="form.password"
            type="password"
            :placeholder="$t('login.password')"
            prefix-icon="Lock"
            size="large"
            show-password
            @keyup.enter="handleLogin"
          />
        </el-form-item>
        <el-form-item>
          <el-button
            type="primary"
            size="large"
            style="width: 100%"
            :loading="loading"
            @click="handleLogin"
          >
            {{ $t('login.button') }}
          </el-button>
        </el-form-item>
      </el-form>
      <!-- 语言切换 -->
      <div class="lang-switch">
        <el-select v-model="currentLang" size="small" style="width: 140px" @change="onChangeLang">
          <el-option
            v-for="item in langOptions"
            :key="item.value"
            :label="item.label"
            :value="item.value"
          />
        </el-select>
      </div>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import type { FormInstance } from 'element-plus'
import { ElMessage } from 'element-plus'
import http from '@/api/http'
import { langOptions, changeLang } from '@/i18n'
import { addDynamicRoutes } from '@/router'

const { t, locale } = useI18n()
const router = useRouter()
const formRef = ref<FormInstance>()
const loading = ref(false)
const currentLang = ref(locale.value)

const form = ref({
  userName: '',
  password: ''
})

const rules = computed(() => ({
  userName: [{ required: true, message: t('login.usernameRequired'), trigger: 'blur' }],
  password: [{ required: true, message: t('login.passwordRequired'), trigger: 'blur' }]
}))

async function onChangeLang(lang: string) {
  await changeLang(lang)
}

async function handleLogin() {
  if (!formRef.value) return
  await formRef.value.validate()

  loading.value = true
  try {
    const res: any = await http.post('/auth/login', form.value)
    localStorage.setItem('token', res.token)
    localStorage.setItem('userName', res.userName)
    localStorage.setItem('nickName', res.nickName || res.userName)
    const menus = res.menus || []
    localStorage.setItem('menus', JSON.stringify(menus))
    addDynamicRoutes(menus)
    ElMessage.success(t('login.success'))
    router.push('/')
  } catch {
    // 错误由 http.ts 拦截器统一处理
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.login-container {
  display: flex;
  justify-content: center;
  align-items: center;
  height: 100vh;
  background: #f0f2f5;
}
.login-card {
  width: 400px;
}
.login-title {
  text-align: center;
  margin-bottom: 24px;
  color: #303133;
}
.lang-switch {
  display: flex;
  justify-content: center;
  margin-top: 12px;
}
</style>
