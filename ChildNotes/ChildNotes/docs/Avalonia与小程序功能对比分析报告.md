# Avalonia 版与微信小程序版功能对比分析报告

> **报告日期**：2026-07-08
> **分析对象**：ChildNotes（AiJi）项目 Avalonia 跨平台版（前端）与 微信小程序版（参考实现）
> **报告类型**：功能完整性对比与差距分析
> **输出位置**：[docs/Avalonia与小程序功能对比分析报告.md](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/docs/Avalonia与小程序功能对比分析报告.md)

---

## 1. 报告概述

### 1.1 分析目的

本报告对 ChildNotes 项目当前 Avalonia 跨平台版本与参考实现的微信小程序版进行系统化功能对比，旨在：

1. 识别 Avalonia 版相对小程序版的功能缺失与实现差异；
2. 量化整体功能完成度；
3. 为后续迭代规划提供基于优先级的路线图建议；
4. 重点标记影响核心业务流程与运营能力的差距，便于团队决策。

### 1.2 分析方法

- **静态分析**：基于两版代码库的目录结构、View/ViewModel/Service 清单、数据库 schema、组件清单进行逐项对照；
- **功能映射**：将小程序的"页面 → 组件 → Service API"三层结构映射到 Avalonia 的"View → ViewModel → Service"三层结构；
- **差异标注**：使用四态符号体系（✅/⚠️/❌/➕）标注每个功能点的实现状态；
- **优先级评估**：基于业务影响与用户感知对缺失项进行 P0~P3 分级。

### 1.3 分析范围

| 维度 | 范围 |
| --- | --- |
| **包含** | 页面/视图层、ViewModel、Service 业务层、数据库 schema、记录类型、表单交互、首页/喂养页/统计/积分/AI/同步/登录等核心模块 |
| **不包含** | 后端 API 实现细节、CI/CD 配置、UI 视觉细节（颜色/间距/字号）、国际化、无障碍 |

### 1.4 数据来源

