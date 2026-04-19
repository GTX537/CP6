import axios from 'axios'
import { ElMessage } from 'element-plus'
import router from '@/router'

// 创建 axios 实例，统一配置
const http = axios.create({
  baseURL: '/api', // 通过 Vite 代理转发到后端
  timeout: 10000
})

// 请求拦截器：每次请求自动带上 Token
http.interceptors.request.use((config) => {
  const token = localStorage.getItem('token')
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

// 响应拦截器：统一处理错误
http.interceptors.response.use(
  (response) => response.data,
  (error) => {
    const status = error.response?.status
    if (status === 401) {
      // Token 过期或未登录，跳转到登录页
      localStorage.removeItem('token')
      router.push('/login')
      ElMessage.error('登录已过期，请重新登录')
    } else if (status === 409) {
      // 乐观锁冲突：由调用方自己决定如何提示用户（弹对话框 / 重新拉取等）
      // 这里不自动 toast，避免和业务级对话框重复
    } else {
      ElMessage.error(error.response?.data?.message || error.response?.data?.title || '请求失败')
    }
    return Promise.reject(error)
  }
)

export default http
