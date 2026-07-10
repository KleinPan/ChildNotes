# 宝宝日记 第三方 SDK 清单

> 依据《App 收集使用个人信息自评估指南》《工业和信息化部 关于开展信息通信服务感知提升行动的通知》要求披露
> 更新日期：2026-07-10
> 适用应用：宝宝日记（Android 包名：`com.babydiary.app`）

## 一、第三方 SDK 清单

| # | SDK / 组件名称 | 提供方 | 用途 | 收集的个人信息 | 处理方式 | 隐私政策 |
|---|---|---|---|---|---|---|
| 1 | **Avalonia.Android** | .NET Foundation | UI 渲染框架 | 不收集 | 本地运行 | https://avaloniaui.net/ |
| 2 | **Microsoft.Data.Sqlite** | Microsoft | 本地数据库访问 | 不收集 | 本地运行 | https://www.microsoft.com/ |
| 3 | **SQLitePCLRaw.bundle_e_sqlite3** | SQLite 项目 | SQLite 原生库（e_sqlite3.so） | 不收集 | 本地运行 | https://sqlite.org/privacy.html |
| 4 | **CommunityToolkit.Mvvm** | Microsoft | MVVM 框架 | 不收集 | 本地运行 | https://www.microsoft.com/ |
| 5 | **Microsoft.Extensions.DependencyInjection** | Microsoft | 依赖注入容器 | 不收集 | 本地运行 | https://www.microsoft.com/ |
| 6 | **Microsoft.Extensions.Logging** | Microsoft | 日志框架 | 不收集 | 本地运行 | https://www.microsoft.com/ |
| 7 | **Serilog** | Serilog 团队 | 结构化日志 | 不收集 | 本地运行 | https://serilog.net/ |
| 8 | **Semi.Avalonia** | irihitech (GitHub) | Avalonia 主题控件库 | 不收集 | 本地运行 | https://github.com/irihitec/Semi.Avalonia |

## 二、用户主动启用的第三方服务（非默认）

以下服务在用户**主动配置**后才会启用，传输的数据受该服务自身隐私政策约束：

| # | 服务名称 | 启用条件 | 传输数据 | 服务方隐私政策 |
|---|---|---|---|---|
| 1 | OpenAI API | 用户在 AI 设置页配置 API Key | 用户主动提交的宝宝记录分析请求 | https://openai.com/privacy |
| 2 | Anthropic Claude API | 用户在 AI 设置页配置 API Key | 用户主动提交的宝宝记录分析请求 | https://www.anthropic.com/legal/privacy |
| 3 | 智谱 AI GLM API | 用户在 AI 设置页配置 API Key | 用户主动提交的宝宝记录分析请求 | https://www.zhipuai.cn/privacy |
| 4 | 阿里云通义千问 API | 用户在 AI 设置页配置 API Key | 用户主动提交的宝宝记录分析请求 | https://help.aliyun.com/learnig/privacy.html |
| 5 | 用户自有服务器 | 用户在数据同步页配置服务器地址 | 用户全部本地业务数据 | 由用户自行负责 |

**重要说明**：
- AI 智能分析功能默认关闭，仅在用户主动配置 LLM API Key 后启用
- 数据同步功能默认关闭，仅在用户主动配置服务器地址后启用
- 关闭上述功能后，应用不会向任何服务器传输任何业务数据

## 三、不集成的常见 SDK

为保护用户隐私，本应用**明确不集成**以下常见 SDK：

| 类型 | SDK 举例 | 不集成原因 |
|---|---|---|
| 广告 SDK | 穿山甲、优量汇、快手联盟 | 本应用无广告 |
| 统计 SDK | 友盟、TalkingData、神策 | 本应用不做用户行为分析 |
| 推送 SDK | 极光、个推、小米推送、华为推送 | 本应用当前不提供推送通知 |
| 地图 SDK | 高德、百度、腾讯地图 | 本应用不需要定位 |
| 登录 SDK | QQ 互联、微信开放平台、微博 | 本应用使用自有账号体系 |
| 崩溃监控 SDK | Bugly、友盟崩溃、Firebase Crashlytics | 当前未接入，后续评估 |
| 应用加固 | 爱加密、梆梆、360 加固 | 当前未接入，后续评估 |

## 四、个人信息收集清单（合规要求）

依据《App 收集使用个人信息自评估指南》要求：

| 业务功能 | 收集信息 | 类型 | 是否必需 | 用户可选关闭 |
|---|---|---|---|---|
| 注册登录 | 用户名、密码 | 个人基本信息 | 必需 | 注销账号 |
| 宝宝记录 | 宝宝姓名、性别、出生日期 | 个人基本信息 | 必需 | 删除宝宝 |
| 喂养记录 | 时间、喂养量、喂养方式 | 应用使用记录 | 必需 | 删除记录 |
| 疫苗记录 | 疫苗名称、接种时间、剂次 | 应用使用记录 | 必需 | 删除记录 |
| 成长记录 | 身高、体重、头围 | 个人生理信息 | 可选 | 不录入 / 删除记录 |
| 数据同步 | 设备 ID（GUID 本地生成） | 设备信息 | 同步功能必需 | 关闭同步 |
| 问题排查 | 脱敏应用日志 | 应用使用日志 | 必需 | 7 天自动清理 |

## 五、权限使用说明

| 权限 | 用途 | 申请时机 | 是否必需 |
|---|---|---|---|
| `android.permission.INTERNET` | 登录注册、数据同步、AI 分析 | 安装时静态申请 | 必需（核心功能依赖） |

**本应用不申请**以下敏感权限：位置、相机、麦克风、通讯录、存储、电话状态、短信。

## 六、联系方式

- **运营者**：个人开发者
- **联系邮箱**：`brianpan95@qq.com`
- **响应时限**：15 个工作日内答复

---

**声明**：本清单依据当前应用版本（v2.0）编制。如后续版本集成新的 SDK，将同步更新本清单。
