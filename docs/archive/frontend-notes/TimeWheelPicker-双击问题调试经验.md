# TimeWheelPicker 双击问题调试经验总结

## 问题现象

用户反馈 TimeWheelPicker 时间选择器需要**点击两次**才能弹出时间选择面板。

## 最终解决方案

**ToggleButton + IsChecked 双向绑定 + Popup.Closed 事件同步**

### 核心代码

**XAML 模板**（`TimeWheelPickerStyles.axaml`）：
```xml
<ToggleButton Name="PART_Toggle"
              IsChecked="{TemplateBinding IsDropdownOpen, Mode=TwoWay}"
              ... />
<Popup Name="PART_Popup"
       IsOpen="{TemplateBinding IsDropdownOpen}"
       IsLightDismissEnabled="True"
       ... />
```

**Code-behind**（`TimeWheelPicker.axaml.cs`）：
```csharp
// 监听 Popup.Closed 事件（关键！）
if (_popup is not null)
{
    _popup.Closed -= OnPopupClosedEvent;
    _popup.Closed += OnPopupClosedEvent;
}

private void OnPopupClosedEvent(object? sender, EventArgs e)
{
    // LightDismiss 关闭后强制同步状态
    if (IsDropdownOpen)
        SetCurrentValue(IsDropdownOpenProperty, false);
    if (_toggleButton is not null)
        _toggleButton.IsChecked = false;
}
```

---

## 失败方案记录

### 方案 1：Button + Click 事件（失败）

```csharp
_displayButton.Click += OnDisplayButtonClick;
```

**失败原因**：
- Button 默认 `Focusable="True"`
- 第一次点击只用于获取焦点，不触发 Click
- 第二次点击才真正触发 Click 事件

### 方案 2：Border + PointerPressed 事件（失败）

```xml
<Border Name="PART_DisplayButton" Cursor="Hand" />
```
```csharp
_displayButton.PointerPressed += OnDisplayAreaPointerPressed;
```

**失败原因**：
- TemplatedControl 级别的事件处理被模板拦截
- 没有解决根本的焦点竞争问题

### 方案 3：TemplatedControl.OnPointerPressed 覆写（失败）

```csharp
protected override void OnPointerPressed(PointerPressedEventArgs e)
{
    SetCurrentValue(IsDropdownOpenProperty, !IsDropdownOpen);
    e.Handled = true;
}
```

**失败原因**：
- 事件在 TemplatedControl 级别处理，但模板内部控件可能先消费事件
- 无法保证事件一定能到达外层

### 方案 4：Focusable = false + Border.PointerPressed（失败）

```csharp
Focusable = false;  // 禁用控件焦点
_displayButton.PointerPressed += OnDisplayAreaPointerPressed;
```

**失败原因**：
- 禁用 `TemplatedControl.Focusable` 不能阻止子元素获取焦点
- Border 的 PointerPressed 仍然被某些机制拦截

### 方案 5：Tunnel 路由策略 + GotFocus（参考 Ursa，失败）

```csharp
_displayButton.AddHandler(PointerPressedEvent, OnDisplayAreaPointerPressed, RoutingStrategies.Tunnel);
_displayButton.GotFocus += OnDisplayAreaGotFocus;
```

**失败原因**：
- Tunnel 策略确实能在事件冒泡前捕获
- 但 GotFocus 触发后设置 `IsDropdownOpen = true`，Popup 打开
- Popup 打开后 LightDismiss 检测到指针仍在原位置，立即关闭 Popup
- 导致看起来"没有反应"

### 方案 6：ToggleButton + IsChecked 双向绑定（部分成功）

```xml
<ToggleButton IsChecked="{TemplateBinding IsDropdownOpen, Mode=TwoWay}" />
```

**部分成功原因**：
- ToggleButton 内置点击切换逻辑，第一次点击就能切换 `IsChecked`
- ✅ 第一次点击能打开弹窗
- ❌ 但 LightDismiss 关闭 Popup 后，`IsChecked` 仍为 `true`，下次点击会切换为 `false` 而不是 `true`

---

## 根本原因分析

### 1. TemplateBinding 是单向的

```xml
<Popup IsOpen="{TemplateBinding IsDropdownOpen}" />
```

- `IsDropdownOpen → Popup.IsOpen`：✅ 正向绑定有效
- `Popup.IsOpen → IsDropdownOpen`：❌ 反向不生效

