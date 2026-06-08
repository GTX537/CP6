<template>
  <el-container class="h-screen app-container">
    <el-header class="app-header">
      <div class="logo">MSBB 系统</div>
      <div class="user-info">
        <span>当前用户：DefaultUser | 拠点：010</span>
        <el-button type="info" size="small" @click="logout">退出</el-button>
      </div>
    </el-header>

    <el-container>
      <el-aside width="250px" class="app-sidebar">
        <el-menu :router="true" default-active="/" class="h-full">
          <template v-if="menuStore.loading">
            <el-menu-item index="/" disabled>菜单加载中...</el-menu-item>
          </template>
          
          <template v-else-if="menuStore.error">
            <el-menu-item index="/" disabled>错误: {{ menuStore.error }}</el-menu-item>
          </template>
          
          <template v-else>
            <el-sub-menu v-for="group in menuStore.groupedMenus" 
                         :key="group.majorCategoryNO" 
                         :index="group.majorCategoryNO.toString()">
              <template #title>
                <span>{{ group.majorCategoryName }}</span>
              </template>
              
              <el-menu-item v-for="item in group.items" 
                            :key="item.functionID" 
                            :index="item.url">
                {{ item.functionName }} ({{ item.functionID }})
              </el-menu-item>
            </el-sub-menu>
          </template>

        </el-menu>
      </el-aside>

      <el-main class="app-main">
        <router-view />
      </el-main>
    </el-container>
  </el-container>
</template>

<script setup lang="ts">
import { onMounted } from 'vue';
import { useMenuStore } from '../stores/menu';

const menuStore = useMenuStore();

// 组件挂载时，从后端获取菜单数据
onMounted(() => {
    if (menuStore.groupedMenus.length === 0 && !menuStore.loading) {
        menuStore.fetchMenus();
    }
});

const logout = () => {
  alert('退出功能待实现 (SSO/JWT)');
  // 实际项目中应调用后端注销 API 并清除 JWT Token
};
</script>

<style scoped>
.app-header {
  background-color: #409EFF; /* Element Plus Primary Color */
  color: white;
  display: flex;
  justify-content: space-between;
  align-items: center;
}
.logo {
  font-size: 1.25rem;
  font-weight: bold;
}
.user-info span {
  margin-right: 20px;
}
.app-container {
  height: 100vh;
}
.app-sidebar {
  border-right: 1px solid #dcdfe6;
  overflow-y: auto;
}
.app-main {
  padding: 20px;
  background-color: #f0f2f5;
  overflow-y: auto;
}
</style>