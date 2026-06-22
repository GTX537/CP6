import axios from 'axios'
import { ElMessage } from 'element-plus'
import router from '@/router'
import i18n from '@/i18n'

// 模块级（非组件）取全局 t；与 format.ts 同模式。i18n↔http 为运行时循环引用，
// 仅在拦截器回调（运行期）取用，模块求值期不触达，安全。
const t = (k: string) => (i18n.global as any).t(k)

// 从 document.cookie 读指定名字的值（CSRF 双提交：cp6_csrf 非 httpOnly，JS 可读）
function getCookie(name: string): string {
  const m = document.cookie.match(new RegExp('(?:^|; )' + name + '=([^;]*)'))
  return m && m[1] ? decodeURIComponent(m[1]) : ''
}

// 清登录态信号（httpOnly token 由后端清；前端仅清非敏感标志）
function clearAuthSignal() {
  localStorage.removeItem('cp6_authed')
  localStorage.removeItem('cp6_mustChangePwd')
}

// 创建 axios 实例，统一配置
const http = axios.create({
  baseURL: '/api',          // 通过 Vite 代理转发到后端
  timeout: 10000,
  withCredentials: true     // T9：携带 httpOnly Cookie（cp6_at/cp6_rt/cp6_csrf）
})

// 请求拦截器：非安全方法注入 CSRF 头（token 在 httpOnly Cookie，浏览器自动带，无需手动注入）
http.interceptors.request.use((config) => {
  const method = (config.method || 'get').toLowerCase()
  if (method !== 'get' && method !== 'head' && method !== 'options') {
    const csrf = getCookie('cp6_csrf')
    if (csrf) {
      config.headers['X-CSRF-Token'] = csrf
    }
  }
  return config
})

// 模块级共享 refresh promise：并发 401 只发一次 refresh
let refreshPromise: Promise<any> | null = null

function doRefresh() {
  if (!refreshPromise) {
    // 直接用底层 axios 实例发，避免再次进入响应拦截器的 401 处理逻辑
    refreshPromise = http.post('/auth/refresh').finally(() => {
      refreshPromise = null
    })
  }
  return refreshPromise
}

// 响应拦截器：统一处理错误 + 401 自动 refresh 一次 + 重放
http.interceptors.response.use(
  (response) => response.data,
  async (error) => {
    const status = error.response?.status
    const config = error.config || {}
    const url: string = config.url || ''

    if (status === 401) {
      // 跳过：refresh/login 本身、或已重放过 → 不再 refresh，直接清登录态跳登录
      const isAuthEndpoint = url.includes('/auth/refresh') || url.includes('/auth/login')
      if (isAuthEndpoint || config._retried) {
        clearAuthSignal()
        router.push('/login')
        if (!isAuthEndpoint) ElMessage.error(t('登录已过期，请重新登录'))
        return Promise.reject(error)
      }
      // 否则：尝试自动 refresh 一次再重放原请求
      try {
        await doRefresh()
        config._retried = true
        return await http(config)   // 拦截器会返回 response.data
      } catch (refreshErr) {
        clearAuthSignal()
        router.push('/login')
        ElMessage.error(t('登录已过期，请重新登录'))
        return Promise.reject(refreshErr)
      }
    } else if (status === 409) {
      // 乐观锁冲突：由调用方自己决定如何提示用户（弹对话框 / 重新拉取等）
      // 这里不自动 toast，避免和业务级对话框重复
    } else {
      // 后端业务错误码（如 E-FIN-107 / E-PUB-001 / E-SEC-004）走 i18n 翻译为友好文案；
      // 自由文本 message 因 key 不存在原样返回（flatJson 缺失回退=key 本身），安全。
      const raw = error.response?.data?.message
      ElMessage.error((raw ? t(raw) : '') || error.response?.data?.title || t('请求失败'))
    }
    return Promise.reject(error)
  }
)

export default http
