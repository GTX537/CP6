// src/stores/menu.ts
import { defineStore } from 'pinia';
import axios from 'axios';

// 定义菜单项的数据结构 (与 MenuDto 对应)
export interface MenuItem {
    majorCategoryNO: number;
    majorCategoryName: string;
    functionNO: number;
    functionName: string;
    functionID: string;
    url: string;
}

// 定义侧边栏所需的结构 (按大分类分组)
export interface GroupedMenu {
    majorCategoryNO: number;
    majorCategoryName: string;
    items: MenuItem[];
}

// 定义 Store
export const useMenuStore = defineStore('menu', {
    state: () => ({
        // 存储分组后的菜单数据
        groupedMenus: [] as GroupedMenu[],
        loading: false,
        error: null as string | null,
    }),
    actions: {
        async fetchMenus() {
            this.loading = true;
            this.error = null;
            const API_URL = 'http://localhost:5000/api/Menu/GetMenus'; // <--- 确保端口号与您的 .NET 8 API 实际运行端口一致！

            try {
                // 调用后端 API
                const response = await axios.get<MenuItem[]>(API_URL);
                
                // 对返回的扁平数据进行分组
                this.groupedMenus = this.groupMenus(response.data);

            } catch (err: any) {
                this.error = 'Failed to fetch menus: ' + (err.message || 'Unknown error');
                console.error(this.error);
            } finally {
                this.loading = false;
            }
        },

        // 将扁平数组转换为按大分类分组的结构
        groupMenus(data: MenuItem[]): GroupedMenu[] {
            // 明确指定对象的键是数字，值是 GroupedMenu
            const groups: { [key: number]: GroupedMenu } = {};

            data.forEach(item => {
                if (!groups[item.majorCategoryNO]) {
                    groups[item.majorCategoryNO] = {
                        majorCategoryNO: item.majorCategoryNO,
                        majorCategoryName: item.majorCategoryName,
                        items: []
                    };
                }
                
                // 修正行：使用非空断言 ! 告诉 TS 编译器，在这里它一定不是 undefined
                groups[item.majorCategoryNO]!.items.push(item); 
            });
            
            // 转换为数组并按大分类排序
            return Object.values(groups).sort((a, b) => a.majorCategoryNO - b.majorCategoryNO);
        }
    }
});