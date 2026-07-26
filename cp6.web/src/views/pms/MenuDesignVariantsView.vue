<template>
  <div class="design-page">
    <header class="topbar">
      <div class="brand">
        <span class="brand-mark">CP</span>
        <div><strong>菜单管理 · UI 方案</strong><small>演示数据，不写入系统</small></div>
      </div>

      <nav class="variant-nav" aria-label="设计方案">
        <button
          v-for="variant in variants"
          :key="variant.key"
          type="button"
          :class="{ active: activeVariant === variant.key }"
          @click="activeVariant = variant.key"
        >
          <b>{{ variant.index }}</b>{{ variant.label }}
        </button>
      </nav>

      <div class="top-actions">
        <span class="concept"><i></i>概念预览</span>
        <el-button :icon="Back" @click="router.push('/menu')">返回当前页面</el-button>
      </div>
    </header>

    <main class="stage">
      <!-- A: tree + detail workbench -->
      <section v-if="activeVariant === 'workbench'" class="variant">
        <div class="page-heading">
          <div><span>方案 A · 推荐</span><h1>结构编辑工作台</h1><p>树负责定位，表单负责编辑，导航预览负责确认结果。</p></div>
          <div><el-button :icon="View">预览</el-button><el-button type="primary" :icon="Check">保存更改</el-button></div>
        </div>

        <div class="workbench layout-frame">
          <aside class="tree-panel panel-muted">
            <div class="panel-title"><div><strong>菜单结构</strong><small>12 个目录 · 48 个页面</small></div><el-button circle type="primary" :icon="Plus" title="新增顶级菜单" /></div>
            <el-input v-model="treeKeyword" clearable :prefix-icon="Search" placeholder="搜索名称或路径" />
            <div class="tree-list">
              <button
                v-for="node in filteredTree"
                :key="node.id"
                type="button"
                class="tree-node"
                :class="{ selected: selectedMenuId === node.id }"
                :style="{ '--depth': node.depth }"
                @click="selectedMenuId = node.id"
              >
                <el-icon class="chevron" :class="{ hidden: !node.group }"><ArrowDown /></el-icon>
                <el-icon class="node-icon"><component :is="node.icon" /></el-icon>
                <span><strong>{{ node.name }}</strong><small>{{ node.path || '目录' }}</small></span>
                <em v-if="node.group">{{ node.count }}</em><i v-else :class="{ off: !node.enabled }"></i>
              </button>
            </div>
            <div class="panel-hint"><el-icon><InfoFilled /></el-icon>选择节点后维护详细配置</div>
          </aside>

          <section class="editor-panel">
            <div class="editor-head">
              <div class="identity"><span><el-icon><component :is="selectedMenu.icon" /></el-icon></span><div><div><h2>{{ selectedMenu.name }}</h2><el-tag size="small" :type="selectedMenu.enabled ? 'success' : 'info'">{{ selectedMenu.enabled ? '已启用' : '已停用' }}</el-tag></div><p>菜单 ID {{ selectedMenu.id }} · 10 分钟前更新</p></div></div>
              <el-button circle :icon="MoreFilled" title="更多操作" />
            </div>

            <div class="form-block">
              <div class="block-title"><span><el-icon><EditPen /></el-icon></span><div><strong>基础信息</strong><small>菜单在导航中的展示方式</small></div></div>
              <div class="form-grid">
                <label><span>菜单名称 <b>*</b></span><el-input v-model="selectedMenu.name" /></label>
                <label><span>菜单类型</span><el-segmented v-model="selectedMenu.type" :options="['目录', '页面']" /></label>
                <label><span>路由路径 <b>*</b></span><el-input v-model="selectedMenu.path" placeholder="/example" /><small>需与前端路由地址保持一致</small></label>
                <label><span>显示图标</span><el-select v-model="selectedMenu.iconName"><el-option label="菜单" value="Menu" /><el-option label="用户" value="User" /><el-option label="设置" value="Setting" /></el-select></label>
              </div>
            </div>

            <div class="form-block behavior-block">
              <div class="block-title amber"><span><el-icon><Operation /></el-icon></span><div><strong>导航行为</strong><small>控制可见性与打开方式</small></div></div>
              <div class="setting-list">
                <div><span><strong>在导航中显示</strong><small>关闭后仍可通过路由访问</small></span><el-switch v-model="selectedMenu.enabled" /></div>
                <div><span><strong>保持页面状态</strong><small>保留筛选条件和滚动位置</small></span><el-switch v-model="selectedMenu.keepAlive" /></div>
                <div><span><strong>打开方式</strong><small>选择点击后的呈现位置</small></span><el-radio-group v-model="selectedMenu.openMode"><el-radio-button value="当前页">当前页</el-radio-button><el-radio-button value="新窗口">新窗口</el-radio-button></el-radio-group></div>
              </div>
            </div>

            <footer class="editor-footer"><span><el-icon><CircleCheck /></el-icon>当前配置完整，可保存</span><div><el-button>放弃更改</el-button><el-button type="primary">保存</el-button></div></footer>
          </section>

          <aside class="preview-panel panel-muted">
            <div class="panel-title"><div><strong>导航预览</strong><small>桌面端效果</small></div><el-button text circle :icon="RefreshRight" title="刷新预览" /></div>
            <div class="nav-preview">
              <div class="mini-brand"><b>CP</b><strong>CP6 管理系统</strong></div>
              <small class="nav-label">工作台</small><div class="nav-row"><el-icon><Odometer /></el-icon>仪表盘</div>
              <small class="nav-label">系统管理</small><div class="nav-row nav-group"><el-icon><Setting /></el-icon>系统管理<el-icon><ArrowDown /></el-icon></div>
              <div class="nav-children"><div class="nav-row"><el-icon><User /></el-icon>角色管理</div><div class="nav-row active"><el-icon><Menu /></el-icon>{{ selectedMenu.name }}</div><div class="nav-row"><el-icon><Lock /></el-icon>权限分配</div></div>
            </div>
            <div class="preview-stats"><div><span>所在层级</span><strong>第 2 级</strong></div><div><span>受影响角色</span><strong>8 个</strong></div><div><span>关联权限</span><strong>4 项</strong></div></div>
          </aside>
        </div>
      </section>

      <!-- B: sorting and publishing -->
      <section v-else-if="activeVariant === 'reorder'" class="variant reorder-variant">
        <div class="page-heading dark-heading">
          <div><span>方案 B · 高频运维</span><h1>排序与发布中心</h1><p>适合菜单较多、经常调整顺序和上线批次的场景。</p></div>
          <div><em><i></i>3 项未发布</em><el-button :icon="RefreshLeft">撤销全部</el-button><el-button type="primary" :icon="Promotion">发布菜单</el-button></div>
        </div>

        <div class="reorder-grid layout-frame">
          <aside class="module-panel panel-muted">
            <div class="panel-title"><strong>业务模块</strong><el-button text :icon="Plus">新建</el-button></div>
            <el-input v-model="moduleKeyword" :prefix-icon="Search" placeholder="筛选模块" />
            <div class="module-list">
              <button v-for="module in filteredModules" :key="module.id" type="button" :class="{ active: activeModule === module.id }" @click="activeModule = module.id">
                <span :style="{ background: module.color }"><el-icon><component :is="module.icon" /></el-icon></span><div><strong>{{ module.name }}</strong><small>{{ module.count }} 个菜单</small></div><el-icon><ArrowRight /></el-icon>
              </button>
            </div>
            <div class="health"><span>菜单健康度 <strong>96%</strong></span><el-progress :percentage="96" :stroke-width="6" :show-text="false" color="#1ca67a" /><small>2 个菜单缺少权限绑定</small></div>
          </aside>

          <section class="ordering-panel">
            <div class="ordering-head"><div><small>系统管理 / 子菜单排序</small><h2>调整菜单显示顺序</h2><p>使用箭头调整顺序，变更会自动进入待发布状态。</p></div><div class="view-switch"><button class="active" title="列表"><el-icon><List /></el-icon></button><button title="紧凑"><el-icon><Grid /></el-icon></button></div></div>
            <div class="sort-columns"><span>顺序 / 菜单</span><span>路由</span><span>状态</span><span>操作</span></div>
            <div class="sort-list">
              <div v-for="(item, index) in sortedMenus" :key="item.id" class="sort-row" :class="{ changed: item.changed }">
                <div class="sort-name"><el-icon class="drag"><Rank /></el-icon><b>{{ String(index + 1).padStart(2, '0') }}</b><span><el-icon><component :is="item.icon" /></el-icon></span><div><strong>{{ item.name }}</strong><small>ID {{ item.id }}</small></div></div>
                <code>{{ item.path }}</code>
                <em :class="item.changed ? 'pending' : 'published'"><i></i>{{ item.changed ? '待发布' : '已发布' }}</em>
                <div class="row-buttons"><el-button text circle :icon="Top" :disabled="index === 0" title="上移" @click="moveMenu(index, -1)" /><el-button text circle :icon="Bottom" :disabled="index === sortedMenus.length - 1" title="下移" @click="moveMenu(index, 1)" /><el-button text circle :icon="MoreFilled" /></div>
              </div>
            </div>
            <div class="drop-zone"><el-icon><Plus /></el-icon>拖到这里添加子菜单</div>
          </section>

          <aside class="changes-panel panel-muted">
            <div class="panel-title"><div><strong>本次变更</strong><small>自动记录</small></div><b class="count">3</b></div>
            <div class="change-summary"><span><el-icon><Sort /></el-icon></span><div><strong>菜单顺序已调整</strong><small>影响系统管理模块</small></div></div>
            <div class="timeline"><div class="current"><i></i><small>刚刚</small><strong>权限分配</strong><p>从第 3 位移动到第 2 位</p></div><div><i></i><small>2 分钟前</small><strong>用户管理</strong><p>修改显示名称</p></div><div><i></i><small>5 分钟前</small><strong>数据字典</strong><p>状态改为停用</p></div></div>
            <div class="impact"><strong><el-icon><Warning /></el-icon>发布影响</strong><div><span>在线用户</span><b>24</b></div><div><span>受影响角色</span><b>8</b></div><p>发布后用户刷新页面即可看到最新菜单。</p></div>
            <el-button type="primary" size="large" :icon="Promotion">检查并发布</el-button>
          </aside>
        </div>
      </section>

      <!-- C: menu and permission governance -->
      <section v-else-if="activeVariant === 'governance'" class="variant">
        <div class="page-heading">
          <div><span>方案 C · 强治理</span><h1>菜单与权限治理</h1><p>围绕“谁能看、谁能用、变更是否合规”组织维护动作。</p></div>
          <div><el-button :icon="Document">导出清单</el-button><el-button type="primary" :icon="Check">保存策略</el-button></div>
        </div>

        <div class="kpis"><div><span class="teal"><el-icon><Menu /></el-icon></span><div><small>菜单节点</small><strong>60</strong></div><em>+4 本月</em></div><div><span class="blue"><el-icon><UserFilled /></el-icon></span><div><small>关联角色</small><strong>18</strong></div><em>覆盖 96%</em></div><div><span class="amber"><el-icon><WarningFilled /></el-icon></span><div><small>待处理风险</small><strong>2</strong></div><em class="warn">需要检查</em></div><div><span class="green"><el-icon><CircleCheckFilled /></el-icon></span><div><small>配置完整度</small><strong>97%</strong></div><em>状态良好</em></div></div>

        <div class="governance-grid layout-frame">
          <aside class="domain-panel panel-muted">
            <div class="panel-title"><div><strong>菜单目录</strong><small>按业务域查看</small></div><el-button circle :icon="Plus" /></div>
            <el-input v-model="governanceKeyword" :prefix-icon="Search" placeholder="搜索菜单" />
            <div class="domains"><div v-for="domain in filteredDomains" :key="domain.name"><div class="domain-title"><el-icon><ArrowDown /></el-icon><strong>{{ domain.name }}</strong><em>{{ domain.items.length }}</em></div><button v-for="item in domain.items" :key="item.id" type="button" :class="{ active: governanceMenuId === item.id }" @click="governanceMenuId = item.id"><el-icon><component :is="item.icon" /></el-icon><span><strong>{{ item.name }}</strong><small>{{ item.path }}</small></span><el-icon v-if="item.warning" class="warning-icon"><WarningFilled /></el-icon></button></div></div>
          </aside>

          <section class="permission-panel">
            <div class="permission-head"><div class="identity"><span><el-icon><Menu /></el-icon></span><div><div><h2>菜单管理</h2></div><p>/menu · 菜单 ID 102</p></div></div><el-switch v-model="governanceEnabled" active-text="启用" /></div>
            <div class="policy-tabs"><button :class="{ active: policyTab === 'role' }" @click="policyTab = 'role'">角色可见性 <b>8</b></button><button :class="{ active: policyTab === 'action' }" @click="policyTab = 'action'">功能权限 <b>4</b></button><button :class="{ active: policyTab === 'audit' }" @click="policyTab = 'audit'">审计记录 <b>12</b></button></div>

            <template v-if="policyTab === 'role'">
              <div class="policy-intro"><div><strong>允许以下角色查看该菜单</strong><p>可见性与功能操作权限分开配置。</p></div><el-button :icon="Plus">添加角色</el-button></div>
              <div class="role-table"><div class="role-head"><span>角色</span><span>数据范围</span><span>查看</span><span>新增</span><span>编辑</span><span>删除</span></div><div v-for="role in roles" :key="role.name" class="role-row"><div class="role-name"><b :style="{ background: role.color }">{{ role.initial }}</b><span><strong>{{ role.name }}</strong><small>{{ role.users }} 位用户</small></span></div><el-tag size="small" effect="plain">{{ role.scope }}</el-tag><el-checkbox v-for="permission in permissionKeys" :key="permission" v-model="role.permissions[permission]" /></div></div>
              <div class="inheritance"><el-icon><Connection /></el-icon><div><strong>继承关系正常</strong><p>继承“系统管理”的访问边界，未发现越权角色。</p></div><el-button link type="primary">查看继承链</el-button></div>
            </template>

            <div v-else-if="policyTab === 'action'" class="action-list"><div v-for="action in actions" :key="action.key"><code>{{ action.key }}</code><span><strong>{{ action.name }}</strong><small>{{ action.description }}</small></span><el-switch v-model="action.enabled" /></div></div>
            <div v-else class="audit-list"><div v-for="audit in audits" :key="audit.time"><b>{{ audit.initial }}</b><span><strong>{{ audit.user }} {{ audit.action }}</strong><small>{{ audit.detail }}</small></span><time>{{ audit.time }}</time></div></div>
          </section>

          <aside class="risk-panel panel-muted">
            <div class="panel-title"><div><strong>配置检查</strong><small>实时检测</small></div><el-icon><RefreshRight /></el-icon></div>
            <div class="score"><div><strong>92</strong><small>健康分</small></div><p>整体配置良好，仍有 2 项建议需要处理。</p></div>
            <div class="checks"><div class="passed"><el-icon><CircleCheckFilled /></el-icon><span><strong>路由已注册</strong><small>/menu 可正常访问</small></span></div><div class="passed"><el-icon><CircleCheckFilled /></el-icon><span><strong>父级状态正常</strong><small>系统管理已启用</small></span></div><div class="warning"><el-icon><WarningFilled /></el-icon><span><strong>权限命名不统一</strong><small>发现 1 个旧版权限键</small></span><el-button link type="warning">处理</el-button></div><div class="warning"><el-icon><WarningFilled /></el-icon><span><strong>存在闲置角色</strong><small>2 个角色 90 天未使用</small></span><el-button link type="warning">查看</el-button></div></div>
            <div class="review"><small>最近复核</small><strong>管理员 · 2026-07-18</strong><el-button plain :icon="Stamp">发起复核</el-button></div>
          </aside>
        </div>
      </section>

      <!-- D: truly recursive tree, inspired by the supplied reference -->
      <section v-else class="variant deep-variant">
        <div class="page-heading">
          <div><span>方案 D · 多层结构</span><h1>多层递归树编辑器</h1><p>每个节点都能继续添加子节点，并可通过拖拽改变所属层级。</p></div>
          <div>
            <span v-if="deepDirty" class="deep-dirty"><i></i>有未保存更改</span>
            <el-button :icon="View">预览导航</el-button>
            <el-button type="primary" :icon="Check" @click="saveDeepTree">保存树结构</el-button>
          </div>
        </div>

        <div class="deep-layout layout-frame">
          <aside class="deep-tree-panel panel-muted">
            <div class="deep-tree-heading">
              <div><strong>MENU 菜单树</strong><small>支持任意层级 · 拖拽调整</small></div>
              <el-button type="primary" :icon="Plus" @click="addDeepRoot">新增顶级</el-button>
            </div>

            <div class="deep-tree-tools">
              <el-input v-model="deepKeyword" clearable :prefix-icon="Search" placeholder="搜索名称、功能 ID 或路由" />
              <el-button-group>
                <el-tooltip content="定位当前节点" placement="top"><el-button :icon="Position" title="定位当前节点" @click="focusDeepSelection" /></el-tooltip>
                <el-tooltip content="全部展开" placement="top"><el-button :icon="Expand" title="全部展开" @click="setDeepExpanded(true)" /></el-tooltip>
                <el-tooltip content="全部收起" placement="top"><el-button :icon="Fold" title="全部收起" @click="setDeepExpanded(false)" /></el-tooltip>
              </el-button-group>
            </div>

            <div class="deep-tree-scope">
              <button type="button" :class="{ active: deepNodeFilter === 'all' }" @click="deepNodeFilter = 'all'"><span class="scope-dot all"></span>全部 <b>{{ deepNodeCount }}</b></button>
              <button type="button" :class="{ active: deepNodeFilter === 'folder' }" @click="deepNodeFilter = 'folder'"><span class="scope-dot folder"></span>目录 <b>{{ deepFolderCount }}</b></button>
              <button type="button" :class="{ active: deepNodeFilter === 'page' }" @click="deepNodeFilter = 'page'"><span class="scope-dot page"></span>页面 <b>{{ deepPageCount }}</b></button>
            </div>

            <div class="deep-tree-scroll">
              <el-tree
                ref="deepTreeRef"
                class="deep-tree"
                :data="deepTreeData"
                :props="deepTreeProps"
                node-key="id"
                highlight-current
                draggable
                :expand-on-click-node="false"
                :default-expanded-keys="deepExpandedKeys"
                :current-node-key="deepSelectedId"
                :filter-node-method="filterDeepNode"
                :allow-drop="allowDeepDrop"
                @node-click="selectDeepNode"
                @node-drop="handleDeepDrop"
              >
                <template #default="{ data }">
                  <div class="deep-node-row" :title="data.name">
                    <span class="deep-node-kind" :class="data.type === '目录' ? 'folder' : 'page'">
                      <el-icon><FolderOpened v-if="data.children?.length" /><Document v-else /></el-icon>
                    </span>
                    <span class="deep-node-copy">
                      <strong>{{ data.name }}</strong>
                      <small>{{ data.code || data.path || '未设置功能 ID' }}</small>
                    </span>
                    <span v-if="data.children?.length" class="deep-child-count">{{ data.children.length }}</span>
                    <i class="deep-node-status" :class="{ off: !data.enabled }"></i>
                    <span class="deep-node-actions">
                      <el-tooltip content="新增子节点" placement="top">
                        <el-button text circle :icon="Plus" title="新增子节点" @click.stop="addDeepChild(data)" />
                      </el-tooltip>
                      <el-tooltip content="复制节点" placement="top">
                        <el-button text circle :icon="CopyDocument" title="复制节点" @click.stop="duplicateDeepNode(data)" />
                      </el-tooltip>
                    </span>
                  </div>
                </template>
              </el-tree>
            </div>

            <footer class="deep-tree-footer">
              <span><el-icon><Rank /></el-icon>拖动节点可排序或改变父级</span>
              <strong>{{ deepNodeCount }} 个节点</strong>
            </footer>
          </aside>

          <section class="deep-detail-panel">
            <header class="deep-detail-head">
              <div class="deep-breadcrumb">
                <span v-for="(item, index) in deepBreadcrumb" :key="item.id">
                  {{ item.name }}<el-icon v-if="index < deepBreadcrumb.length - 1"><ArrowRight /></el-icon>
                </span>
              </div>
              <div class="deep-title-row">
                <div class="deep-title-identity">
                  <span :class="deepSelectedNode.type === '目录' ? 'folder' : 'page'"><el-icon><FolderOpened v-if="deepSelectedNode.children?.length" /><Document v-else /></el-icon></span>
                  <div><h2>{{ deepSelectedNode.name }}</h2><p>{{ deepSelectedNode.code }} · 第 {{ deepBreadcrumb.length }} 层</p></div>
                </div>
                <div class="deep-title-actions">
                  <el-tag :type="deepSelectedNode.enabled ? 'success' : 'info'">{{ deepSelectedNode.enabled ? '已启用' : '已停用' }}</el-tag>
                  <el-button :icon="Plus" @click="addDeepChild(deepSelectedNode)">新增子节点</el-button>
                  <el-dropdown>
                    <el-button circle :icon="MoreFilled" title="更多操作" />
                    <template #dropdown><el-dropdown-menu><el-dropdown-item>移动到...</el-dropdown-item><el-dropdown-item>复制节点</el-dropdown-item><el-dropdown-item divided class="danger-item">删除节点</el-dropdown-item></el-dropdown-menu></template>
                  </el-dropdown>
                </div>
              </div>
              <div class="deep-context-strip">
                <span><el-icon><Connection /></el-icon>{{ deepBreadcrumb.length }} 层路径</span>
                <span><el-icon><FolderOpened /></el-icon>{{ deepDescendantCount }} 个下级节点</span>
                <span><el-icon><User /></el-icon>影响 8 个角色</span>
              </div>
            </header>

            <div class="deep-detail-content">
              <section class="deep-form-section">
                <div class="deep-section-title"><span><el-icon><EditPen /></el-icon></span><div><strong>节点信息</strong><small>维护当前菜单节点的标识与导航位置</small></div></div>
                <div class="deep-form-grid">
                  <label><span>菜单名称 <b>*</b></span><el-input v-model="deepSelectedNode.name" @input="deepDirty = true" /></label>
                  <label><span>节点类型</span><el-segmented v-model="deepSelectedNode.type" :options="['目录', '页面', '外链']" @change="deepDirty = true" /></label>
                  <label><span>功能 ID <b>*</b></span><el-input v-model="deepSelectedNode.code" @input="deepDirty = true"><template #prefix><span class="field-prefix">ID</span></template></el-input></label>
                  <label><span>父级菜单</span><el-input :model-value="deepParentName" disabled><template #prefix><el-icon><Folder /></el-icon></template></el-input></label>
                  <label class="span-two"><span>执行路由</span><el-input v-model="deepSelectedNode.path" placeholder="/module/page" @input="deepDirty = true"><template #prefix><span class="field-prefix">URL</span></template></el-input><small>页面节点填写前端路由；目录节点可以留空</small></label>
                  <label><span>图标</span><el-select v-model="deepSelectedNode.iconName" @change="deepDirty = true"><el-option label="文件夹" value="Folder" /><el-option label="文档" value="Document" /><el-option label="用户" value="User" /><el-option label="设置" value="Setting" /></el-select></label>
                  <label><span>同级排序</span><el-input-number v-model="deepSelectedNode.orderNo" :min="0" controls-position="right" @change="deepDirty = true" /></label>
                </div>
              </section>

              <section class="deep-form-section deep-behavior">
                <div class="deep-section-title amber"><span><el-icon><Operation /></el-icon></span><div><strong>展示与行为</strong><small>控制该节点在系统导航中的呈现方式</small></div></div>
                <div class="deep-behavior-grid">
                  <div><span><strong>启用节点</strong><small>停用后节点及其下级不会出现在菜单中</small></span><el-switch v-model="deepSelectedNode.enabled" @change="deepDirty = true" /></div>
                  <div><span><strong>默认展开</strong><small>用户进入系统时自动展开该目录</small></span><el-switch v-model="deepSelectedNode.defaultOpen" @change="deepDirty = true" /></div>
                  <div><span><strong>在新窗口打开</strong><small>适用于外部系统或独立工作台</small></span><el-switch v-model="deepSelectedNode.newWindow" @change="deepDirty = true" /></div>
                  <div><span><strong>显示导航图标</strong><small>关闭后仅显示菜单文字</small></span><el-switch v-model="deepSelectedNode.showIcon" @change="deepDirty = true" /></div>
                </div>
                <div v-if="deepDescendantCount" class="deep-impact-note"><el-icon><InfoFilled /></el-icon><span><strong>层级影响提示</strong><small>停用或移动当前节点时，会同时影响其下 {{ deepDescendantCount }} 个节点。</small></span></div>
              </section>

              <section class="deep-form-section child-section">
                <div class="child-section-head">
                  <div class="deep-section-title"><span><el-icon><Connection /></el-icon></span><div><strong>直属子节点</strong><small>当前节点下一级的菜单，共 {{ deepSelectedNode.children?.length || 0 }} 个</small></div></div>
                  <el-button :icon="Plus" @click="addDeepChild(deepSelectedNode)">添加子节点</el-button>
                </div>
                <div v-if="deepSelectedNode.children?.length" class="deep-child-list">
                  <div v-for="(child, index) in deepSelectedNode.children" :key="child.id">
                    <el-icon class="child-drag"><Rank /></el-icon>
                    <span class="deep-node-kind" :class="child.type === '目录' ? 'folder' : 'page'"><el-icon><FolderOpened v-if="child.children?.length" /><Document v-else /></el-icon></span>
                    <span><strong>{{ child.name }}</strong><small>{{ child.code || child.path }}</small></span>
                    <el-tag size="small" effect="plain">{{ child.type }}</el-tag>
                    <em>顺序 {{ index + 1 }}</em>
                    <el-button text :icon="ArrowRight" @click="focusDeepNode(child)">进入</el-button>
                  </div>
                </div>
                <div v-else class="deep-empty"><el-icon><Folder /></el-icon><strong>暂无子节点</strong><p>这个节点仍然可以继续添加下一级菜单。</p><el-button :icon="Plus" @click="addDeepChild(deepSelectedNode)">创建第一个子节点</el-button></div>
              </section>
            </div>

            <footer class="deep-detail-footer">
              <span><el-icon><InfoFilled /></el-icon>所有操作仅作用于方案预览，不会写入正式菜单。</span>
              <div><el-button @click="resetDeepDemo">重置演示</el-button><el-button type="primary" :icon="Check" @click="saveDeepTree">保存更改</el-button></div>
            </footer>
          </section>
        </div>
      </section>
    </main>
  </div>
