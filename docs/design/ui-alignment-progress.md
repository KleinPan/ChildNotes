# UI 样式对齐设计规范 — 实施进度

> 跟踪首页样式与 [design-tokens.md](design-tokens.md) 规范的对齐进度。
> 最近一次更新: 2026-08-10

## 目标

把 `ChildNotes/ChildNotes/Styles/` 下的样式系统从"基于 WeUI 命名"迁到"基于 DesignTokens 语义命名",
并把硬编码颜色 / 字号 / 圆角统一到 [design-tokens.md](design-tokens.md) 定义的 Token 体系。

## 总体进度

| 阶段 | 状态 | Commit | 备注 |
|---|---|---|---|
| 圆角/边距/阴影/首页字号 修复 | ✅ 完成 | 26f8369 | MCP 实测验证 |
| **P0 资源字典 DesignTokens.axaml** | ✅ 完成 | 19ea9a1 | 新建,语义命名 Token |
| **P1 Primary Button 规范** | ✅ 完成 | 19ea9a1 | 高度 48 / 圆角 24 / 字号 16 |
| **P3 Spacing/Motion/Semantic/AI Token** | ✅ 完成 | 19ea9a1 | 随 P0 一起落地 |
| **P4 section-title 字号规范** | ✅ 完成 | 19ea9a1 | 15sp SemiBold → 20sp Bold |
| **P2-A 硬编码颜色清单** | ✅ 完成 | (本批) | 282 处分布/分类 |
| **P2 主体清理(279 处)** | ⏸ **阻塞** | — | **需先决定色值迁移策略** |
| P0 WeUIColors 色值迁移 | ⏸ 待定 | — | 与 P2 同根 |

## 已落地的规范 Token (DesignTokens.axaml)

### 颜色

| Key | 值 | 规范 token |
|---|---|---|
| `Color.Brand.Primary` | #00C875 | color.brand.primary |
| `Color.Brand.PrimaryLight` | #E8FFF4 | color.brand.primaryLight |
| `Color.Brand.PrimaryDark` | #00A85A | color.brand.primaryDark |
| `Color.Growth.Blue` | #3B82F6 | color.growth.blue |
| `Color.Feeding.Orange` | #FF9F43 | color.feeding.orange |
| `Color.Sleep.Purple` | #8B7CF6 | color.sleep.purple |
| `Color.Medicine.Red` | #F56565 | — |
| `Color.Vaccine.Yellow` | #F6C344 | color.vaccine.yellow |
| `Color.Surface.Background` | #F5F6F8 | color.surface.background |
| `Color.Surface.Card` | #FFFFFF | color.surface.card |
| `Color.Text.Primary` | #1F2937 | color.text.primary |
| `Color.Text.Secondary` | #6B7280 | color.text.secondary |
| `Color.Semantic.{Success/Warning/Error/Info}` | 见 Token | semantic.* |
| `Color.AI.{Primary/Background}` | #7C5CFF / #F0FFF0 | color.ai.* |

每个 `Color.*` 都对应一个 `*Brush` 供控件 `Background/Foreground` 引用。

### 间距 / 圆角 / 字号

| 类别 | Token | 规范值 |
|---|---|---|
| Spacing | `Spacing.XS` … `Spacing.4XL` | 4 / 8 / 12 / 16 / 20 / 24 / 32 / 40 / 48 |
| Spacing | `Spacing.PageMargin` | 16,0,16,0 |
| Radius | `Radius.Small` / `Medium` / `Large` | 8 / 16 / 24 |
| Radius | `Radius.PrimaryButton` / `SecondaryButton` | 24 / 8 |
| FontSize | `FontSize.LargeTitle` … `FontSize.Minor` | 24 / 20 / 17 / 16 / 14 / 13 / 12 |
| Button | `Button.Primary.Height` | 48 |
| Motion | `Motion.Duration.Fast/Normal/Slow` | 80 / 200 / 400 ms |

## 已落地的样式调整

### 1. Primary Button 规范对齐 (WeUIStyles.axaml)

```xml
<Style Selector="Button.weui-btn.primary">
    <Setter Property="Height" Value="48" />           <!-- 规范 48 -->
    <Setter Property="MinHeight" Value="48" />
    <Setter Property="CornerRadius" Value="24" />     <!-- 规范 pill 24 -->
    <Setter Property="FontSize" Value="16" />         <!-- 规范 16 -->
    <Setter Property="Background" Value="{StaticResource WeuiGreenBrush}" />
    <Setter Property="Foreground" Value="White" />
    <Setter Property="BorderThickness" Value="0" />
</Style>
```

基础 `Button.weui-btn` 仍保留 `WeuiBtnRadius` (24) 作为默认圆角,
Primary 通过专属 Selector 强制覆盖为 pill 24。

### 2. section-title 字号规范对齐 (WeUIStyles.axaml)

```xml
<Style Selector="Border.section-title TextBlock">
    <Setter Property="FontSize" Value="20" />          <!-- 规范 FontSize.SectionTitle=20 -->
    <Setter Property="FontWeight" Value="Bold" />      <!-- 规范 SectionTitle=Bold -->
    <Setter Property="Foreground" Value="{StaticResource WeuiFg0Brush}" />
</Style>
```