**LightDismiss 关闭流程**：
1. 用户点击 Popup 外部
2. Popup 内部将 `IsOpen` 设为 `false`
3. 但这个变化**不会回写**到 `IsDropdownOpen` 属性
4. `IsDropdownOpen` 仍然是 `true`
5. ToggleButton 的 `IsChecked` 也仍然是 `true`
6. 下次点击 ToggleButton → `IsChecked` 从 `true` 变为 `false` → 弹窗关闭（而不是打开）

### 2. Popup.Closed 事件 vs IsDropdownOpen 属性变化

| 事件类型 | 触发条件 | 可靠性 |
|---------|---------|--------|
| `IsDropdownOpen` 属性变化 | 仅当代码显式设置 `IsDropdownOpen = false` | ❌ LightDismiss 不触发 |
| `Popup.Closed` 事件 | Popup 以任何方式关闭（LightDismiss、代码关闭等） | ✅ 始终触发 |

---

## 关键经验教训

### 教训 1：不要用 Button 作为 Popup 触发器

- Button 的 Click 事件依赖焦点机制
- 第一次点击只用于获取焦点，第二次才触发 Click
- ❌ 不适合需要"单击立即响应"的场景

### 教训 2：TemplateBinding 不是双向绑定

- `{TemplateBinding Property}` 是单向绑定
- 如果需要双向同步，必须用 `{TemplateBinding Property, Mode=TwoWay}` 或监听事件
- **特别是 Popup 的 `IsOpen` 属性，LightDismiss 关闭后不会回写**

### 教训 3：Popup.Closed 事件是最可靠的状态同步点

- `Popup.Closed` 事件在任何关闭方式下都会触发
- 包括：LightDismiss、代码关闭、Esc 键关闭
- ✅ 应该在 `Popup.Closed` 中同步控件状态

### 教训 4：ToggleButton 是 ComboBox 类控件的标准做法

- Avalonia 原生 ComboBox 就是用的 ToggleButton + Popup
- ToggleButton 内置点击切换逻辑，无需手动处理事件
- 但需要配合 `Popup.Closed` 事件同步状态

### 教训 5：MCP 调试工具是验证 UI 问题的利器

通过 MCP 的 `get_properties` 可以直接查看运行时属性：
- `IsDropdownOpen`
- `IsChecked`
- `IsFocused`
- `IsKeyboardFocusWithin`

这比"截图看效果"更精确，能直接定位状态不同步问题。

---

## 正确的架构模式

```
TimeWheelPicker (TemplatedControl)
├── ToggleButton (PART_Toggle)
│   ├── IsChecked ←双向绑定→ IsDropdownOpen
│   └── 点击切换 IsChecked → 同步 IsDropdownOpen → Popup.IsOpen
└── Popup (PART_Popup)
    ├── IsOpen ←单向绑定← IsDropdownOpen
    ├── IsLightDismissEnabled = true
    └── Closed 事件 → 同步 IsDropdownOpen = false, IsChecked = false
```

### 数据流

```
用户点击 ToggleButton
    ↓
ToggleButton.IsChecked 切换
    ↓ (双向绑定)
IsDropdownOpen 切换
    ↓ (单向绑定)
Popup.IsOpen 切换
    ↓
弹窗打开/关闭

用户点击 Popup 外部 (LightDismiss)
    ↓
Popup 内部 IsOpen = false
    ↓
Popup.Closed 事件触发
    ↓ (代码处理)
IsDropdownOpen = false
ToggleButton.IsChecked = false
```

---

## 相关文件

| 文件 | 作用 |
|------|------|
| [TimeWheelPicker.axaml.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/Controls/TimeWheelPicker.axaml.cs) | 时间选择器 code-behind |
| [TimeWheelPickerStyles.axaml](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/Styles/TimeWheelPickerStyles.axaml) | 时间选择器样式模板 |
| [MomentumWheelList.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/Controls/MomentumWheelList.cs) | 物理惯性滚轮控件 |

---

## 参考资源

- **Ursa DateTimePicker 源码**：https://github.com/irihitech/Ursa.Avalonia
  - `src/Ursa/Controls/DateTimePicker/Base/DateTimePickerBaseT.cs`
  - 使用 TextBox + Tunnel PointerPressed + GotFocus 方案
- **Avalonia Popup 文档**：Popup 的 `IsLightDismissEnabled` 和 `Closed` 事件
