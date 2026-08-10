# UI 样式对齐设计规范 — 实施进度

> 跟踪首页样式与 [design-tokens.md](design-tokens.md) 规范的对齐进度。
> 最近一次更新: 2026-08-10 23:00 (路径 B 完成)

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
| **P2-A 硬编码颜色清单** | ✅ 完成 | 7f959c2 | 282 处分布/分类 |
| **P2 路径 A: White 批量清理(75 处)** | ✅ 完成 | 18070fc | 21 文件 White → SurfaceCardBrush |
| **P2 路径 A: 4 色值批量清理(27 处)** | ✅ 完成 | 6640991 / bcf7b5b | WeuiGreen/Orange/Red/SurfaceCard 同色零变更 |
| **P2 路径 B 试点: HomeView (25 处)** | ✅ 完成 | 6c96449 | A/B 截图验证 |
| **P2 路径 B 推进: FeedingView (14 处)** | ✅ 完成 | be339cb | |
| **P2 路径 B 推进: BabySetupView (11 处)** | ✅ 完成 | bce6f7a | |
| **P2 路径 B 推进: BabyManager + Family (11 处)** | ✅ 完成 | cbd954b | 新增 MaskLightBrush / BrandPrimaryAlpha10Brush |
| **P2 路径 B 推进: 14 个小文件 (34 处)** | ✅ 完成 | 34b4603 | |
| **P2 路径 B 推进: GrowthView (11 处)** | ✅ 完成 | 8ec57ae | 保留咖啡主题色系 |
| **P2 路径 B 推进: RecordSheetView (40 处)** | ✅ 完成 | 00811b0 | 保留疫苗业务色 |
| P0 WeUIColors 色值迁移 | ⏸ 待定 | — | 业务色尚需业务方补充 |
| 业务色统一 (积分金/疫苗/危险红) | ⏸ 待业务方 | — | DesignTokens 未定义 |

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

**路径 A 实际执行结果** (2026-08-10):

| 批次 | 替换色值 | Token | 处数 | Commit |
|---|---|---|---|---|
| 1 | `="White"` | `SurfaceCardBrush` | 75 | 18070fc |
| 2 | `#07C160` | `WeuiGreenBrush` | 16 | bcf7b5b |
| 2 | `#FFFFFF` | `SurfaceCardBrush` | 5 | bcf7b5b |
| 2 | `#FA9D3B` | `WeuiOrangeBrush` | 3 | bcf7b5b |
| 2 | `#FA5151` | `WeuiRedBrush` | 3 | bcf7b5b |

**累计 102 处零外观变更清理**(原 282 处的 36.2%)。

**路径 B 实际执行结果** (2026-08-10 23:00):

| 页面 | 处数 | Commit | 备注 |
|---|---|---|---|
| HomeView (试点) | 25 | 6c96449 | A/B 截图验证,MCP 实测通过 |
| FeedingView | 14 | be339cb | |
| BabySetupView | 11 | bce6f7a | |
| BabyManager + Family | 11 | cbd954b | 新增 MaskLightBrush / BrandPrimaryAlpha10Brush |
| 14 个小文件 (AiSettings/QuickInput/QuickMenu/Mine/InAppMessage/Language/Sync/Reminder/Help/DeveloperOptions/PrivacyConsent/Points/Statistics/Membership/AiAnalysis) | 34 | 34b4603 | |
| GrowthView | 11 | 8ec57ae | 保留咖啡主题色系 |
| RecordSheetView | 40 | 00811b0 | 保留疫苗业务色 |

**累计 146 处路径 B 迁移**(覆盖 19 个 .axaml 文件)。

**新增 Token** (DesignTokens.axaml):

| Token | 值 | 用途 |
|---|---|---|
| GrowthBlueLightBrush | #E0F2FE | 增长按钮浅蓝底/边 |
| FeedingOrangeLightBrush | #FFF3E0 | 喂奶按钮浅橙底/边 |
| MedicineRedLightBrush | #FED7D7 | 异常卡浅红边/底 |
| BrandPrimaryAlpha14Brush | #1400C875 | 品牌主色 14% alpha |
| BrandPrimaryAlpha10Brush | #1A00C875 | 品牌主色 10% alpha |
| MaskBrush | #CC000000 | 80% 黑遮罩(Toast 浮层) |
| MaskLightBrush | #80000000 | 50% 黑遮罩(弹层背景) |
| TextPlaceholderBrush | #9CA3AF | 文字弱色/placeholder |

**业务色保留** (DesignTokens 未定义,待业务方补充):

- 会员/积分金色: #FFB84D / #E67E22 / #FFF0E0 / #FFF7ED
- 疫苗业务色: #EDF1F7 / #9EE6C1 / #FFFBE8 / #F4D35E / #FFBF9B / #EEF8FF / #BFE8FF / #0675B8
- 危险红: #D32F2F
- 半透白: #CCFFFFFF / #E6FFFFFF / #80FFFFFF / #A0000000
- 阴影参数: #0A000000 / #20000000 / #15000000 / #40000000
- 特殊遮罩: #A0000000 (62.5% 遮罩,全屏预览)
- 咖啡主题色 (GrowthView 专属): #5D4037 / #8D6E63 / #A1887F / #6D4C41 / #2D1810 / #FFCC80 / #FFAB91 / #7E9B8E / #D0D0D0 / #DDDDDD
- 启动页品牌色 (LoadingView 专属): #FFF8E7 / #5D4037 / #8D6E63
- 业务色 (MineView 会员金): #FFB84D

**关键结论**: 路径 B 推进顺利,**19 个页面 146 处全部完成**,
业务色 75+ 处因 DesignTokens 未定义保留。
P0 色值迁移需要业务方确认是否统一品牌色系(从 WeUI #07C160 → DesignTokens #00C875)。

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
