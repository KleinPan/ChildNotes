# Ursa.Avalonia 开发参考

## 评估结论（2026-07-02）

经全面技术评估，**当前项目暂不引入 `Irihi.Ursa` NuGet 包**，但将其作为开发参考项目，供新需求或优化时借鉴实现思路。

### 不引入的原因

1. **Timeline 控件布局模型不匹配**：Ursa Timeline 是"单列竖线+左右交替内容"模型（Left/Center/Right/Alternate 四种模式），而本项目疫苗时间轴是"中列竖线+左右双列并排卡片"结构（左 FreeDoses + 中 TimelineAxisItem + 右 PaidDoses），两者本质不同，无法直接替代。
2. **性能优化不可复用**：现有疫苗时间轴的 5 重优化（预加载缓存、渐进式渲染、OneTime 绑定、预计算 bool 属性、原地更新）深度耦合于自研架构，迁移后会丢失。
3. **CalendarHeatMapControl 无对应控件**：Ursa 不提供日历热力图控件，该控件仍需保留为自研。
4. **Ursa 2.0 Breaking Changes**：要求 ViewModel 层依赖 Avalonia/Ursa 框架类型，违反项目"Shared 层不依赖 UI 框架"的契约。
5. **自研方案已稳定**：`TimelineAxisItem`（110 行）+ `CalendarHeatMapControl`（393 行）+ 内联模板已稳定运行，并有专项性能测试保障。

## Ursa 项目信息

| 维度 | 详情 |
|---|---|
| GitHub | https://github.com/irihitech/Ursa.Avalonia |
| NuGet 包名 | `Irihi.Ursa`（核心）/ `Irihi.Ursa.Themes.Semi`（主题） |
| 许可证 | MIT（可借鉴源码，无法律风险） |
| 项目地位 | .NET Foundation 正式成员项目 |
| GitHub Stars | 1,500+ |
| 最新稳定版 | 1.11.1（2025-05-31）；2.0 已在 GitHub 发布（2026-05-05），NuGet 待稳定 |
| 目标框架 | net8.0; net10.0 |
| 依赖 Avalonia 版本 | Ursa 2.0 要求 12.0.2（本项目用 12.0.5，兼容） |
| 主题依赖 | `Semi.Avalonia`（本项目已引用 12.0.3） |

## Ursa 控件清单（可参考的实现）

Ursa 提供 30+ 企业级控件，以下为开发新需求时可参考的控件：

| 控件 | 源码路径 | 参考价值 |
|---|---|---|
| **Timeline** | `src/Ursa/Controls/Timeline/` | 时间轴布局、列宽对齐算法 |
| **TagInput** | `src/Ursa/Controls/TagInput/` | 标签输入交互 |
| **AutoCompleteBox / MultiAutoCompleteBox** | `src/Ursa/Controls/AutoCompleteBox/` | 搜索过滤、多选 |
| **DateTimePicker 系列** | `src/Ursa/Controls/DateTimePicker/` | 日期时间选择（含 DateOnly/TimeOnly 变体） |
| **MultiComboBox / TreeComboBox** | `src/Ursa/Controls/ComboBox/` | 多选下拉、树形下拉 |
| **NavMenu** | `src/Ursa/Controls/NavMenu/` | 多级折叠导航 |
| **ElasticWrapPanel** | `src/Ursa/Controls/ElasticWrapPanel/` | 弹性换行布局 |
| **Anchor** | `src/Ursa/Controls/Anchor/` | 锚点定位 |
| **FormGroup / FormItem** | `src/Ursa/Controls/FormGroup/` | 表单分组+验证 |
| **NumericUpDown** | `src/Ursa/Controls/NumericUpDown/` | 数字调节器（11 种数值类型） |
| **ImageViewer** | `src/Ursa/Controls/ImageViewer/` | 图片查看器 |
| **Step** | `src/Ursa/Controls/Step/` | 步骤条 |
| **Drawer / Dialog / OverlayDialogHost** | `src/Ursa/Controls/Dialog/` | 抽屉、对话框、覆盖层 |
| **Toast / Notification** | `src/Ursa/Controls/Notification/` | 通知提示 |
| **Badge / Banner / Avatar / Breadcrumb** | 对应目录 | 装饰性控件 |
| **IconButton / ButtonGroup / IconSplitButton** | `src/Ursa/Controls/Buttons/` | 按钮系列 |
| **Clock** | `src/Ursa/Controls/Clock/` | 时钟控件 |

## 可借鉴的设计思路

### 1. Timeline 控件（`src/Ursa/Controls/Timeline/`）

**架构**：`Timeline : ItemsControl` + 自定义 `TimelinePanel : Panel` + `TimelineItem : HeaderedContentControl`

**借鉴点**：

- **首末项半截竖线**：通过 `:first` / `:last` 伪类 + 样式控制，让首末项的竖线只画一半。
  - 可应用到 `TimelineAxisItem`：增加 `IsFirst` / `IsLast` 属性，首末项竖线只画一半。