涉及 11 个使用 section-title 的页面,字号统一从 15sp SemiBold 升至 20sp Bold。

## 待定项策略

### P2 硬编码颜色清理 (P2-A 报告)

**P2-A 扫描结论** (2026-08-10):

- **总硬编码色值**: 282 处,分布在 21 个 .axaml 文件(Styles 已不计)
- **文件 Top 5**: RecordSheetView(82) / GrowthView(35) / HomeView(37) / FeedingView(20) / BabySetupView(11)
- **按色值聚类**:

| 类别 | 出现频率 | 现有色值 | 规范 Token | 是否能直接替换 |
|---|---|---|---|---|
| 文字主色 | 频繁 | `#1A1A1A` / `#333333` | `TextPrimaryBrush`=`#1F2937` | ⚠️ 色相不同,会改外观 |
| 文字次色 | 频繁 | `#666666` / `#888888` / `#999999` | `TextSecondaryBrush`=`#6B7280` | ⚠️ 色相不同,会改外观 |
| 文字弱色 | 偶发 | `#BBBBBB` / `#BDBDBD` | `TextPlaceholderBrush`=`#9CA3AF` | ⚠️ 色相不同,会改外观 |
| 浅灰底 | 高频 | `#F5F5F5` / `#F0F0F0` / `#F9F9F9` | `SurfaceDisabledBrush`=`#F3F4F6` | ⚠️ 色相不同,会改外观 |
| 品牌主色 | 中频 | `#07C160` | `BrandPrimaryBrush`=`#00C875` | ⚠️ 色相不同,会改外观 |
| 品牌强调态 | 中频 | `#E8F9EF` | `BrandPrimaryLightBrush`=`#E8FFF4` | ⚠️ 色相不同,会改外观 |
| iOS 蓝 | 中频 | `#0A84FF` | `GrowthBlueBrush`=`#3B82F6` | ⚠️ 色相不同,会改外观 |
| 业务橙 | 中频 | `#E86F24` | `FeedingOrangeBrush`=`#FF9F43` | ⚠️ 色相不同,会改外观 |
| 业务橙强调态 | 中频 | `#FFF7F1` / `#FFE7D6` | (无) | 需新建 |
| 业务蓝强调态 | 中频 | `#F5FBFF` / `#E8F5FF` | (无) | 需新建 |
| 遮罩层 | 中频 | `#CC000000` / `#88000000` / `#80000000` / `#A0000000` | (无) | 需新建 `Mask*Brush` |
| 阴影参数 | 中频 | `#0A000000` / `#14000000` / `#08000000` / `#20000000` / `#15000000` | BoxShadow 仍硬编码 | 需新建 `Shadow*Brush` |
| 卡片白底 | 高频 | `White` / `#FFFFFF` | `SurfaceCardBrush`=`#FFFFFF` | ✅ **可零风险替换** |
| 暗底半透白 | 中频 | `#CCFFFFFF` / `#80FFFFFF` / `#E6FFFFFF` | (无) | 需新建 |

**关键发现**:

P2 硬编码颜色清理与 P0 WeUIColors 色值迁移是**同一个根因**: 现有项目以 WeUI 色值体系为基础,
设计规范定义了一套新色值。两套体系**色相/明度大量不一致**, 直接 Token 替换必然改变 UI 外观。

**两种推进路径**(需用户决策):

#### 路径 A: "语义对等"零外观变更

仅用同名/同色值的 Token 替换硬编码(目前能零风险替换的只有 `#FFFFFF` → `SurfaceCardBrush`)。
- 收益: 替换 50-80 处, 留下 ~200 处硬编码。
- 风险: 0。
- 适用: 想立刻清理一批,但不打算动外观。

#### 路径 B: "全量规范"整体迁移

按 design-tokens.md 全量替换,所有 UI 同步切换为规范色系。
- 收益: 282 处全部清理,符合设计规范, Token 100% 落地。
- 风险: 改外观,需要产品/设计确认 OK。涉及 21 个页面,工作量大,需拆 N 个 PR。
- 适用: 已经决定切换到新设计语言。

**当前状态**: 阻塞,等待用户决策走 A 还是 B。

## 验证手段

- **构建**: `cd ChildNotes && dotnet build ChildNotes\ChildNotes.csproj -v quiet --nologo`
- **MCP 视觉验证**: 通过 Keincheck MCP (`list_windows` / `screenshot_window` / `query_controls`)
  获取运行中 app 真实属性,与规范对比。

## 相关文件

| 文件 | 作用 |
|---|---|
| [DesignTokens.axaml](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/Styles/DesignTokens.axaml) | 语义命名 Token 资源字典(本批新建) |
| [DesignTokens.axaml.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/Styles/DesignTokens.axaml.cs) | 配套 code-behind |
| [WeUIColors.axaml](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/Styles/WeUIColors.axaml) | 旧 WeUI 命名(保留,渐进迁移) |
| [WeUIStyles.axaml](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/Styles/WeUIStyles.axaml) | 控件样式(本批调整 Primary/section-title) |
| [App.axaml](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/App.axaml) | 合并 DesignTokens.axaml 进 Application.Resources |
| [design-tokens.md](design-tokens.md) | 规范源文件 |
