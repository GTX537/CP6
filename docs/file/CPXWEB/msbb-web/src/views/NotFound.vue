<template>
  <el-result
    icon="error"
    title="404 Not Found"
    sub-title="抱歉，您访问的页面不存在或功能ID未配置路由。"
  >
    <template #extra>
      <el-button type="primary" @click="$router.push('/')">返回首页</el-button>
    </template>
  </el-result>
</template>

<script setup lang="ts">
// 确保使用 <script setup> 且语言为 ts
</script>