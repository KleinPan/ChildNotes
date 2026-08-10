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
| **P0 资源字典 DesignTokens.axaml** | ✅ 完成 | (本批) | 新建,语义命名 Token |
| **P1 Primary Button 规范** | ✅ 完成 | (本批) | 高度 48 / 圆角 24 / 字号 16 |
| **P3 Spacing/Motion/Semantic/AI Token** | ✅ 完成 | (本批) | 随 P0 一起落地 |
| **P4 section-title 字号规范** | ✅ 完成 | (本批) | 15sp SemiBold → 20sp Bold |
| P2 硬编码颜色清理(279 处) | ⏸ 待定 | — | 影响外观,需先与产品确认 |
| P0 WeUIColors 色值迁移 | ⏸ 待定 | — | 同 P2,见"待定项策略" |

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

### P2 硬编码颜色清理 (279 处, 27 个文件)

**风险**: 用现有 WeUI Token (`WeuiRedBrush`=#FA5151) 替换为新 Token (`MedicineRedBrush`=#F56565) 会
改变外观色相,需视觉确认。

**策略**:

- 不强删硬编码;先按"功能等价"原则在 DesignTokens 增加 `*AliasBrush` (如 `DangerBrush`=现有 `WeuiRedBrush` 值)
  保持外观一致,作为中间态。
- 待产品/设计确认新色值后,再批量替换为规范值。
- 单页面 PR 模式推进: 每次只动 1-2 个页面的硬编码,降低风险。

### P0 WeUIColors 色值迁移

**风险**: 现有 `WeuiGreenBrush`=#07C160 与规范 `BrandPrimary`=#00C875 色相不同,直接替换会改变
所有用到该色的 UI。

**策略**:

- 短期(本次): 不替换,保留 WeUI 命名,新增 DesignTokens 提供规范值。
- 中期: 在设计评审后, 用 `DynamicResource` 让 WeUIColors 引用 DesignTokens 的 Color.*,
  改一处生效全局。
- 长期: 删 WeUIColors,迁移完成。

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