</template>

<script setup lang="ts">
import { computed, nextTick, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import {
  ArrowDown, ArrowRight, Back, Bottom, Check, CircleCheck, CircleCheckFilled,
  Connection, CopyDocument, DataBoard, Document, EditPen, Expand, Fold, Folder, FolderOpened,
  Grid, InfoFilled, List, Lock, Menu, Money, MoreFilled, Odometer, Operation, Plus, Position, Promotion, Rank, RefreshLeft,
  RefreshRight, Search, Setting, ShoppingBag, Sort, Stamp, Tickets, Top, User,
  UserFilled, View, Warning, WarningFilled
} from '@element-plus/icons-vue'

type VariantKey = 'workbench' | 'reorder' | 'governance' | 'deep-tree'
type PermissionKey = 'view' | 'create' | 'edit' | 'delete'

const router = useRouter()
const activeVariant = ref<VariantKey>('workbench')
const treeKeyword = ref('')
const moduleKeyword = ref('')
const governanceKeyword = ref('')
const selectedMenuId = ref(102)
const activeModule = ref(100)
const governanceMenuId = ref(102)
const governanceEnabled = ref(true)
const policyTab = ref<'role' | 'action' | 'audit'>('role')

const variants: { key: VariantKey; index: string; label: string }[] = [
  { key: 'workbench', index: 'A', label: '结构工作台' },
  { key: 'reorder', index: 'B', label: '排序与发布' },
  { key: 'governance', index: 'C', label: '权限治理' },
  { key: 'deep-tree', index: 'D', label: '递归树' }
]

const menus = reactive([
  { id: 100, name: '系统管理', path: '', depth: 0, group: true, count: 8, enabled: true, icon: Setting, iconName: 'Setting', type: '目录', keepAlive: false, openMode: '当前页' },
  { id: 101, name: '角色管理', path: '/role', depth: 1, group: false, count: 0, enabled: true, icon: User, iconName: 'User', type: '页面', keepAlive: true, openMode: '当前页' },
  { id: 102, name: '菜单管理', path: '/menu', depth: 1, group: false, count: 0, enabled: true, icon: Menu, iconName: 'Menu', type: '页面', keepAlive: true, openMode: '当前页' },
  { id: 103, name: '权限分配', path: '/permission', depth: 1, group: false, count: 0, enabled: true, icon: Lock, iconName: 'Setting', type: '页面', keepAlive: false, openMode: '当前页' },
  { id: 104, name: '用户管理', path: '/user', depth: 1, group: false, count: 0, enabled: true, icon: UserFilled, iconName: 'User', type: '页面', keepAlive: true, openMode: '当前页' },
  { id: 105, name: '多语言管理', path: '/lang', depth: 1, group: false, count: 0, enabled: true, icon: Tickets, iconName: 'Menu', type: '页面', keepAlive: false, openMode: '当前页' },
  { id: 200, name: '贩卖管理 (ERP)', path: '', depth: 0, group: true, count: 12, enabled: true, icon: ShoppingBag, iconName: 'Setting', type: '目录', keepAlive: false, openMode: '当前页' },
  { id: 700, name: '采购管理 (Pur)', path: '', depth: 0, group: true, count: 10, enabled: true, icon: DataBoard, iconName: 'Setting', type: '目录', keepAlive: false, openMode: '当前页' },
  { id: 600, name: '财务管理 (Fin)', path: '', depth: 0, group: true, count: 14, enabled: false, icon: Money, iconName: 'Setting', type: '目录', keepAlive: false, openMode: '当前页' }
])

const filteredTree = computed(() => {
  const keyword = treeKeyword.value.trim().toLowerCase()
  return keyword ? menus.filter(item => `${item.name} ${item.path}`.toLowerCase().includes(keyword)) : menus
})
const selectedMenu = computed(() => menus.find(item => item.id === selectedMenuId.value) ?? menus[0]!)

const modules = [
  { id: 100, name: '系统管理', count: 8, icon: Setting, color: '#dff7f4' },
  { id: 200, name: '贩卖管理 (ERP)', count: 12, icon: ShoppingBag, color: '#e6efff' },
  { id: 700, name: '采购管理 (Pur)', count: 10, icon: DataBoard, color: '#fff0da' },
  { id: 600, name: '财务管理 (Fin)', count: 14, icon: Money, color: '#f0eaff' },
  { id: 300, name: '制造执行 (MES)', count: 16, icon: Operation, color: '#e6f4e9' }
]
const filteredModules = computed(() => {
  const keyword = moduleKeyword.value.trim().toLowerCase()
  return keyword ? modules.filter(item => item.name.toLowerCase().includes(keyword)) : modules
})

const sortedMenus = ref([
  { id: 101, name: '角色管理', path: '/role', icon: User, changed: false },
  { id: 102, name: '菜单管理', path: '/menu', icon: Menu, changed: false },
  { id: 103, name: '权限分配', path: '/permission', icon: Lock, changed: true },
  { id: 104, name: '用户管理', path: '/user', icon: UserFilled, changed: false },
  { id: 105, name: '多语言管理', path: '/lang', icon: Tickets, changed: false },
  { id: 106, name: '数据字典', path: '/dict', icon: List, changed: true }
])
function moveMenu(index: number, offset: number) {
  const target = index + offset
  if (target < 0 || target >= sortedMenus.value.length) return
  const [item] = sortedMenus.value.splice(index, 1)
  if (!item) return
  item.changed = true
  sortedMenus.value.splice(target, 0, item)
}

const domains = [
  { name: '平台与系统', items: [
    { id: 101, name: '角色管理', path: '/role', icon: User, warning: false },
    { id: 102, name: '菜单管理', path: '/menu', icon: Menu, warning: false },
    { id: 103, name: '权限分配', path: '/permission', icon: Lock, warning: true },
    { id: 104, name: '用户管理', path: '/user', icon: UserFilled, warning: false }
  ]},
  { name: '数据与审计', items: [
    { id: 105, name: '数据字典', path: '/dict', icon: List, warning: false },
    { id: 106, name: '操作日志', path: '/operlog', icon: Document, warning: false }
  ]}
]
const filteredDomains = computed(() => {
  const keyword = governanceKeyword.value.trim().toLowerCase()
  if (!keyword) return domains
  return domains.map(domain => ({ ...domain, items: domain.items.filter(item => `${item.name} ${item.path}`.toLowerCase().includes(keyword)) })).filter(domain => domain.items.length)
})

const permissionKeys: PermissionKey[] = ['view', 'create', 'edit', 'delete']
const roles = reactive([
  { name: '系统管理员', initial: '管', users: 3, scope: '全部数据', color: '#1dbac2', permissions: { view: true, create: true, edit: true, delete: true } },
  { name: '业务主管', initial: '主', users: 12, scope: '本部门', color: '#4777d9', permissions: { view: true, create: true, edit: true, delete: false } },
  { name: '审计专员', initial: '审', users: 4, scope: '全部数据', color: '#9a68c7', permissions: { view: true, create: false, edit: false, delete: false } },
  { name: '普通用户', initial: '用', users: 86, scope: '本人数据', color: '#7c8996', permissions: { view: false, create: false, edit: false, delete: false } }
])
const actions = reactive([
  { key: 'menu:view', name: '查看菜单', description: '读取菜单树和菜单详情', enabled: true },
  { key: 'menu:create', name: '新建菜单', description: '创建顶级菜单或子菜单', enabled: true },
  { key: 'menu:update', name: '编辑菜单', description: '修改菜单配置与显示顺序', enabled: true },
  { key: 'menu:delete', name: '删除菜单', description: '删除未被角色引用的菜单', enabled: true }
])
const audits = [
  { initial: '管', user: '系统管理员', action: '修改了角色可见性', detail: '移除“普通用户”的查看权限', time: '今天 09:42' },
  { initial: '陈', user: '陈经理', action: '更新了菜单名称', detail: '菜单维护 → 菜单管理', time: '昨天 16:18' },
  { initial: '管', user: '系统管理员', action: '完成权限复核', detail: '复核结果：通过', time: '07-12 11:05' }
]

interface DeepMenuNode {
  id: number
  name: string
  code: string
  path: string
  type: '目录' | '页面' | '外链'
  iconName: string
  orderNo: number
  enabled: boolean
  defaultOpen: boolean
  newWindow: boolean
  showIcon: boolean
  children: DeepMenuNode[]
}

function makeDeepNode(
  id: number,
  name: string,
  code: string,
  type: DeepMenuNode['type'],
  path = '',
  children: DeepMenuNode[] = []
): DeepMenuNode {
  return {
    id,
    name,
    code,
    type,
    path,
    iconName: type === '目录' ? 'Folder' : 'Document',
    orderNo: 0,
    enabled: true,
    defaultOpen: false,
    newWindow: false,
    showIcon: true,
    children
  }
}

function createDeepTreeData(): DeepMenuNode[] {
  return [
    makeDeepNode(1, '系统管理', 'SYS', '目录', '', [
      makeDeepNode(11, '权限与用户', 'SYS-AUTH', '目录', '', [
        makeDeepNode(111, '菜单与权限', 'SYS-MENU', '目录', '', [
          makeDeepNode(1111, '角色管理', 'SYS-ROLE', '页面', '/role'),
          makeDeepNode(1112, '菜单管理', 'SYS-MENU-EDIT', '页面', '/menu'),
          makeDeepNode(1113, '权限分配', 'SYS-PERMISSION', '页面', '/permission')
        ]),
        makeDeepNode(112, '用户与组织', 'SYS-USER', '目录', '', [
          makeDeepNode(1121, '用户管理', 'SYS-USER-EDIT', '页面', '/user'),
          makeDeepNode(1122, '部门管理', 'SYS-DEPT', '页面', '/pub/dept')
        ])
      ]),
      makeDeepNode(12, '数据与审计', 'SYS-AUDIT', '目录', '', [
        makeDeepNode(121, '数据字典', 'SYS-DICT', '页面', '/dict'),
        makeDeepNode(122, '操作日志', 'SYS-LOG', '页面', '/operlog')
      ])
    ]),
    makeDeepNode(2, '人力资源', 'HR', '目录', '', [
      makeDeepNode(21, '薪资管理', 'HR-PAY', '目录', '', [
        makeDeepNode(211, '奖金申请', 'HR-BONUS', '目录', '', [
          makeDeepNode(2111, '警卫绩效奖金申请表', 'SFSEF2041', '页面', '/hr/bonus/guard'),
          makeDeepNode(2112, '员工绩效奖金申请表', 'HR-BONUS-EMP', '页面', '/hr/bonus/employee'),
          makeDeepNode(2113, '部门奖金复核表', 'HR-BONUS-REVIEW', '页面', '/hr/bonus/review')
        ]),
        makeDeepNode(212, '薪资调整', 'HR-SALARY', '目录', '', [
          makeDeepNode(2121, '调薪申请单', 'HR-SALARY-APPLY', '页面', '/hr/salary/apply'),
          makeDeepNode(2122, '调薪复核单', 'HR-SALARY-REVIEW', '页面', '/hr/salary/review')
        ])
      ]),
      makeDeepNode(22, '人资合规', 'HR-COMPLIANCE', '目录', '', [
        makeDeepNode(221, '人员异动', 'HR-TRANSFER', '页面', '/hr/transfer'),
        makeDeepNode(222, '离职管理', 'HR-OFFBOARD', '页面', '/hr/offboard')
      ]),
      makeDeepNode(23, '教育训练', 'HR-TRAINING', '目录')
    ]),
    makeDeepNode(3, '采购管理', 'PUR', '目录', '', [
      makeDeepNode(31, '采购申请', 'PUR-PR', '目录', '', [
        makeDeepNode(311, '采购申请单', 'PUR-PR-EDIT', '页面', '/pur/pr'),
        makeDeepNode(312, '采购申请审批', 'PUR-PR-APPROVE', '页面', '/pur/pr/approve')
      ]),
      makeDeepNode(32, '采购订单', 'PUR-PO', '页面', '/pur/po')
    ]),
    makeDeepNode(4, '财务管理', 'FIN', '目录', '', [
      makeDeepNode(41, '应付管理', 'FIN-AP', '目录', '', [
        makeDeepNode(411, '应付发票', 'FIN-AP-INVOICE', '页面', '/fin/ap-invoice'),
        makeDeepNode(412, '付款核销', 'FIN-AP-PAYMENT', '页面', '/fin/ap-payment')
      ])
    ])
  ]
}

const deepTreeProps = { children: 'children', label: 'name' }
const deepTreeRef = ref<any>()
const deepTreeData = reactive<DeepMenuNode[]>(createDeepTreeData())
const deepExpandedKeys = [2, 21, 211]
const deepKeyword = ref('')
const deepNodeFilter = ref<'all' | 'folder' | 'page'>('all')
const deepSelectedId = ref(2111)
const deepDirty = ref(false)
let nextDeepId = 9000

function findDeepNode(
  nodes: DeepMenuNode[],
  id: number,
  parent: DeepMenuNode | null = null,
  path: DeepMenuNode[] = []
): { node: DeepMenuNode; parent: DeepMenuNode | null; path: DeepMenuNode[] } | null {
  for (const node of nodes) {
    const nextPath = [...path, node]
    if (node.id === id) return { node, parent, path: nextPath }
    const found = findDeepNode(node.children, id, node, nextPath)
    if (found) return found
  }
  return null
}

function countDeepNodes(nodes: DeepMenuNode[]): number {
  return nodes.reduce((total, node) => total + 1 + countDeepNodes(node.children), 0)
}

const deepSelectedInfo = computed(() => findDeepNode(deepTreeData, deepSelectedId.value))
const deepSelectedNode = computed<DeepMenuNode>(() => deepSelectedInfo.value?.node ?? deepTreeData[0]!)
const deepBreadcrumb = computed(() => deepSelectedInfo.value?.path ?? [deepSelectedNode.value])
const deepParentName = computed(() => deepSelectedInfo.value?.parent?.name ?? '顶级菜单')
const deepNodeCount = computed(() => countDeepNodes(deepTreeData))
const deepFolderCount = computed(() => countDeepNodesByType(deepTreeData, '目录'))
const deepPageCount = computed(() => deepNodeCount.value - deepFolderCount.value)
const deepDescendantCount = computed(() => countDeepNodes(deepSelectedNode.value.children))

watch([deepKeyword, deepNodeFilter], ([keyword, type]) => {
  deepTreeRef.value?.filter({ keyword, type })
})

function countDeepNodesByType(nodes: DeepMenuNode[], type: DeepMenuNode['type']): number {
  return nodes.reduce((total, node) => total + (node.type === type ? 1 : 0) + countDeepNodesByType(node.children, type), 0)
}

function filterDeepNode(value: { keyword: string; type: 'all' | 'folder' | 'page' } | undefined, data: DeepMenuNode) {
  if (!value) return true
  const keyword = value.keyword.trim().toLowerCase()
  const matchesKeyword = !keyword || `${data.name} ${data.code} ${data.path}`.toLowerCase().includes(keyword)
  const matchesType = value.type === 'all' || (value.type === 'folder' ? data.type === '目录' : data.type !== '目录')
  return matchesKeyword && matchesType
}

function selectDeepNode(data: DeepMenuNode) {
  deepSelectedId.value = data.id
}

function focusDeepNode(data: DeepMenuNode) {
  deepSelectedId.value = data.id
  nextTick(() => deepTreeRef.value?.setCurrentKey(data.id))
}

function focusDeepSelection() {
  const info = findDeepNode(deepTreeData, deepSelectedId.value)
  if (!info) return
  setDeepExpanded(false)
  nextTick(() => {
    info.path.slice(0, -1).forEach(item => {
      const node = deepTreeRef.value?.getNode(item.id)
      if (node) node.expanded = true
    })
    deepTreeRef.value?.setCurrentKey(deepSelectedId.value)
    const scroller = document.querySelector<HTMLElement>('.deep-tree-scroll')
    const current = document.querySelector<HTMLElement>('.deep-tree .is-current')
    if (scroller && current) {
      const scrollerRect = scroller.getBoundingClientRect()
      const currentRect = current.getBoundingClientRect()
      scroller.scrollTo({
        top: scroller.scrollTop + currentRect.top - scrollerRect.top - scrollerRect.height / 2 + currentRect.height / 2,
        behavior: 'smooth'
      })
    }
  })
}

function addDeepChild(parent: DeepMenuNode) {
  const child = makeDeepNode(++nextDeepId, '新建子菜单', `MENU-${nextDeepId}`, '页面', '/new-page')
  child.orderNo = parent.children.length
  parent.children.push(child)
  deepDirty.value = true
  nextTick(() => {
    const parentNode = deepTreeRef.value?.getNode(parent.id)
    if (parentNode) parentNode.expanded = true
    focusDeepNode(child)
  })
  ElMessage.success(`已在“${parent.name}”下添加演示子节点`)
}

function addDeepRoot() {
  const node = makeDeepNode(++nextDeepId, '新建顶级菜单', `ROOT-${nextDeepId}`, '目录')
  node.orderNo = deepTreeData.length
  deepTreeData.push(node)
  deepDirty.value = true
  nextTick(() => focusDeepNode(node))
}

function cloneDeepNode(source: DeepMenuNode, root = true): DeepMenuNode {
  const clone = makeDeepNode(
    ++nextDeepId,
    root ? `${source.name} - 副本` : source.name,
    `${source.code}-COPY`,
    source.type,
    source.path,
    source.children.map(child => cloneDeepNode(child, false))
  )
  clone.iconName = source.iconName
  clone.orderNo = source.orderNo + 1
  clone.enabled = source.enabled
  clone.defaultOpen = source.defaultOpen
  clone.newWindow = source.newWindow
  clone.showIcon = source.showIcon
  return clone
}

function duplicateDeepNode(source: DeepMenuNode) {
  const info = findDeepNode(deepTreeData, source.id)
  if (!info) return
  const siblings = info.parent?.children ?? deepTreeData
  const index = siblings.findIndex(item => item.id === source.id)
  const clone = cloneDeepNode(source)
  siblings.splice(index + 1, 0, clone)
  deepDirty.value = true
  nextTick(() => focusDeepNode(clone))
  ElMessage.success('已复制节点及其全部下级')
}

function walkDeepTree(nodes: DeepMenuNode[], visitor: (node: DeepMenuNode) => void) {
  nodes.forEach(node => {
    visitor(node)
    walkDeepTree(node.children, visitor)
  })
}

function setDeepExpanded(expanded: boolean) {
  walkDeepTree(deepTreeData, item => {
    const node = deepTreeRef.value?.getNode(item.id)
    if (node) node.expanded = expanded
  })
}

function allowDeepDrop() {
  return true
}

function handleDeepDrop() {
  deepDirty.value = true
  ElMessage.success('树结构已调整，保存后生效')
}

function saveDeepTree() {
  deepDirty.value = false
  ElMessage.success('D 方案演示数据已保存')
}

function resetDeepDemo() {
  deepTreeData.splice(0, deepTreeData.length, ...createDeepTreeData())
  deepSelectedId.value = 2111
  deepDirty.value = false
  nextTick(() => {
    setDeepExpanded(false)
    deepExpandedKeys.forEach(id => {
      const node = deepTreeRef.value?.getNode(id)
      if (node) node.expanded = true
    })
    deepTreeRef.value?.setCurrentKey(deepSelectedId.value)
  })
}
</script>

<style scoped>
:global(body) { margin: 0; background: #edf3f4; color: #1f3940; }
:global(*) { box-sizing: border-box; }
.design-page { min-height: 100vh; background: #edf3f4; color: #1f3940; font-family: var(--cp-font, 'Microsoft YaHei', Arial, sans-serif); }
button { font: inherit; }
.topbar { min-height: 72px; padding: 11px 24px; display: grid; grid-template-columns: minmax(250px,1fr) auto minmax(250px,1fr); align-items: center; gap: 18px; position: sticky; top: 0; z-index: 20; background: #fff; border-bottom: 1px solid #dce6e8; }
.brand, .top-actions, .page-heading, .page-heading > div:last-child, .panel-title, .identity, .identity > div > div, .block-title, .editor-footer, .nav-group, .sort-name, .row-buttons, .permission-head, .policy-intro, .inheritance { display: flex; align-items: center; }
.brand { gap: 11px; }.brand-mark { width: 40px; height: 40px; display: grid; place-items: center; border-radius: 7px; background: #20bbc3; color: #fff; font-size: 16px; font-weight: 800; box-shadow: 0 7px 18px rgba(32,187,195,.22); }.brand strong,.brand small { display: block; }.brand strong { font-size: 15px; }.brand small { margin-top: 3px; color: #829398; font-size: 10px; }
.variant-nav { display: flex; gap: 4px; padding: 4px; border-radius: 7px; background: #edf3f4; }.variant-nav button { height: 36px; padding: 0 14px; display: flex; align-items: center; gap: 7px; border: 0; border-radius: 5px; background: transparent; color: #60767c; font-size: 12px; font-weight: 700; cursor: pointer; }.variant-nav button:hover { background: rgba(255,255,255,.7); }.variant-nav button.active { background: #fff; color: #0b7e84; box-shadow: 0 2px 7px rgba(39,69,76,.1); }.variant-nav b { width: 19px; height: 19px; display: grid; place-items: center; border-radius: 4px; background: #dbe6e8; font-size: 9px; }.variant-nav .active b { background: #1cb3ba; color: #fff; }
.top-actions { justify-content: flex-end; gap: 12px; }.concept { display: flex; align-items: center; gap: 6px; color: #71858a; font-size: 10px; }.concept i { width: 7px; height: 7px; border-radius: 50%; background: #e7a630; box-shadow: 0 0 0 4px #fff1d8; }
.stage { padding: 18px 24px 28px; }.variant { max-width: 1720px; margin: 0 auto; }.page-heading { min-height: 92px; justify-content: space-between; gap: 20px; padding-bottom: 17px; }.page-heading > div:first-child > span { color: #11989f; font-size: 10px; font-weight: 800; }.page-heading h1 { margin: 5px 0 0; font-size: 24px; letter-spacing: 0; }.page-heading p { margin: 4px 0 0; color: #74878c; font-size: 11px; }.page-heading > div:last-child { gap: 9px; flex-wrap: wrap; justify-content: flex-end; }
.layout-frame { background: #fff; border: 1px solid #dbe5e7; border-radius: 6px; overflow: hidden; box-shadow: 0 11px 32px rgba(40,70,77,.06); }.panel-muted { background: #f8fbfb; }.panel-title { min-height: 70px; padding: 15px 16px; justify-content: space-between; gap: 10px; }.panel-title strong,.panel-title small { display: block; }.panel-title strong { font-size: 13px; }.panel-title small { margin-top: 3px; color: #8a999d; font-size: 9px; }

.workbench { min-height: calc(100vh - 200px); display: grid; grid-template-columns: minmax(260px,310px) minmax(530px,1fr) minmax(225px,260px); }.tree-panel { display: flex; min-width: 0; flex-direction: column; border-right: 1px solid #e2e9eb; }.tree-panel > .el-input,.module-panel > .el-input,.domain-panel > .el-input { width: calc(100% - 28px); margin: 0 14px 13px; }.tree-list { flex: 1; padding: 2px 9px 12px; overflow: auto; }.tree-node { --depth: 0; width: 100%; min-height: 51px; padding: 6px 9px 6px calc(8px + var(--depth) * 18px); display: flex; align-items: center; gap: 7px; border: 1px solid transparent; border-radius: 5px; background: transparent; color: #496168; text-align: left; cursor: pointer; }.tree-node:hover { background: #edf5f5; }.tree-node.selected { border-color: #b9e4e5; background: #e1f6f6; color: #087c82; }.tree-node .chevron { flex: 0 0 13px; color: #8fa0a4; font-size: 10px; }.tree-node .hidden { visibility: hidden; }.tree-node .node-icon { flex: 0 0 20px; font-size: 15px; }.tree-node > span { flex: 1; min-width: 0; }.tree-node strong,.tree-node small { display: block; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }.tree-node strong { font-size: 11px; }.tree-node small { margin-top: 3px; color: #8d9c9f; font-size: 8px; }.tree-node em { min-width: 20px; padding: 2px 5px; border-radius: 9px; background: #e5edef; color: #6e8186; text-align: center; font-size: 8px; font-style: normal; }.tree-node > i { width: 6px; height: 6px; border-radius: 50%; background: #28b27f; }.tree-node > i.off { background: #b5c1c4; }.panel-hint { padding: 11px 14px; display: flex; align-items: center; gap: 6px; border-top: 1px solid #e3eaec; color: #849499; font-size: 9px; }
.editor-panel { display: flex; min-width: 0; flex-direction: column; }.editor-head { min-height: 88px; padding: 16px 22px; display: flex; align-items: center; justify-content: space-between; border-bottom: 1px solid #e4ebed; }.identity { gap: 11px; min-width: 0; }.identity > span { width: 41px; height: 41px; display: grid; place-items: center; flex: 0 0 41px; border-radius: 6px; background: #e1f5f5; color: #108f95; font-size: 20px; }.identity h2 { margin: 0; font-size: 17px; }.identity > div > div { gap: 8px; }.identity p { margin: 3px 0 0; color: #8b999d; font-size: 9px; }.form-block { padding: 21px 22px 23px; border-bottom: 1px solid #e9edef; }.behavior-block { flex: 1; }.block-title { gap: 9px; margin-bottom: 17px; }.block-title > span { width: 32px; height: 32px; display: grid; place-items: center; border-radius: 5px; background: #e1f5f5; color: #0d8a90; }.block-title.amber > span { background: #fff1d8; color: #b67711; }.block-title strong,.block-title small { display: block; }.block-title strong { font-size: 12px; }.block-title small { margin-top: 3px; color: #8a999d; font-size: 9px; }.form-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 16px 18px; }.form-grid label > span { display: block; margin-bottom: 6px; color: #52676d; font-size: 10px; font-weight: 700; }.form-grid label > span b { color: #e35b5b; }.form-grid label > small { display: block; margin-top: 5px; color: #93a0a4; font-size: 8px; }.form-grid .el-select,.form-grid .el-segmented { width: 100%; }.setting-list { border: 1px solid #e1e8ea; border-radius: 5px; overflow: hidden; }.setting-list > div { min-height: 63px; padding: 10px 13px; display: flex; align-items: center; justify-content: space-between; gap: 15px; border-bottom: 1px solid #e9edef; }.setting-list > div:last-child { border-bottom: 0; }.setting-list span { min-width: 0; }.setting-list strong,.setting-list small { display: block; }.setting-list strong { font-size: 10px; }.setting-list small { margin-top: 3px; color: #89999d; font-size: 8px; }.editor-footer { min-height: 66px; padding: 12px 22px; justify-content: space-between; gap: 15px; background: #fafcfc; }.editor-footer > span { display: flex; align-items: center; gap: 6px; color: #288668; font-size: 9px; }
.preview-panel { border-left: 1px solid #e2e9eb; }.nav-preview { margin: 12px; min-height: 342px; padding: 14px 9px; border: 1px solid #dde7e9; border-radius: 5px; background: #fff; box-shadow: 0 5px 15px rgba(40,69,76,.05); }.mini-brand { min-height: 44px; padding: 2px 7px 13px; display: flex; align-items: center; gap: 7px; border-bottom: 1px solid #edf1f2; }.mini-brand b { width: 27px; height: 27px; display: grid; place-items: center; border-radius: 5px; background: #1bb4bc; color: #fff; font-size: 9px; }.mini-brand strong { font-size: 9px; }.nav-label { display: block; margin: 15px 9px 5px; color: #9ba7aa; font-size: 7px; font-weight: 800; }.nav-row { min-height: 32px; padding: 6px 9px; display: flex; align-items: center; gap: 8px; border-radius: 4px; color: #61757a; font-size: 9px; }.nav-row > .el-icon:last-child { margin-left: auto; }.nav-group { color: #344f55; font-weight: 700; }.nav-children { padding-left: 12px; }.nav-row.active { background: #e0f5f5; color: #087c82; font-weight: 700; }.preview-stats { margin: 15px 14px; }.preview-stats > div { padding: 9px 0; display: flex; justify-content: space-between; border-bottom: 1px solid #e1e8ea; font-size: 9px; }.preview-stats span { color: #809196; }

.reorder-variant { margin-top: -18px; }.dark-heading { min-height: 112px; margin: 0 -24px 18px; padding: 17px 26px; background: #25373f; color: #fff; }.dark-heading p { color: #b5c3c7; }.dark-heading > div:first-child > span { color: #5dd0ca; }.dark-heading em { display: flex; align-items: center; gap: 6px; color: #f2cb82; font-size: 9px; font-style: normal; }.dark-heading em i { width: 6px; height: 6px; border-radius: 50%; background: #eeb044; }.reorder-grid { min-height: calc(100vh - 184px); display: grid; grid-template-columns: 245px minmax(540px,1fr) 280px; }.module-panel { padding: 0 13px 18px; border-right: 1px solid #e1e8ea; }.module-panel .panel-title { padding-left: 2px; padding-right: 2px; }.module-list { display: grid; gap: 5px; }.module-list button { min-height: 55px; padding: 7px; display: flex; align-items: center; gap: 8px; border: 1px solid transparent; border-radius: 5px; background: transparent; color: #52676e; text-align: left; cursor: pointer; }.module-list button:hover { background: #eef4f5; }.module-list button.active { border-color: #b9dfe0; background: #e4f4f3; color: #087b80; }.module-list button > span { width: 33px; height: 33px; display: grid; place-items: center; flex: 0 0 33px; border-radius: 5px; }.module-list button > div { min-width: 0; flex: 1; }.module-list strong,.module-list small { display: block; }.module-list strong { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 10px; }.module-list small { margin-top: 3px; color: #8c999d; font-size: 8px; }.health { margin-top: 19px; padding: 12px; border: 1px solid #dfe7e9; border-radius: 5px; background: #fff; }.health > span { display: block; color: #74878c; font-size: 9px; }.health > span strong { float: right; color: #228664; }.health .el-progress { padding-top: 8px; clear: both; }.health small { display: block; margin-top: 7px; color: #89989c; font-size: 8px; }
.ordering-panel { min-width: 0; padding: 22px 24px; }.ordering-head { display: flex; justify-content: space-between; gap: 15px; margin-bottom: 19px; }.ordering-head small { color: #118c91; font-size: 8px; }.ordering-head h2 { margin: 8px 0 0; font-size: 18px; }.ordering-head p { margin: 4px 0 0; color: #819196; font-size: 9px; }.view-switch { height: 35px; padding: 3px; display: flex; border: 1px solid #dce5e7; border-radius: 5px; background: #f4f7f8; }.view-switch button { width: 29px; border: 0; border-radius: 3px; background: transparent; color: #839398; cursor: pointer; }.view-switch button.active { background: #fff; color: #108b90; box-shadow: 0 2px 5px rgba(40,68,74,.1); }.sort-columns,.sort-row { display: grid; grid-template-columns: minmax(220px,1.2fr) minmax(130px,.8fr) 78px 94px; align-items: center; gap: 10px; }.sort-columns { min-height: 29px; padding: 0 11px; color: #87969a; font-size: 7px; font-weight: 800; }.sort-list { display: grid; gap: 6px; }.sort-row { min-height: 62px; padding: 7px 10px; border: 1px solid #dfe7e9; border-radius: 5px; transition: .2s; }.sort-row:hover { border-color: #98d2d4; box-shadow: 0 4px 13px rgba(37,69,75,.06); }.sort-row.changed { border-left: 3px solid #e0a137; }.sort-name { min-width: 0; gap: 8px; }.sort-name .drag { color: #a1afb2; cursor: grab; }.sort-name > b { width: 19px; color: #829297; font: 700 9px Consolas,monospace; }.sort-name > span { width: 32px; height: 32px; display: grid; place-items: center; flex: 0 0 32px; border-radius: 5px; background: #edf5f5; color: #148c91; }.sort-name > div { min-width: 0; }.sort-name strong,.sort-name small { display: block; }.sort-name strong { font-size: 10px; }.sort-name small { margin-top: 3px; color: #91a0a3; font-size: 7px; }.sort-row code { overflow: hidden; color: #5d7177; text-overflow: ellipsis; white-space: nowrap; font: 9px Consolas,monospace; }.sort-row > em { display: flex; align-items: center; gap: 5px; font-size: 8px; font-style: normal; }.sort-row > em i { width: 6px; height: 6px; border-radius: 50%; }.published { color: #2e8467; }.published i { background: #2bae7e; }.pending { color: #ad7518; }.pending i { background: #e9a52f; }.row-buttons { justify-content: flex-end; }.drop-zone { min-height: 44px; margin-top: 8px; display: flex; align-items: center; justify-content: center; gap: 6px; border: 1px dashed #c6d7da; border-radius: 5px; color: #758b90; font-size: 9px; }
.changes-panel { padding: 0 16px 18px; border-left: 1px solid #e1e8ea; }.changes-panel .panel-title { padding-left: 0; padding-right: 0; }.count { width: 24px; height: 24px; display: grid; place-items: center; border-radius: 50%; background: #ffefd3; color: #ae7518; font-size: 9px; }.change-summary { padding: 11px; display: flex; gap: 9px; border: 1px solid #dfe7e9; border-radius: 5px; background: #fff; }.change-summary > span { width: 30px; height: 30px; display: grid; place-items: center; border-radius: 5px; background: #e4f4f3; color: #138d92; }.change-summary strong,.change-summary small { display: block; }.change-summary strong { font-size: 9px; }.change-summary small { margin-top: 3px; color: #8a989c; font-size: 7px; }.timeline { padding: 16px 0 4px 8px; }.timeline > div { position: relative; padding: 0 0 17px 17px; border-left: 1px solid #d7e1e3; }.timeline > div:last-child { border-color: transparent; }.timeline i { position: absolute; left: -4px; top: 2px; width: 7px; height: 7px; border-radius: 50%; background: #afbdc0; box-shadow: 0 0 0 4px #f8fbfb; }.timeline .current i { background: #19a5a6; }.timeline small,.timeline strong { display: block; }.timeline small { color: #95a2a5; font-size: 7px; }.timeline strong { margin-top: 3px; font-size: 9px; }.timeline p { margin: 3px 0 0; color: #798c91; font-size: 8px; }.impact { margin: 5px 0 13px; padding: 12px; border: 1px solid #ecd8b1; border-radius: 5px; background: #fff9ed; }.impact > strong { display: flex; align-items: center; gap: 6px; margin-bottom: 7px; color: #a66d11; font-size: 9px; }.impact > div { padding: 4px 0; display: flex; justify-content: space-between; color: #687b81; font-size: 8px; }.impact p { margin: 6px 0 0; color: #8c7d63; font-size: 7px; line-height: 1.5; }.changes-panel > .el-button { width: 100%; }

.kpis { margin-bottom: 12px; display: grid; grid-template-columns: repeat(4,1fr); gap: 11px; }.kpis > div { min-height: 76px; padding: 11px 14px; display: flex; align-items: center; gap: 10px; border: 1px solid #dfe7e9; border-radius: 6px; background: #fff; }.kpis > div > span { width: 36px; height: 36px; display: grid; place-items: center; flex: 0 0 36px; border-radius: 5px; font-size: 17px; }.kpis .teal { color: #0b898e; background: #e1f4f3; }.kpis .blue { color: #416fc0; background: #e8effc; }.kpis .amber { color: #b27716; background: #fff0d6; }.kpis .green { color: #27865f; background: #e4f3eb; }.kpis small,.kpis strong { display: block; }.kpis small { color: #829398; font-size: 8px; }.kpis strong { margin-top: 2px; font-size: 17px; }.kpis em { margin-left: auto; align-self: flex-end; color: #4b886e; font-size: 7px; font-style: normal; }.kpis em.warn { color: #b27616; }
.governance-grid { min-height: calc(100vh - 286px); display: grid; grid-template-columns: 255px minmax(560px,1fr) 270px; }.domain-panel { padding: 0 12px 16px; border-right: 1px solid #e1e8ea; }.domain-panel .panel-title { padding-left: 2px; padding-right: 2px; }.domains > div + div { margin-top: 10px; }.domain-title { min-height: 30px; padding: 0 6px; display: flex; align-items: center; gap: 6px; color: #52676e; }.domain-title strong { flex: 1; font-size: 9px; }.domain-title em { min-width: 19px; padding: 2px 5px; border-radius: 8px; background: #e4ecee; text-align: center; font-size: 7px; font-style: normal; }.domains button { width: 100%; min-height: 47px; padding: 5px 7px 5px 23px; display: flex; align-items: center; gap: 7px; border: 1px solid transparent; border-radius: 5px; background: transparent; color: #5b6f75; text-align: left; cursor: pointer; }.domains button:hover { background: #edf4f4; }.domains button.active { border-color: #b9dfe0; background: #e2f4f3; color: #087b80; }.domains button > span { min-width: 0; flex: 1; }.domains button strong,.domains button small { display: block; }.domains button strong { font-size: 9px; }.domains button small { margin-top: 3px; color: #8d9da0; font-size: 7px; }.warning-icon { color: #d49728; }
.permission-panel { min-width: 0; padding: 0 22px 22px; }.permission-head { min-height: 82px; justify-content: space-between; gap: 15px; border-bottom: 1px solid #e3eaec; }.policy-tabs { height: 50px; display: flex; gap: 21px; border-bottom: 1px solid #e3eaec; }.policy-tabs button { position: relative; border: 0; background: transparent; color: #73868b; font-size: 9px; font-weight: 700; cursor: pointer; }.policy-tabs button.active { color: #0d8489; }.policy-tabs button.active::after { content: ''; position: absolute; left: 0; right: 0; bottom: -1px; height: 2px; background: #1aa4a8; }.policy-tabs b { margin-left: 3px; padding: 1px 5px; border-radius: 8px; background: #edf2f3; font-size: 7px; }.policy-intro { min-height: 78px; justify-content: space-between; gap: 15px; }.policy-intro strong { font-size: 10px; }.policy-intro p { margin: 3px 0 0; color: #85969a; font-size: 8px; }.role-table { border: 1px solid #dfe7e9; border-radius: 5px; overflow: hidden; }.role-head,.role-row { display: grid; grid-template-columns: minmax(160px,1.1fr) minmax(90px,.7fr) repeat(4,48px); align-items: center; gap: 7px; }.role-head { min-height: 31px; padding: 0 12px; background: #f5f8f8; color: #829398; font-size: 7px; font-weight: 800; }.role-head span:nth-child(n+3) { text-align: center; }.role-row { min-height: 59px; padding: 6px 12px; border-top: 1px solid #e7edef; }.role-name { display: flex; align-items: center; gap: 8px; }.role-name > b { width: 29px; height: 29px; display: grid; place-items: center; flex: 0 0 29px; border-radius: 5px; color: #fff; font-size: 8px; }.role-name strong,.role-name small { display: block; }.role-name strong { font-size: 9px; }.role-name small { margin-top: 3px; color: #8f9da1; font-size: 7px; }.role-row .el-checkbox { justify-self: center; }.inheritance { min-height: 56px; margin-top: 12px; padding: 10px 12px; gap: 9px; border: 1px solid #cae5da; border-radius: 5px; background: #eff9f5; color: #287c5d; }.inheritance > div { flex: 1; }.inheritance strong { font-size: 9px; }.inheritance p { margin: 3px 0 0; color: #699181; font-size: 7px; }.action-list,.audit-list { margin-top: 17px; display: grid; gap: 7px; }.action-list > div { min-height: 62px; padding: 9px 12px; display: flex; align-items: center; gap: 12px; border: 1px solid #dfe7e9; border-radius: 5px; }.action-list code { min-width: 88px; color: #147f84; font-size: 8px; }.action-list span { flex: 1; }.action-list strong,.action-list small { display: block; }.action-list strong { font-size: 9px; }.action-list small { margin-top: 3px; color: #89999d; font-size: 8px; }.audit-list > div { min-height: 58px; padding: 8px 2px; display: flex; align-items: center; gap: 9px; border-bottom: 1px solid #e4ebed; }.audit-list > div > b { width: 28px; height: 28px; display: grid; place-items: center; border-radius: 50%; background: #e0f2f2; color: #0e858a; font-size: 8px; }.audit-list > div > span { flex: 1; }.audit-list strong,.audit-list small { display: block; }.audit-list strong { font-size: 9px; }.audit-list small { margin-top: 3px; color: #8b999d; font-size: 8px; }.audit-list time { color: #95a2a5; font-size: 7px; }
.risk-panel { padding: 0 15px 17px; border-left: 1px solid #e1e8ea; }.risk-panel .panel-title { padding-left: 0; padding-right: 0; }.score { padding: 9px 0 14px; text-align: center; }.score > div { width: 96px; height: 96px; margin: 0 auto; display: grid; align-content: center; border-radius: 50%; background: radial-gradient(circle,#fff 61%,transparent 63%),conic-gradient(#1ba77b 0 92%,#dfe8e5 92% 100%); }.score strong,.score small { display: block; }.score strong { color: #268260; font-size: 23px; }.score small { color: #82948f; font-size: 7px; }.score p { max-width: 190px; margin: 10px auto 0; color: #7d9095; font-size: 8px; line-height: 1.5; }.checks { border-top: 1px solid #dfe7e9; }.checks > div { min-height: 55px; padding: 8px 1px; display: flex; align-items: center; gap: 8px; border-bottom: 1px solid #e1e8ea; }.checks > div > span { flex: 1; }.checks strong,.checks small { display: block; }.checks strong { font-size: 8px; }.checks small { margin-top: 3px; color: #89989c; font-size: 7px; }.checks .passed > .el-icon { color: #269a70; }.checks .warning > .el-icon { color: #d99b2b; }.review { margin-top: 15px; padding: 11px; border: 1px solid #dfe7e9; border-radius: 5px; background: #fff; }.review small,.review strong { display: block; }.review small { color: #89989c; font-size: 7px; }.review strong { margin: 4px 0 10px; font-size: 8px; }.review .el-button { width: 100%; }

.deep-variant { max-width: 1600px; }
.deep-dirty { display: flex; align-items: center; gap: 6px; color: #b57917; font-size: 9px; }
.deep-dirty i { width: 7px; height: 7px; border-radius: 50%; background: #e9a42f; box-shadow: 0 0 0 4px #fff0d3; }
.deep-layout { min-height: calc(100vh - 200px); display: grid; grid-template-columns: minmax(390px, 42%) minmax(590px, 1fr); }
.deep-tree-panel { min-width: 0; min-height: 0; display: flex; flex-direction: column; overflow: hidden; border-right: 1px solid #dfe7e9; }
.deep-tree-heading { min-height: 76px; padding: 15px 18px; display: flex; align-items: center; justify-content: space-between; gap: 14px; border-bottom: 1px solid #e2e9eb; }
.deep-tree-heading strong,.deep-tree-heading small { display: block; }.deep-tree-heading strong { font-size: 13px; }.deep-tree-heading small { margin-top: 4px; color: #87979b; font-size: 9px; }
.deep-tree-tools { padding: 13px 15px; display: grid; grid-template-columns: 1fr auto; gap: 8px; border-bottom: 1px solid #e5ebed; background: #fff; }
.deep-tree-scroll { flex: 1; min-height: 0; padding: 10px 9px 18px; overflow: auto; }
.deep-tree { background: transparent; color: #455d64; --el-tree-node-hover-bg-color: #edf5f5; }
.deep-tree :deep(.el-tree-node__content) { min-height: 48px; height: auto; margin: 2px 0; padding-right: 5px; border: 1px solid transparent; border-radius: 5px; }
.deep-tree :deep(.el-tree-node__content:hover) { border-color: #d7e6e8; }
.deep-tree :deep(.el-tree-node.is-current > .el-tree-node__content) { border-color: #a9dcde; background: #dff3f3; color: #087d82; }
.deep-tree :deep(.el-tree-node__expand-icon) { color: #72868b; font-size: 13px; }
.deep-tree :deep(.el-tree-node__children) { position: relative; }
.deep-tree :deep(.el-tree-node__children::before) { content: ''; position: absolute; top: 0; bottom: 6px; left: 8px; border-left: 1px dashed #cad8da; }
.deep-node-row { width: calc(100% - 3px); min-width: 0; display: flex; align-items: center; gap: 8px; }
.deep-node-kind { width: 30px; height: 30px; display: grid; place-items: center; flex: 0 0 30px; border-radius: 5px; font-size: 15px; }
.deep-node-kind.folder { color: #b87915; background: #fff0d5; }.deep-node-kind.page { color: #168b91; background: #e3f4f4; }
.deep-node-copy { min-width: 0; flex: 1; }.deep-node-copy strong,.deep-node-copy small { display: block; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }.deep-node-copy strong { font-size: 10px; }.deep-node-copy small { margin-top: 3px; color: #8b9a9e; font: 7px Consolas, monospace; }
.deep-child-count { min-width: 20px; padding: 2px 5px; border-radius: 9px; background: #e4ecee; color: #6f8287; text-align: center; font-size: 7px; }
.deep-node-actions { display: flex; opacity: 0; transition: opacity .15s; }.deep-node-row:hover .deep-node-actions,.deep-tree :deep(.is-current) > .el-tree-node__content .deep-node-actions { opacity: 1; }
.deep-node-actions .el-button { width: 25px; height: 25px; margin: 0; }
.deep-tree-footer { min-height: 48px; padding: 10px 15px; display: flex; align-items: center; justify-content: space-between; border-top: 1px solid #dfe7e9; background: #fff; color: #7c8e93; font-size: 8px; }.deep-tree-footer span { display: flex; align-items: center; gap: 6px; }.deep-tree-footer strong { color: #486169; }

.deep-detail-panel { min-width: 0; min-height: 0; display: flex; flex-direction: column; overflow: hidden; background: #fff; }
.deep-detail-head { padding: 14px 24px 17px; border-bottom: 1px solid #dfe7e9; }
.deep-breadcrumb { min-height: 25px; display: flex; align-items: center; flex-wrap: wrap; gap: 4px; color: #73868b; font-size: 8px; }.deep-breadcrumb span { display: flex; align-items: center; gap: 4px; }.deep-breadcrumb span:last-child { color: #0d858a; font-weight: 700; }
.deep-title-row { min-height: 51px; display: flex; align-items: center; justify-content: space-between; gap: 18px; }
.deep-title-identity { min-width: 0; display: flex; align-items: center; gap: 11px; }.deep-title-identity > span { width: 42px; height: 42px; display: grid; place-items: center; flex: 0 0 42px; border-radius: 6px; font-size: 20px; }.deep-title-identity > span.folder { color: #b47716; background: #fff0d5; }.deep-title-identity > span.page { color: #118b90; background: #e1f4f4; }.deep-title-identity h2 { margin: 0; font-size: 18px; }.deep-title-identity p { margin: 4px 0 0; color: #8a999d; font: 8px Consolas, monospace; }
.deep-title-actions { display: flex; align-items: center; gap: 8px; }
.deep-detail-content { flex: 1; min-height: 0; overflow: auto; }
.deep-form-section { padding: 21px 24px 23px; border-bottom: 1px solid #e5ebed; }
.deep-section-title { display: flex; align-items: center; gap: 9px; margin-bottom: 17px; }.deep-section-title > span { width: 32px; height: 32px; display: grid; place-items: center; flex: 0 0 32px; border-radius: 5px; color: #0d898f; background: #e1f4f4; }.deep-section-title.amber > span { color: #b57612; background: #fff0d6; }.deep-section-title strong,.deep-section-title small { display: block; }.deep-section-title strong { font-size: 11px; }.deep-section-title small { margin-top: 3px; color: #89999d; font-size: 8px; }
.deep-form-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 15px 18px; }.deep-form-grid label { min-width: 0; }.deep-form-grid label > span { display: block; margin-bottom: 6px; color: #52666c; font-size: 9px; font-weight: 700; }.deep-form-grid label > span b { color: #e15e5e; }.deep-form-grid label > small { display: block; margin-top: 5px; color: #8d9ca0; font-size: 7px; }.deep-form-grid .span-two { grid-column: 1 / -1; }.deep-form-grid .el-select,.deep-form-grid .el-segmented,.deep-form-grid .el-input-number { width: 100%; }.field-prefix { color: #72878c; font: 800 7px Consolas, monospace; }
.deep-behavior-grid { display: grid; grid-template-columns: 1fr 1fr; border: 1px solid #dfe7e9; border-radius: 5px; overflow: hidden; }.deep-behavior-grid > div { min-height: 62px; padding: 10px 13px; display: flex; align-items: center; justify-content: space-between; gap: 13px; border-bottom: 1px solid #e7edef; }.deep-behavior-grid > div:nth-child(odd) { border-right: 1px solid #e7edef; }.deep-behavior-grid > div:nth-last-child(-n+2) { border-bottom: 0; }.deep-behavior-grid span { min-width: 0; }.deep-behavior-grid strong,.deep-behavior-grid small { display: block; }.deep-behavior-grid strong { font-size: 9px; }.deep-behavior-grid small { margin-top: 3px; color: #89999d; font-size: 7px; }
.child-section-head { display: flex; align-items: flex-start; justify-content: space-between; gap: 15px; }.child-section-head .deep-section-title { margin-bottom: 14px; }
.deep-child-list { border: 1px solid #dfe7e9; border-radius: 5px; overflow: hidden; }.deep-child-list > div { min-height: 56px; padding: 7px 11px; display: grid; grid-template-columns: 18px 32px minmax(150px,1fr) 55px 65px 52px; align-items: center; gap: 8px; border-bottom: 1px solid #e7edef; }.deep-child-list > div:last-child { border-bottom: 0; }.child-drag { color: #9caaad; cursor: grab; }.deep-child-list .deep-node-kind { width: 30px; height: 30px; }.deep-child-list > div > span:nth-child(3) { min-width: 0; }.deep-child-list strong,.deep-child-list small { display: block; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }.deep-child-list strong { font-size: 9px; }.deep-child-list small { margin-top: 3px; color: #8c9a9e; font: 7px Consolas, monospace; }.deep-child-list em { color: #788b90; font-size: 7px; font-style: normal; }
.deep-empty { min-height: 170px; display: grid; place-items: center; align-content: center; gap: 6px; border: 1px dashed #c7d7da; border-radius: 5px; color: #87979b; text-align: center; }.deep-empty > .el-icon { font-size: 25px; }.deep-empty strong { color: #51686f; font-size: 10px; }.deep-empty p { margin: 0 0 6px; font-size: 8px; }
.deep-detail-footer { min-height: 66px; padding: 11px 24px; display: flex; align-items: center; justify-content: space-between; gap: 18px; border-top: 1px solid #dfe7e9; background: #fafcfc; }.deep-detail-footer > span { display: flex; align-items: center; gap: 6px; color: #7d8f94; font-size: 8px; }.deep-detail-footer > div { display: flex; gap: 8px; }

/* D polish pass: denser workspace, stronger hierarchy, and explicit tree controls. */
.deep-layout { grid-template-columns: minmax(360px,38%) minmax(620px,1fr); }
.deep-tree-heading strong { font-size: 14px; }.deep-tree-heading small { color: #71858a; font-size: 10px; }
.deep-tree-scope { min-height: 43px; padding: 6px 15px; display: flex; align-items: center; gap: 5px; border-bottom: 1px solid #e2e9eb; background: #fff; }
.deep-tree-scope button { height: 29px; padding: 0 9px; display: flex; align-items: center; gap: 5px; border: 1px solid transparent; border-radius: 4px; background: transparent; color: #657a80; font-size: 10px; cursor: pointer; }
.deep-tree-scope button:hover { background: #f0f5f6; }.deep-tree-scope button.active { border-color: #b8dfe0; background: #e2f4f3; color: #087b80; }.deep-tree-scope button b { color: #7b8d92; font-size: 8px; }
.scope-dot { width: 7px; height: 7px; border-radius: 2px; }.scope-dot.all { background: #789095; }.scope-dot.folder { background: #e1a23c; }.scope-dot.page { background: #1baeb5; }
.deep-node-copy strong { font-size: 12px; }.deep-node-copy small { color: #768b90; font-size: 9px; }.deep-node-status { width: 7px; height: 7px; flex: 0 0 7px; border-radius: 50%; background: #25ad7d; }.deep-node-status.off { background: #b9c4c6; }
.deep-breadcrumb { color: #657a80; font-size: 10px; }.deep-context-strip { min-height: 34px; margin-top: 12px; padding: 7px 10px; display: flex; align-items: center; gap: 18px; border: 1px solid #e0e8ea; border-radius: 4px; background: #f7fafa; color: #667c82; font-size: 10px; }.deep-context-strip span { display: flex; align-items: center; gap: 5px; }.deep-context-strip .el-icon { color: #168d92; }
.deep-section-title strong { font-size: 13px; }.deep-section-title small { color: #73878c; font-size: 10px; }.deep-form-grid label > span { font-size: 11px; }.deep-form-grid label > small { color: #758a8f; font-size: 9px; }.field-prefix { font-size: 8px; }
.deep-behavior-grid strong { font-size: 11px; }.deep-behavior-grid small { color: #74898e; font-size: 9px; }.deep-impact-note { margin-top: 10px; padding: 10px 12px; display: flex; align-items: center; gap: 8px; border: 1px solid #ead8b5; border-radius: 4px; background: #fff9ee; color: #a26b12; }.deep-impact-note > span { min-width: 0; }.deep-impact-note strong,.deep-impact-note small { display: block; }.deep-impact-note strong { font-size: 10px; }.deep-impact-note small { margin-top: 3px; color: #8b795d; font-size: 9px; }
.deep-child-list strong { font-size: 11px; }.deep-child-list small { color: #758a8f; font-size: 9px; }.deep-child-list em { color: #657c82; font-size: 9px; }
@media (min-width: 881px) { .deep-layout { height: calc(100vh - 200px); min-height: 660px; max-height: 860px; overflow: hidden; }.deep-tree-scroll,.deep-detail-content { overscroll-behavior: contain; scrollbar-gutter: stable; } }
@media (hover: none) { .deep-node-actions { opacity: 1; } }

@media (max-width: 1240px) { .workbench { grid-template-columns: 280px minmax(520px,1fr); }.preview-panel { display: none; }.reorder-grid { grid-template-columns: 225px minmax(520px,1fr); }.changes-panel { display: none; }.governance-grid { grid-template-columns: 230px minmax(550px,1fr); }.risk-panel { display: none; }.deep-layout { grid-template-columns: minmax(340px,38%) minmax(560px,1fr); }.concept { display: none; } }
@media (max-width: 880px) { .topbar { position: static; grid-template-columns: 1fr auto; padding: 10px 14px; }.variant-nav { grid-column: 1/-1; grid-row: 2; }.variant-nav button { flex: 1; justify-content: center; padding: 0 7px; }.stage { padding: 13px; }.page-heading { align-items: flex-start; }.workbench,.reorder-grid,.governance-grid,.deep-layout { min-height: auto; grid-template-columns: 1fr; }.tree-panel,.module-panel,.domain-panel,.deep-tree-panel { max-height: 440px; border-right: 0; border-bottom: 1px solid #e1e8ea; }.form-grid,.deep-form-grid { grid-template-columns: 1fr; }.deep-form-grid .span-two { grid-column: auto; }.kpis { grid-template-columns: repeat(2,1fr); }.dark-heading { margin-left: -13px; margin-right: -13px; }.sort-columns,.sort-row { grid-template-columns: minmax(210px,1fr) 75px 90px; }.sort-columns span:nth-child(2),.sort-row code { display: none; } }
@media (max-width: 560px) { .brand small,.concept,.variant-nav b { display: none; }.brand strong { font-size: 12px; }.top-actions .el-button span { display: none; }.variant-nav button { font-size: 10px; }.page-heading { display: block; }.page-heading > div:last-child { margin-top: 13px; justify-content: flex-start; }.page-heading h1 { font-size: 20px; }.editor-head,.form-block,.ordering-panel,.permission-panel,.deep-detail-head,.deep-form-section,.deep-detail-footer { padding-left: 14px; padding-right: 14px; }.editor-footer,.deep-detail-footer { align-items: flex-start; flex-direction: column; }.deep-title-row { align-items: flex-start; flex-direction: column; }.deep-title-actions { width: 100%; flex-wrap: wrap; }.deep-behavior-grid { grid-template-columns: 1fr; }.deep-behavior-grid > div,.deep-behavior-grid > div:nth-child(odd) { border-right: 0; border-bottom: 1px solid #e7edef; }.deep-behavior-grid > div:last-child { border-bottom: 0; }.deep-child-list { overflow-x: auto; }.deep-child-list > div { min-width: 600px; }.kpis { grid-template-columns: 1fr; }.role-table { overflow-x: auto; }.role-head,.role-row { min-width: 590px; }.sort-columns { display: none; }.sort-row { grid-template-columns: minmax(175px,1fr) 74px; }.sort-row > em { display: none; } }
@media (max-width: 560px) { .deep-title-actions,.deep-context-strip { width: 100%; flex-wrap: wrap; }.deep-context-strip { gap: 8px 14px; }.deep-tree-scope { overflow-x: auto; }.deep-tree-scope button { flex: 0 0 auto; } }
</style>