- **列宽自动对齐**：`TimelinePanel.MeasureOverride/ArrangeOverride` 遍历所有 `TimelineItem`，调用 `GetWidth()` 取 `left/mid/right` 三列最大值，通过 `SetWidth()` 统一设置到每个 item 的 `PART_RootGrid` 列宽。
  - 若未来疫苗时间轴中列宽度需动态化，可参考此模式。
- **`SetIfUnset` 模式**：容器属性未被显式设置时才用默认值，避免覆盖用户显式设置。
  - 可应用到 `CalendarHeatMapControl` 的 `CellWidth` 等属性。
- **数据绑定 API 设计**：4 个 `MemberBinding`（Icon/Header/Content/Time）+ 3 个 `DataTemplate`，声明式数据驱动。
  - 可参考到任何需要数据驱动渲染的控件。

**Ursa Timeline 局限性**（本项目疫苗时间轴无法直接套用的原因）：

| 特性 | Ursa Timeline | 本项目疫苗时间轴 |
|---|---|---|
| 数据结构 | 扁平 `TimelineItem[]` | 嵌套分组 `TimelineGroup[]` → `FreeDoses[]` + `PaidDoses[]` |
| 布局模型 | 单列竖线，内容 Left/Right/Alternate 交替 | 中列竖线，左右双列卡片并排 |
| 每行内容 | 1 个 TimelineItem | 1~2 个卡片（左免费 + 右自费） |
| 状态语义 | 通用 5 种（Default/Ongoing/Success/Warning/Error） | 业务 7 种（Done/Skipped/Replaced/Overdue/Due/Soon/Pending） |

### 2. 通用设计模式借鉴

- **`AffectsRender<T>` / `AffectsMeasure<T>`**：Ursa 大量使用这两个静态方法注册触发重绘/重测量的属性，比手动重写 `OnPropertyChanged` 更简洁。本项目 `TimelineAxisItem` 已采用此模式。
- **伪类驱动样式**：Ursa 用 `[PseudoClasses]` 特性声明多个伪类，通过 `PseudoClasses.Set()` 切换状态，比字符串 `Classes.xxx` 绑定更规范。
- **`FuncTemplate<T>` 默认面板**：`Timeline` 用 `FuncTemplate<Panel?>` 为 `ItemsPanelProperty` 提供默认值，避免 XAML 配置。本项目若新增类似 ItemsControl 可参考。

## 何时重新评估引入

以下场景出现时，可重新考虑引入 Ursa NuGet 包：

| 场景 | 触发条件 |
|---|---|
| 新增表单密集功能 | 需要大量 TagInput/NumericUpDown/DateTimePicker 等控件，自研成本超过集成成本 |
| 团队决定统一 UI 框架 | 多个项目需共享控件库，Ursa 作为统一标准 |
| Ursa 提供 CalendarHeatMap | 若 Ursa 未来新增热力图控件，可替代 `CalendarHeatMapControl` |
| Ursa 2.0 NuGet 稳定发布 | 等待 2.0 正式版在 NuGet 发布并稳定 3-6 个月 |

## 参考方式

- **源码借鉴**：Ursa 采用 MIT 协议，可直接阅读源码，将设计思路融入自研控件，无需引入 NuGet 包。
- **查阅路径**：通过 GitHub 在线浏览，或使用 mcp_gread 工具读取（`view_repo` / `read_code` / `search_code`）。
- **本地缓存**：评估时已完整分析 Timeline 控件的 6 个源文件（Timeline.cs / TimelineItem.cs / TimelinePanel.cs / TimelineDisplayMode.cs / TimelineItemType.cs / TimelineFormatConverter.cs），结论已记录在本文件。

## 相关本地文件

| 文件 | 说明 |
|---|---|
| [TimelineAxisItem.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/Controls/TimelineAxisItem.cs) | 自研时间轴单项（竖线+圆点），可借鉴 Ursa 首末项半截竖线 |
| [CalendarHeatMapControl.axaml](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/Controls/CalendarHeatMapControl.axaml) | 自研日历热力图，Ursa 无对应控件 |
| [CalendarHeatMapControl.axaml.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/Controls/CalendarHeatMapControl.axaml.cs) | 热力图 code-behind |
| [RecordSheetView.axaml](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/Views/RecordSheetView.axaml) | 疫苗时间轴内联模板（行 844-1107） |
| [VaccineCatalog.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/Services/VaccineCatalog.cs) | 疫苗时间轴数据模型（VaccinePlan/Group/View/Builder） |
| [VaccineFormViewModel.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/ViewModels/Forms/VaccineFormViewModel.cs) | 疫苗时间轴 ViewModel（含 5 重性能优化） |
| [VaccineTimelinePerformanceTests.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes.Tests/VaccineTimelinePerformanceTests.cs) | 疫苗时间轴性能基准测试 |
| [Directory.Packages.props](file:///e:/0_Code/5_Git/AiJi/ChildNotes/Directory.Packages.props) | NuGet 包版本管理（已含 Semi.Avalonia 12.0.3） |