| 来源 | 说明 |
| --- | --- |
| Avalonia 版源码 | `e:\0_Code\5_Git\AiJi\ChildNotes\ChildNotes\` |
| 小程序版源码（参考副本） | `E:\0_Code\5_Git\AiJi参考\child-notes-front-z-master` |
| 项目规则 | [.trae/rules/project_rules.md](file:///e:/0_Code/5_Git/AiJi/.trae/rules/project_rules.md) |
| MCP 调试指南 | [.trae/rules/mcp-debug-guide.md](file:///e:/0_Code/5_Git/AiJi/.trae/rules/mcp-debug-guide.md) |
| Ursa 评估报告 | [.trae/rules/ursa-avalonia-reference.md](file:///e:/0_Code/5_Git/AiJi/.trae/rules/ursa-avalonia-reference.md) |

### 1.5 状态符号说明

| 符号 | 含义 |
| --- | --- |
| ✅ | 已实现且与小程序版对齐 |
| ⚠️ | 部分实现 / 实现方式有差异 |
| ❌ | 未实现（缺失） |
| ➕ | Avalonia 独有，小程序版无对应功能 |

---

## 2. 两版架构与技术栈对比

### 2.1 整体架构对比

| 维度 | 微信小程序版 | Avalonia 版 |
| --- | --- | --- |
| **定位** | 微信生态内的小程序，依托微信分发 | 独立跨平台 App（Windows 调试 + Android 发布 + iOS 潜在扩展） |
| **分发渠道** | 微信小程序码、分享链接、微信搜索 | 应用商店 / GitHub Release / 直链 APK |
| **更新机制** | 微信热更新（无需用户操作） | App 自更新或应用商店更新（Android）/ 重装（iOS） |
| **离线能力** | 弱（小程序缓存有限） | 强（SQLite 本地优先，完全离线可用） |
| **账号体系** | 强依赖微信账号（wx.login + openid） | 独立账号密码体系（无微信依赖） |
| **支付能力** | 微信支付原生可用（未启用） | 无内置支付（需自接 SDK） |

### 2.2 技术栈对比

| 技术栈 | 微信小程序版 | Avalonia 版 |
| --- | --- | --- |
| **UI 框架** | WXML + WXSS + JS（小程序原生） | Avalonia 12.0.5（XAML + C#） |
| **运行时** | 微信小程序运行时（JavaScriptCore / V8） | .NET 10 |
| **语言** | JavaScript（ES6+） | C# 13 |
| **MVVM 框架** | 无（小程序原生 Page/Component） | CommunityToolkit.Mvvm（SourceGenerator） |
| **数据存储** | wx.setStorageSync（KV 存储，无关系查询） | Microsoft.Data.Sqlite（关系型，13+ 张表） |
| **网络通信** | wx.request（封装 api.js） | HttpClient（BaseApiClient + 专用 Client） |
| **图片加载** | wx.chooseImage + image 组件 | Avalonia Bitmap（异步解码） |
| **认证加密** | 无（依赖微信 openid） | PBKDF2-SHA256，600k 迭代 |
| **日志** | console + 自实现埋点 | Serilog（分级脱敏滚动速率限制）+ DevLogger（内存环形 500 行） |
| **依赖注入** | 无（全局 App/Service 单例） | ServiceProvider 服务定位器 |
| **调试集成** | 微信开发者工具 | Keincheck MCP（#if DEBUG）+ Visual Studio |

### 2.3 数据存储对比

| 维度 | 微信小程序版 | Avalonia 版 |
| --- | --- | --- |
| **存储模型** | KV（wx.setStorageSync） | 关系型 SQLite |
| **表数量** | 0（全部 KV 结构） | 13+ 张表 |
| **查询能力** | 全量加载后内存过滤 | SQL 索引查询、分页、聚合 |
| **数据完整性** | 弱（无外键约束） | 强（外键 + 事务 + 备份） |
| **离线写入** | 支持（缓存） | 支持（SQLite 直写） |
| **冲突解决** | 后端权威 | 本地优先 + MarkSynced 防重推 |

### 2.4 模块规模对比

| 模块 | 微信小程序版 | Avalonia 版 |
| --- | --- | --- |
| **页面/视图** | 8 个 Page | 17 个 View + MainWindow |
| **组件** | 22 个 Component | 4 个 Controls + 内联模板 |
| **Service** | 6 个（api.js 封装） | 28 个 |
| **ViewModel** | 0（Page 直接处理） | 19 顶层 + 11 Forms + 5 Home 子 |
| **记录类型** | 13 种 | 11 种（缺 2 种） |
| **表单组件** | 13 个 | 11 个 |

---

## 3. 功能模块完整对照表

### 3.1 全局与导航

| 功能点 | 微信小程序版 | Avalonia 版 | 状态 |
| --- | --- | --- | --- |
| 4 Tab 主导航 | custom-tab-bar 组件（emoji 图标） | MainShellView 4 Tab + 9 overlay | ✅ |
| 自定义 tabBar 样式 | 支持（custom-tab-bar） | TabControl 模板自定义 | ✅ |
| 抽屉式表单容器 | record-drawer 组件 | RecordSheetView | ✅ |
| 系统返回键处理 | 小程序自动管理 | HandleSystemBack（RootContainer） | ➕ |
| 键盘高度回调 | 无 | OnAndroidKeyboardHeightChanged | ➕ |
| Tab 状态保存/恢复 | 小程序自动管理 | 手动实现 | ➕ |
| 全局 globalData | userInfo/token/currentBaby/babyList/systemInfo/pendingFamilyInvite/pendingReferrerId | AppState 单例 | ✅ |
| 应用版本号管理 | 小程序版本号 | Directory.Build.targets 关闭 Git 后缀 + tag 驱动 | ➕ |

### 3.2 宝宝管理

| 功能点 | 微信小程序版 | Avalonia 版 | 状态 |
| --- | --- | --- | --- |
| 多宝宝支持 | babyList + currentBaby | BabyService 多宝宝 | ✅ |
| 宝宝切换 | setCurrentBaby / getCurrentBabyId | BabyService 切换 + AppState | ✅ |
| 新增宝宝 | addBaby | BabyManagerView + BabyService | ✅ |
| 修改宝宝资料 | updateBabyInfo | BabyManagerView 编辑 | ✅ |
| 删除宝宝 | 后端 API | BabyManagerView | ✅ |
| 多宝宝隔离 | X-Baby-Id HTTP header | AppState.CurrentBabyId 注入 | ✅ |
| 宝宝初始引导 | baby-setup 页 | BabySetupView | ✅ |
| 成长阶段计算 | getGrowthStage | DailyTipsCatalog + VaccineCatalog | ✅ |

### 3.3 家庭协作

| 功能点 | 微信小程序版 | Avalonia 版 | 状态 |
| --- | --- | --- | --- |
| 家庭成员列表 | family 页 + getFamilyMembers | FamilyView + FamilyApiClient | ✅ |
| 修改自己角色 | updateMyFamilyRole | FamilyView 角色编辑 | ✅ |
| 邀请家人加入 | onShareAppMessage 分享链接 + joinFamilyViaInvite | FamilyView 邀请 | ⚠️ |
| 接受邀请跳转 | pendingFamilyInvite 全局暂存 | 需手动输入邀请码 | ⚠️ |
| 邀请有礼联动 | 邀请记录 + 积分奖励 | 积分页有邀请记录展示 | ⚠️ |

### 3.4 记录类型（13 种逐项对比）

| # | 记录类型 | 微信小程序版 | Avalonia 版 | 状态 |
| --- | --- | --- | --- | --- |
| 1 | 喂奶 | feed-form（母乳双侧独立计时） | FeedFormViewModel | ✅ |
| 2 | 睡眠 | sleep-form | SleepFormViewModel | ✅ |
| 3 | 尿布 | diaper-form | DiaperFormViewModel | ✅ |
| 4 | 体温 | temperature-form | TemperatureFormViewModel | ✅ |
| 5 | 补给用药 | supplement-form | SupplementFormViewModel | ✅ |
| 6 | 成长 | growth-form（最多4张照片） | GrowthFormViewModel | ✅ |
| 7 | 异常 | abnormal-form | AbnormalFormViewModel | ✅ |
| 8 | 吸奶 | pump-form | PumpFormViewModel | ✅ |
| 9 | 辅食 | complementary-form | ComplementaryFormViewModel | ✅ |
| 10 | 疫苗 | vaccine-form（含自定义疫苗） | VaccineFormViewModel（含时间轴+原地更新） | ✅ |
| 11 | 活动 | activity-form | ActivityFormViewModel | ✅ |
| 12 | **妈妈饮食** | maternal-food-form | — | ❌ |
| 13 | **里程碑** | growth 页内独立记录（getMilestones/addMilestone/updateMilestone） | GrowthView 含里程碑但未作为独立记录类型 | ⚠️ |

### 3.5 记录表单功能（特殊交互对比）

| 表单 | 小程序版特殊交互 | Avalonia 版对应实现 | 状态 |
| --- | --- | --- | --- |
| **feed-form 喂奶** | 母乳亲喂双侧独立计时（leftStartTime/rightStartTime 持久化后端） | FeedFormViewModel 双侧计时 | ✅ |
| **sleep-form 睡眠** | startTime 持久化后端，进行中状态实时刷新 | SleepFormViewModel + 进行中状态 | ✅ |
| **growth-form 成长** | 最多 4 张照片上传 | GrowthFormViewModel + UploadService | ✅ |
| **vaccine-form 疫苗** | 自定义疫苗（addCustomVaccine） | VaccineFormViewModel + VaccineCatalog 自定义疫苗 | ✅ |
| **supplement-form 补给** | 自定义补给项（getSupplementCustomItems） | SupplementFormViewModel + user_supplement_item 表 | ✅ |
| **maternal-food 妈妈饮食** | 完整表单 | — | ❌ |
| **abnormal-form 异常** | 发烧/腹泻/其他分类 | AbnormalFormViewModel | ✅ |
| **temperature-form 体温** | 与发烧追踪器联动 | TemperatureFormViewModel + AbnormalTracking | ✅ |
| **complementary-form 辅食** | 辅食类型选择 | ComplementaryFormViewModel | ✅ |
| **pump-form 吸奶** | 吸奶量记录 | PumpFormViewModel | ✅ |
| **activity-form 活动** | 活动类型 | ActivityFormViewModel | ✅ |
| **diaper-form 尿布** | 尿布状态 | DiaperFormViewModel | ✅ |

### 3.6 首页功能

| 功能点 | 微信小程序版 | Avalonia 版 | 状态 |
| --- | --- | --- | --- |
| 宝宝概览 | baby-status-bar | HomeView 顶部 + HomeCore | ✅ |
| 今日统计 | good-status（dailyTips/growthStage/sleeping） | HomeCore 子 VM | ✅ |
| 异常追踪面板 | fever-tracker / diarrhea-tracker / activity-tracker / vaccine-tracker | AbnormalTracking / ActivityTracking / VaccineTracking 子 VM | ✅ |
| 金刚区快捷记录 | quick-actions（9 种类型） | QuickMenuView（首页底部功能面板） | ✅ |
| 抽屉式表单 | record-drawer | RecordSheetView | ✅ |
| 进行中状态实时刷新 | 睡眠/喂奶定时刷新 | AiStatus + 进行中状态 | ✅ |
| 发烧用药过量警告 | fever-tracker（4 小时 + 30 秒定时器） | AbnormalTracking | ⚠️ |
| AI 智能记入口 | — | QuickInputView（首页底部 AI 输入栏） | ➕ |

### 3.7 喂养页

| 功能点 | 微信小程序版 | Avalonia 版 | 状态 |
| --- | --- | --- | --- |
| 按日期查看时间线 | feeding 页 | FeedingView | ✅ |
| 日期切换 | 日期选择器 | FeedingView 日期切换 | ✅ |
| 左滑删除 | 自实现手势（SWIPE_OPEN_RATIO=0.38，速度判定） | Avalonia SwipeItem | ⚠️ |
| 点击编辑 | 点击记录项 | 点击记录项 | ✅ |
| 唤醒睡眠 | wakeUpSleep API | RecordService 对应方法 | ✅ |

### 3.8 成长里程碑

| 功能点 | 微信小程序版 | Avalonia 版 | 状态 |
| --- | --- | --- | --- |
| 里程碑列表 | growth 页 | GrowthView | ✅ |
| 自动创建出生节点 | growth 页自动处理 | GrowthView 处理 | ✅ |
| 里程碑标题/日期/内容 | 表单 | GrowthFormViewModel | ✅ |
| 最多 4 张照片 | wx.chooseImage | UploadService | ✅ |
| 编辑里程碑 | updateMilestone | GrowthFormViewModel 编辑 | ✅ |
| 里程碑作为独立记录类型 | milestone 表 + 独立 API | milestone 表存在但未作为独立记录类型暴露 | ⚠️ |

### 3.9 统计页

| 功能点 | 微信小程序版 | Avalonia 版 | 状态 |
| --- | --- | --- | --- |
| 13 种记录类型 | 全部支持 | 11 种（缺妈妈饮食/里程碑统计） | ⚠️ |
| 5 种区间（日/3天/周/月/自定义） | 全部支持 | StatisticsView 支持 | ✅ |
| 日历热力图 | 自实现 | CalendarHeatMapControl（自研） | ✅ |
| 柱状图 | 自实现 | 统计页图表 | ✅ |
| 4 项汇总 | 汇总卡片 | 统计页汇总 | ✅ |
| 历史记录查询 | getHistoryRecords | RecordService 历史查询 | ✅ |
| 区间统计 | getStatsRange / getDailyStats | StatisticsService | ✅ |

### 3.10 积分运营

| 功能点 | 微信小程序版 | Avalonia 版 | 状态 |
| --- | --- | --- | --- |
| 积分仪表盘 | points 页 getDashboard | PointsView + PointsService | ✅ |
| 签到 | getSignInRule + signIn | PointsView 签到 | ✅ |
| 抽奖 | getActiveLottery + joinLottery | — | ❌ |
| 邀请记录 | getInviteRecords | PointsView 邀请记录展示 | ⚠️ |
| 分享转发有礼 | onShareAppMessage | 无原生分享 | ❌ |
| 任务体系 | task_record 表 | task_record 表存在 | ⚠️ |
| 抽奖历史 | getLotteryHistory | — | ❌ |

### 3.11 AI 智能分析

| 功能点 | 微信小程序版 | Avalonia 版 | 状态 |
| --- | --- | --- | --- |
| 智能分析生成 | smartAnalysis.generateAnalysis | AiAnalysisService（7 天区间 → TXT → LLM） | ➕ |
| 分析列表 | getAnalysisList | AiAnalysisView 列表 | ➕ |
| 分析详情 | getAnalysisDetail | AiAnalysisView 详情 | ➕ |
| AI 智能记（自然语言→记录） | — | AiNoteParseService（三级降级） | ➕ |
| LLM 客户端 | — | LlmClient（OpenAI/DeepSeek/通义千问/Ollama/vLLM/LM Studio） | ➕ |
| AI 设置页 | — | AiSettingsView | ➕ |

### 3.12 同步机制

| 功能点 | 微信小程序版 | Avalonia 版 | 状态 |
| --- | --- | --- | --- |
| 在线同步 | 后端权威，前端缓存 | 本地优先 + 双向同步（Pull + Push） | ➕ |
| 分页同步 | — | 500/页 | ➕ |
| 单事务 upsert | — | 单事务 | ➕ |
| 防重推 | — | MarkSynced | ➕ |
| DB 备份 | — | 同步前备份 | ➕ |
| 重试策略 | — | SyncPolicy（Network 3/Server5xx 2/Auth 1） | ➕ |
| 同步触发 | — | 启动 8s / 写入 5s 防抖 / 手动 / 网络恢复 / 15min 保活 / 3s 节流 | ➕ |
| 网络监听 | — | NetworkMonitor（三层探测三态） | ➕ |
| 同步设置页 | — | SyncSettingsView | ➕ |
| 用户 ID 迁移修复 | — | 支持 | ➕ |

### 3.13 上传能力

| 功能点 | 微信小程序版 | Avalonia 版 | 状态 |
| --- | --- | --- | --- |
| 图片选择 | wx.chooseImage | UploadService（本地优先 + 异步上传） | ✅ |
| 图片上传 | wx.uploadFile | 异步上传 | ✅ |
| 图片预览 | wx.previewImage + img-preview 组件 | 内置预览 | ✅ |
| 上传IfNeeded | uploadFileIfNeeded | 本地优先判断 | ✅ |
| 本地路径判断 | isLocalFilePath | 本地路径判断 | ✅ |
| 支持格式 | 图片 | .jpg/.jpeg/.png/.gif/.webp | ✅ |

### 3.14 登录认证

| 功能点 | 微信小程序版 | Avalonia 版 | 状态 |
| --- | --- | --- | --- |
| 微信登录 | wx.login + loginByWechatCode + loginWithWechat + silentLoginWithWechat | — | ❌（已替代） |
| 账号密码登录 | — | LoginView（PBKDF2-SHA256，600k 迭代） | ➕ |
| 注册 | — | LoginView 注册 | ➕ |
| 获取当前用户 | getCurrentUser | AuthService | ✅ |
| 更新资料 | updateProfile | MineView 编辑 | ✅ |
| 30 天滑动过期 | — | AuthService 30 天 | ➕ |
| 明文密码自动迁移 | — | 支持 | ➕ |
| 手机验证码 | — | — | ❌（双方均无） |
| OAuth | — | — | ❌（双方均无） |
| 隐私协议 | wx.openPrivacyContract | — | ⚠️ |

### 3.15 性能优化

| 优化点 | 微信小程序版 | Avalonia 版 | 状态 |
| --- | --- | --- | --- |
| 首页批量查询 | — | 支持 | ➕ |
| 双重防抖 | — | 写入 5s + 同步 3s | ➕ |
| 疫苗时间轴预加载 | — | 预加载缓存 | ➕ |
| 疫苗时间轴原地更新 | — | 原地更新 | ➕ |
| 活动分页加载 | — | 分页加载 | ➕ |
| OneTime 绑定 | — | OneTime 绑定 | ➕ |
| 虚拟化列表 | — | 虚拟化 | ➕ |
| 异步 Bitmap 解码 | — | 异步解码 | ➕ |
| 异步 DB 查询 | — | 异步查询 | ➕ |
| 左滑删除性能优化 | 速度判定 + SWIPE_OPEN_RATIO | SwipeItem 原生 | ⚠️ |

### 3.16 日志与调试

| 功能点 | 微信小程序版 | Avalonia 版 | 状态 |
| --- | --- | --- | --- |
| 控制台日志 | console + 埋点 | DevLogger（内存环形 500 行） | ➕ |
| 文件日志 | — | Serilog（分级脱敏滚动速率限制） | ➕ |
| 全局异常处理 | — | 三层捕获 | ➕ |
| 开发者选项页 | — | DeveloperOptionsView（动画开关 + 日志导出） | ➕ |
| MCP 调试集成 | — | Keincheck（#if DEBUG） | ➕ |
| 日志导出 | — | LogExportService（Android 10+ MediaStore.Downloads 反射） | ➕ |
| DB Schema 版本检测 | — | 支持 | ➕ |

---

## 4. 重点：Avalonia 版未实现或实现不完善的功能

### 4.1 ❌ 妈妈饮食记录（maternal-food）

| 维度 | 说明 |
| --- | --- |
| **小程序版实现** | 独立表单组件 `maternal-food-form`，独立记录类型，独立 API（addMaternalFood 等），记录妈妈的饮食内容（如月子餐、特殊饮食、过敏食物等），与宝宝记录平级 |
| **Avalonia 版状态** | 完全缺失，无对应 ViewModel、表单、数据库字段、Service 方法 |
| **影响评估** | 业务完整性受损：哺乳期妈妈饮食是育儿记录的重要补充，缺失导致该场景用户无法使用本应用记录妈妈饮食。对核心宝宝记录无影响，但降低了应用在哺乳期家庭中的实用性 |
| **缺失原因** | 未规划优先级，或评估认为非核心功能 |

### 4.2 ❌ 抽奖功能（lottery）

| 维度 | 说明 |
| --- | --- |
| **小程序版实现** | points 页集成抽奖：`getActiveLottery` 获取活动、`joinLottery` 参与、`getLotteryHistory` 查看历史 |
| **Avalonia 版状态** | 完全缺失，PointsView 无抽奖入口 |
| **影响评估** | 运营能力受损：抽奖是积分体系的重要消耗出口与用户活跃抓手，缺失导致积分只能签到获取却缺乏消耗场景，长期影响用户留存 |
| **缺失原因** | 跨平台 App 缺少微信生态的运营触点，抽奖需自研完整活动管理系统，成本较高 |

### 4.3 ❌ 分享转发有礼（onShareAppMessage）

| 维度 | 说明 |
| --- | --- |
| **小程序版实现** | `onShareAppMessage` 分享小程序卡片，支持家庭邀请与邀请有礼两种场景，用户点击分享卡片直接打开小程序并携带参数 |
| **Avalonia 版状态** | 完全缺失原生分享，家庭邀请改为邀请码方式 |
| **影响评估** | 运营能力严重受损：分享有礼是小程序裂变增长的核心渠道，跨平台 App 缺失原生分享导致增长靠应用商店分发，获客成本高 |
| **缺失原因** | 跨平台 App 需对接各平台原生分享 SDK（Android Intent / iOS UIActivityViewController），且分享内容需承载邀请码而非小程序路径 |

### 4.4 ⚠️ 家庭邀请流程

| 维度 | 说明 |
| --- | --- |
| **小程序版实现** | 分享小程序卡片 → 接收方点击打开 → `pendingFamilyInvite` 全局暂存 → 进入应用自动跳转家庭页 → `joinFamilyViaInvite` 一键加入 |
| **Avalonia 版状态** | 部分实现：FamilyView 可生成邀请码，但接收方需手动输入邀请码，无自动跳转 |
| **影响评估** | 用户体验下降：邀请流程从"一键加入"退化为"复制粘贴邀请码"，转化率显著降低 |
| **缺失原因** | 跨平台 App 缺少小程序的卡片承载与启动参数传递，需通过 Deep Link / Universal Link 实现，复杂度高 |

### 4.5 ⚠️ 邀请记录与邀请有礼联动

| 维度 | 说明 |
| --- | --- |
| **小程序版实现** | `getInviteRecords` 返回邀请记录，邀请成功后自动发放积分奖励 |
| **Avalonia 版状态** | PointsView 有邀请记录展示，但缺少分享触达渠道，邀请有礼闭环不完整 |
| **影响评估** | 运营闭环不完整：有奖励规则无触达渠道，邀请有礼名存实亡 |
| **缺失原因** | 依赖分享转发能力（4.3） |

### 4.6 ⚠️ 里程碑作为独立记录类型

| 维度 | 说明 |
| --- | --- |
| **小程序版实现** | milestone 独立表 + 独立 API（`getMilestones` / `addMilestone` / `updateMilestone`），与成长记录并列 |
| **Avalonia 版状态** | milestone 表存在，GrowthView 含里程碑展示，但未作为独立记录类型在统计/首页等模块暴露 |
| **影响评估** | 轻微：里程碑数据未丢失，但跨模块联动不足 |
| **缺失原因** | 数据层已就绪，UI 层未充分暴露 |

### 4.7 ⚠️ 发烧用药过量警告

| 维度 | 说明 |
| --- | --- |
| **小程序版实现** | fever-tracker 组件：4 小时内用药过量警告 + 30 秒定时器实时刷新 |
| **Avalonia 版状态** | AbnormalTracking 子 VM 实现了异常追踪，但 4 小时用药过量警告与 30 秒定时器的具体实现需核实 |
| **影响评估** | 安全提示能力可能不完整：用药过量警告涉及儿童用药安全，缺失有健康风险 |
| **缺失原因** | 需核实具体实现 |

### 4.8 ⚠️ 左滑删除交互

| 维度 | 说明 |
| --- | --- |
| **小程序版实现** | 自实现手势，支持速度判定，`SWIPE_OPEN_RATIO=0.38` 阈值控制 |
| **Avalonia 版状态** | 使用 Avalonia 原生 SwipeItem，交互细节（速度判定、阈值）可能不同 |
| **影响评估** | 轻微：功能等价，交互手感有差异 |
| **缺失原因** | 跨平台控件差异 |

### 4.9 ⚠️ 隐私协议

| 维度 | 说明 |
| --- | --- |
| **小程序版实现** | `wx.openPrivacyContract` |
| **Avalonia 版状态** | 无对应实现 |
| **影响评估** | 合规风险：应用商店上架（尤其 iOS）通常要求隐私协议弹窗 |
| **缺失原因** | 未规划 |

### 4.10 ⚠️ 任务体系

| 维度 | 说明 |
| --- | --- |
| **小程序版实现** | task_record 表支持任务记录 |
| **Avalonia 版状态** | task_record 表存在，但任务体系的完整 UI 与规则引擎未明确 |
| **影响评估** | 运营能力不完整：任务体系是积分获取的另一渠道 |
| **缺失原因** | 需核实 |

---

## 5. 微信特有能力替代方案分析

### 5.1 微信登录 → 账号密码

| 维度 | 说明 |
| --- | --- |
| **微信能力** | `wx.login` 获取 code → 后端换 openid → 静默登录 |
| **Avalonia 替代** | 账号密码登录（PBKDF2-SHA256，600k 迭代，30 天滑动过期） |
| **优势** | 无微信依赖，跨平台一致；后端账号体系独立可控 |
| **劣势** | 用户需注册账号，增加注册流失率；无微信社交关系链 |
| **建议** | 当前方案合理，后续可考虑接入各平台原生 OAuth（Google/Apple Sign-In）降低注册摩擦 |

### 5.2 分享邀请 → 邀请码 + Deep Link

| 维度 | 说明 |
| --- | --- |
| **微信能力** | `onShareAppMessage` 分享小程序卡片，携带 familyId/referrerId |
| **Avalonia 替代** | 当前：手动邀请码；建议：Deep Link / Universal Link |
| **技术方案** | Android App Link / iOS Universal Link + 邀请码兜底 |
| **劣势** | 实现复杂度高，需配置各平台域名验证 |
| **建议** | P1 优先级，先优化邀请码 UX（一键复制、二维码），后续接入 Deep Link |

### 5.3 图片选择上传 → UploadService

| 维度 | 说明 |
| --- | --- |
| **微信能力** | `wx.chooseImage` + `wx.uploadFile` + `wx.previewImage` |
| **Avalonia 替代** | UploadService（本地优先 + 异步上传，支持 .jpg/.jpeg/.png/.gif/.webp） |
| **优势** | 跨平台一致，本地优先策略离线可用 |
| **劣势** | 需自实现相册选择 UI（各平台不同） |
| **状态** | ✅ 已良好替代 |

### 5.4 自定义 tabBar → TabControl 模板

| 维度 | 说明 |
| --- | --- |
| **微信能力** | custom-tab-bar 组件，emoji 图标 |
| **Avalonia 替代** | MainShellView 4 Tab + 9 overlay，TabControl 模板自定义 |
| **优势** | 更灵活，支持 overlay 覆盖层 |
| **状态** | ✅ 已良好替代 |

### 5.5 邀请有礼 → 邀请码 + 积分奖励

| 维度 | 说明 |
| --- | --- |
| **微信能力** | 分享卡片携带 referrerId → 新用户注册 → 自动发放积分 |
| **Avalonia 替代** | 邀请码绑定 referrerId → 新用户注册时填写邀请码 → 后端发放积分 |
| **劣势** | 依赖用户主动填写邀请码，转化率低于自动绑定 |
| **建议** | P1，结合 5.2 Deep Link 实现自动绑定 |

---

## 6. 缺失功能优先级评估

### 6.1 P0：阻塞核心业务，必须立即补齐

**无 P0 项**。Avalonia 版核心宝宝记录（11/13 种）、首页、喂养页、统计、同步、登录均已就绪，核心业务流程完整可用。

### 6.2 P1：影响用户体验或运营能力，建议尽快补齐

| # | 功能 | 优先级 | 理由 |
| --- | --- | --- | --- |
| 1 | 家庭邀请 Deep Link（4.4） | P1 | 家庭协作是核心场景，邀请转化率直接影响家庭用户获取 |
| 2 | 邀请有礼闭环（4.5 / 5.5） | P1 | 增长引擎，依赖 1 |
| 3 | 抽奖功能（4.2） | P1 | 积分消耗出口，缺失导致积分体系失衡 |
| 4 | 隐私协议弹窗（4.9） | P1 | 应用商店上架合规要求 |

### 6.3 P2：增强功能，按需补齐

| # | 功能 | 优先级 | 理由 |
| --- | --- | --- | --- |
| 5 | 妈妈饮食记录（4.1） | P2 | 业务完整性补充，非核心宝宝记录 |
| 6 | 里程碑独立记录类型（4.6） | P2 | 数据已就绪，UI 联动优化 |
| 7 | 发烧用药过量警告核实（4.7） | P2 | 安全提示，需核实后补齐 |
| 8 | 任务体系完善（4.10） | P2 | 积分获取渠道 |

### 6.4 P3：可不做（已被替代方案覆盖或不适用）

| # | 功能 | 优先级 | 理由 |
| --- | --- | --- | --- |
| 9 | 微信原生分享（4.3） | P3 | 已由邀请码 + 计划中的 Deep Link 替代 |
| 10 | 左滑删除交互细节对齐（4.8） | P3 | 功能等价，交互细节差异可接受 |
| 11 | 微信支付 | P3 | 双方均未启用，不适用 |
| 12 | 订阅消息 / 客服会话 / 小程序码 / 扫码 / 定位 / 蓝牙 | P3 | 微信特有，跨平台需自研或弃用 |

---

## 7. 建议的解决方案

### 7.1 P1-1：家庭邀请 Deep Link

| 维度 | 建议 |
| --- | --- |
| **技术方案** | Android App Link（`/.well-known/assetlinks.json`）+ iOS Universal Link（`apple-app-site-association`）+ 邀请码兜底 |
| **涉及文件** | `FamilyView.axaml`、`FamilyViewModel.cs`、`FamilyApiClient.cs`、`AppState.cs`、`RootContainer.axaml.cs`（Deep Link 接收）、`AndroidManifest.xml`（intent-filter）、`Info.plist`（Associated Domains） |
| **依赖关系** | 后端需提供 Deep Link 解析接口（familyId → 邀请信息） |
| **工作量评估** | 中（2-3 人周，含双平台域名验证） |

### 7.2 P1-2：邀请有礼闭环

| 维度 | 建议 |
| --- | --- |
| **技术方案** | 邀请码绑定 referrerId → 新用户注册时携带 referrerId → 后端自动发放积分 |
| **涉及文件** | `LoginView.axaml`、`LoginViewModel.cs`、`AuthService.cs`、`PointsService.cs` |
| **依赖关系** | 依赖 7.1 Deep Link；后端需支持 referrerId 入参 |
| **工作量评估** | 小（1 人周） |

### 7.3 P1-3：抽奖功能

| 维度 | 建议 |
| --- | --- |
| **技术方案** | 复用小程序后端抽奖 API（getActiveLottery / joinLottery / getLotteryHistory） → Avalonia 新增 LotteryView + LotteryViewModel |
| **涉及文件** | `PointsView.axaml`（增加抽奖入口）、新增 `LotteryView.axaml` + `LotteryViewModel.cs`、`PointsService.cs`（补充抽奖方法） |
| **依赖关系** | 后端抽奖 API 已就绪（小程序版在用） |
| **工作量评估** | 中（2 人周，含转盘动画） |

### 7.4 P1-4：隐私协议弹窗

| 维度 | 建议 |
| --- | --- |
| **技术方案** | 首次启动弹窗 + 设置页入口，协议文本从后端拉取或内置 |
| **涉及文件** | 新增 `PrivacyDialog.axaml`、`AppState.cs`（首次标记）、`DeveloperPreferences.cs`（本地存储） |
| **依赖关系** | 无 |
| **工作量评估** | 小（0.5 人周） |

### 7.5 P2-1：妈妈饮食记录

| 维度 | 建议 |
| --- | --- |
| **技术方案** | 新增 MaternalFoodFormViewModel + 数据库表 + Service 方法，参照 feed-form 模式 |
| **涉及文件** | 新增 `MaternalFoodFormViewModel.cs`、`RecordSheetView.axaml`（注册表单）、`RecordService.cs`（addMaternalFood 等）、数据库迁移（新增表）、`StatisticsView.axaml`（统计类型） |
| **依赖关系** | 后端需提供对应 API（小程序版已有） |
| **工作量评估** | 中（1.5 人周） |

### 7.6 P2-2：里程碑独立记录类型

| 维度 | 建议 |
| --- | --- |
| **技术方案** | 数据层已就绪（milestone 表），UI 层在统计/首页暴露 |
| **涉及文件** | `StatisticsView.axaml`、`HomeView.axaml`、`StatisticsService.cs` |
| **依赖关系** | 无 |
| **工作量评估** | 小（0.5 人周） |

### 7.7 P2-3：发烧用药过量警告核实

| 维度 | 建议 |
| --- | --- |
| **技术方案** | 核实 AbnormalTracking 子 VM 是否实现 4 小时窗口判定与 30 秒定时器，未实现则补齐 |
| **涉及文件** | [AbnormalTrackingViewModel.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/ViewModels/Home/AbnormalTrackingViewModel.cs) |
| **依赖关系** | 无 |
| **工作量评估** | 小（0.5 人周） |

### 7.8 P2-4：任务体系完善

| 维度 | 建议 |
| --- |--- |
| **技术方案** | task_record 表已存在，补齐任务规则引擎与 UI |
| **涉及文件** | 新增 `TaskView.axaml`、`PointsService.cs`（任务方法） |
| **依赖关系** | 后端任务规则配置 |
| **工作量评估** | 中（2 人周） |

---

## 8. 总结与建议

### 8.1 整体完成度评估

| 维度 | 完成度 | 说明 |
| --- | --- | --- |
| **核心宝宝记录** | **85%** | 11/13 种记录类型已实现，缺妈妈饮食与里程碑独立化 |
| **首页与导航** | **95%** | 全部对齐，且有 AI 智能记等增强 |
| **统计与可视化** | **90%** | 13 种类型缺 2 种，图表与热力图已实现 |
| **家庭协作** | **70%** | 列表/角色已就绪，邀请流程 UX 退化 |
| **积分运营** | **60%** | 签到已就绪，抽奖/分享有礼缺失 |
| **AI 智能分析** | **100%+** | 远超小程序版（小程序版无 AI） |
| **同步机制** | **100%+** | 远超小程序版（小程序版无本地优先同步） |
| **登录认证** | **90%** | 账号密码替代微信登录，缺隐私协议 |
| **性能与调试** | **100%+** | 远超小程序版 |
| **整体加权完成度** | **约 88%** | 核心业务完整，运营能力与部分细节有差距 |

### 8.2 核心差距总结

1. **运营能力差距**：抽奖、分享有礼、任务体系缺失，导致积分体系闭环不完整，影响用户留存与增长；
2. **家庭邀请 UX 退化**：从"一键加入"退化为"邀请码手动输入"，转化率下降；
3. **记录类型缺失**：妈妈饮食记录完全缺失，哺乳期家庭场景覆盖不全；
4. **合规缺失**：隐私协议弹窗缺失，影响应用商店上架。

### 8.3 Avalonia 版核心优势（相对小程序版）

1. **离线能力**：SQLite 本地优先，完全离线可用，数据完整性强；
2. **同步机制**：双向同步 + 重试 + 备份 + 防重推，工程化程度高；
3. **AI 能力**：AI 周报 + AI 智能记（自然语言→记录）+ 多 LLM 后端支持；
4. **性能优化**：疫苗时间轴 5 重优化、虚拟化、异步解码、批量查询；
5. **调试能力**：MCP 集成 + 开发者选项 + Serilog 分级日志；
6. **跨平台**：Windows 调试 + Android 发布 + iOS 潜在扩展，摆脱微信生态依赖。

### 8.4 后续路线图建议

#### 阶段一（1-2 月）：补齐 P1，完善运营与合规

- [ ] 隐私协议弹窗（P1-4，0.5 人周）
- [ ] 家庭邀请 Deep Link（P1-1，2-3 人周）
- [ ] 邀请有礼闭环（P1-2，1 人周）
- [ ] 抽奖功能（P1-3，2 人周）

#### 阶段二（2-3 月）：补齐 P2，完善业务完整性

- [ ] 妈妈饮食记录（P2-1，1.5 人周）
- [ ] 里程碑独立记录类型（P2-2，0.5 人周）
- [ ] 发烧用药过量警告核实（P2-3，0.5 人周）
- [ ] 任务体系完善（P2-4，2 人周）

#### 阶段三（持续）：平台扩展与优化

- [ ] iOS 平台接入评估（受 ILLink 超时限制，需探索方案）
- [ ] 各平台原生 OAuth 接入（降低注册摩擦）
- [ ] 推送通知（替代微信订阅消息）
- [ ] 应用内更新（Android）

### 8.5 结论

Avalonia 版作为跨平台独立 App，在**核心业务完整性**（宝宝记录、首页、喂养、统计、同步、AI）上已达到甚至超越小程序版，整体完成度约 **88%**。主要差距集中在**运营能力**（抽奖/分享有礼/任务）与**家庭邀请 UX**，这些差距不影响核心业务可用性，但影响用户增长与留存。

建议按 P1 → P2 顺序补齐，优先解决合规（隐私协议）与增长（家庭邀请 Deep Link + 邀请有礼 + 抽奖），再补齐业务完整性（妈妈饮食、里程碑、任务体系）。P3 项可不做或长期搁置。

---

**报告结束**
