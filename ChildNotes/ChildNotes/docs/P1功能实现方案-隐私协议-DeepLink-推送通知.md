# P1 功能实现方案：隐私协议 · 家庭邀请 Deep Link · 推送通知

> **方案日期**：2026-07-08
> **方案范围**：3 项 P1 功能 + 1 项路线图功能
> **依赖项目**：ChildNotes (Avalonia 前端) + ChildNotes.Backend (ASP.NET Core 后端) + ChildNotes.Android / ChildNotes.iOS
> **参考文档**：[Avalonia与小程序功能对比分析报告.md](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/docs/Avalonia与小程序功能对比分析报告.md)

---

## 目录

1. [隐私协议弹窗（P1-4）](#1-隐私协议弹窗p1-4)
2. [家庭邀请 Deep Link（P1-1）](#2-家庭邀请-deep-linkp1-1)
3. [邀请有礼闭环（P1-2）](#3-邀请有礼闭环p1-2)
4. [推送通知（替代微信订阅消息）](#4-推送通知替代微信订阅消息)
5. [工作量与里程碑](#5-工作量与里程碑)
6. [风险与依赖](#6-风险与依赖)

---

## 1. 隐私协议弹窗（P1-4）

### 1.1 现状

- **小程序版**：[pages/mine/index.js](file:///e:/0_Code/5_Git/AiJi参考/child-notes-front-z-master/pages/mine/index.js) 中 `onLogin` 调用 `wx.getUserProfile` 前检查 `agreedPrivacy`，并通过 `wx.openPrivacyContract()` 打开微信内置隐私协议。
- **Avalonia 版**：[Views/LoginView.axaml](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/Views/LoginView.axaml) 和 [ViewModels/LoginViewModel.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/ViewModels/LoginViewModel.cs) 完全没有隐私协议相关代码，注册/登录按钮直接可点击。
- **合规风险**：Android 应用商店（特别是国内）和 iOS App Store 均要求首次启动展示隐私政策，用户"同意"后才能进入。当前 Avalonia 版直接进入登录页，上架会被拒。

### 1.2 目标

- 首次启动 App（未同意隐私协议）时，弹出隐私协议弹窗，用户点击"同意并继续"才能进入登录页。
- 用户"不同意"则退出 App（Android 调 `Finish()`，iOS 调 `exit(0)`）。
- 同意后持久化标志，后续启动不再弹窗。
- 在"我的"页提供"隐私协议"入口，可随时查看。

### 1.3 技术方案

#### 1.3.1 持久化标志

复用 [DeveloperPreferences](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/Services/DeveloperPreferences.cs) 的 JSON 文件模式，新建 [Services/PrivacyConsent.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/Services/PrivacyConsent.cs)：

```csharp
namespace ChildNotes.Services;

/// <summary>
/// 隐私协议同意状态持久化。
/// 存储路径同 DeveloperPreferences：LocalApplicationData/ChildNotes/privacy-consent.json
/// </summary>
public sealed class PrivacyConsent
{
    public bool Agreed { get; set; }
    public DateTime? AgreedAt { get; set; }
    public string? Version { get; set; }  // 协议版本号，便于后续协议更新时重新弹窗

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChildNotes", "privacy-consent.json");

    public static PrivacyConsent Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<PrivacyConsent>(json) ?? new();
        }
        catch { return new(); }
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch { /* 非致命，下次再弹 */ }
    }

    /// <summary>当前协议版本号，每次协议内容更新时递增。</summary>
    public const string CurrentVersion = "2026.07.001";

    /// <summary>是否需要展示隐私协议弹窗（未同意 或 版本不一致）。</summary>
    public static bool ShouldShow()
    {
        var c = Load();
        return !c.Agreed || c.Version != CurrentVersion;
    }
}
```

#### 1.3.2 弹窗 UI

新建 [Views/PrivacyConsentView.axaml](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/Views/PrivacyConsentView.axaml)：复用 [DialogHost](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/Controls/DialogHost.axaml) 的模态遮罩 + 居中卡片风格。

**布局要点**：
- 标题："隐私政策"
- 正文：滚动区域，前 3 行加粗为"摘要"，下方为完整协议（可滚动）
- 链接：点击"查看完整协议"打开外部浏览器（用 `Launcher.LaunchUriAsync`）
- 按钮："不同意，退出" / "同意并继续"
- 关闭手势：禁用返回键关闭（强制用户做选择）

#### 1.3.3 启动流程改造

在 [App.axaml.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/App.axaml.cs) 的 `OnFrameworkInitializationCompleted` 中，恢复会话之前插入隐私协议检查：

```csharp
// 新增：隐私协议检查（在 TryRestoreSession 之前）
if (PrivacyConsent.ShouldShow())
{
    // 展示隐私协议弹窗，用户同意后才继续
    // 不同意 → 退出应用
    ShowPrivacyConsent(host);
    return;  // 不继续执行后续初始化
}
var restored = TryRestoreSession();
```

**移动端退出实现**：
- Android：[MainActivity.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes.Android/MainActivity.cs) 暴露 `Finish()` 方法供 Avalonia 层调用
- iOS：通过 `DependencyInjection` 或 `IApplicationExitService` 接口，iOS 端实现里调 `UIApplication.SharedApplication.PerformSelector(new ObjCRuntime.Selector("suspend"), null, 0)`（苹果不允许主动退出，建议 suspend）

#### 1.3.4 "我的"页入口

在 [Views/MineView.axaml](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/Views/MineView.axaml) 添加一行"隐私政策"，点击后展示完整协议（同首次弹窗的弹层）。

#### 1.3.5 协议内容存放

协议正文存为 `Assets/PrivacyPolicy.md`，通过 `AssetLoader` 读取。便于运营更新时只改 markdown 文件，重新发版。

### 1.4 涉及文件

| 操作 | 文件 |
|---|---|
| 新建 | `Services/PrivacyConsent.cs` |
| 新建 | `Views/PrivacyConsentView.axaml` + `.cs` |
| 新建 | `ViewModels/PrivacyConsentViewModel.cs` |
| 新建 | `Assets/PrivacyPolicy.md` |
| 修改 | [App.axaml.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/App.axaml.cs)（启动流程插入检查） |
| 修改 | [Views/MineView.axaml](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/Views/MineView.axaml) + [ViewModels/MineViewModel.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/ViewModels/MineViewModel.cs)（新增入口） |
| 修改 | [ChildNotes.Android/MainActivity.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes.Android/MainActivity.cs)（暴露 ExitApp 接口） |
| 新建 | `Services/IApplicationExit.cs` + 平台实现（Android/iOS/Desktop） |

### 1.5 工作量评估

- **小**（0.5 人周）：协议弹窗 UI + 持久化 + 启动流程改造
- 协议正文内容请法务/产品提供（不在开发工作量内）

### 1.6 验收标准

- [ ] 首次启动展示隐私协议弹窗，"不同意"退出 App
- [ ] "同意并继续"后进入登录页，再次启动不再弹窗
- [ ] "我的"页可查看隐私协议
- [ ] 协议版本号变化后，已同意用户再次启动会重新弹窗

---

## 2. 家庭邀请 Deep Link（P1-1）

### 2.1 现状

- **小程序版**：家人管理页通过 `onShareAppMessage` 分享带 `inviteBabyId` / `inviteRoleCode` / `inviteRoleName` 参数的链接，被邀请人打开小程序时 [app.js](file:///e:/0_Code/5_Git/AiJi参考/child-notes-front-z-master/app.js) 的 `onLaunch` / `onShow` 捕获参数，已登录则直接 `joinFamilyViaInvite`，未登录则保存到 globalData 待登录后处理。
- **Avalonia 版**：[FamilyViewModel.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/ViewModels/FamilyViewModel.cs) 的 `OpenJoin` / `ConfirmJoin` 让用户**手动输入宝宝 ID + 选角色**，UX 退化严重（需要切换到 Family 页 → 点"加入" → 输入长串 ID → 选角色 → 确认）。
- **后端已就绪**：[BabyController.JoinFamily](file:///e:/0_Code/5_Git/AiJi/ChildNotes.Backend/ChildNotes.Api/Controllers/BabyController.cs#L38-L40) 接受 `JoinFamilyRequest { BabyId, RoleCode, RoleName? }`，[BabyService.JoinFamilyViaInviteAsync](file:///e:/0_Code/5_Git/AiJi/ChildNotes.Backend/ChildNotes.Infrastructure/Services/BabyService.cs#L202) 会一次性把当前用户加到该宝宝主人名下所有宝宝下，无需前端处理多宝宝同步。

### 2.2 目标

- 邀请方在"家人管理"页点击"邀请家人"，选择角色后生成邀请链接，可分享到微信/QQ/其他应用。
- 被邀请方点击链接：
  - App 已安装 → 唤起 App → 已登录则直接调用 `JoinFamily` → 弹出"加入成功"提示 → 自动刷新家庭列表
  - App 已安装 → 唤起 App → 未登录则保存邀请参数到本地，登录完成后自动处理
  - App 未安装 → 打开应用商店下载页（Android）/ 展示引导页（iOS 暂无应用商店落地页则提示）

### 2.3 技术方案

#### 2.3.1 Deep Link 协议设计

**统一 URL 格式**（跨平台）：

```
https://childnotes.app/invite?babyId={babyId}&roleCode={roleCode}&roleName={roleName}
```

- 用 HTTPS 域名而非自定义 scheme：iOS Universal Links 和 Android App Links 都要求 HTTPS 域名，且能避免与其他 App 冲突。
- 退化为自定义 scheme 作为兜底：`childnotes://invite?babyId=...&roleCode=...`

#### 2.3.2 平台配置

**Android App Links**（[ChildNotes.Android/Properties/AndroidManifest.xml](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes.Android/Properties/AndroidManifest.xml)）：

```xml
<activity android:name="com.CompanyName.ChildNotes.MainActivity"
          android:exported="true">
    <intent-filter android:autoVerify="true">
        <action android:name="android.intent.action.VIEW" />
        <category android:name="android.intent.category.DEFAULT" />
        <category android:name="android.intent.category.BROWSABLE" />
        <data android:scheme="https"
              android:host="childnotes.app"
              android:pathPrefix="/invite" />
    </intent-filter>
    <intent-filter>
        <action android:name="android.intent.action.VIEW" />
        <category android:name="android.intent.category.DEFAULT" />
        <category android:name="android.intent.category.BROWSABLE" />
        <data android:scheme="childnotes" />
    </intent-filter>
</activity>
```

**iOS Universal Links**（[ChildNotes.iOS/Info.plist](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes.iOS/Info.plist)）：

```xml
<key>com.apple.developer.associated-domains</key>
<array>
    <string>applinks:childnotes.app</string>
</array>
```

#### 2.3.3 后端配置：apple-app-site-association 和 assetlinks.json

在 `childnotes.app` 域名根目录部署两个验证文件：

- `/.well-known/apple-app-site-association`：iOS Universal Links 验证
- `/.well-known/assetlinks.json`：Android App Links 验证

这两个文件由后端静态文件服务或 CDN 提供，部署一次即可。

#### 2.3.4 安卓端 Intent 捕获

修改 [MainActivity.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes.Android/MainActivity.cs) 的 `OnCreate`：

```csharp
protected override void OnCreate(Bundle? savedInstanceState)
{
    base.OnCreate(savedInstanceState);
    // ... 现有代码 ...

    // 捕获 Deep Link Intent
    var intent = Intent;
    var data = intent?.Data;
    if (data is not null)
    {
        var url = data.ToString();
        Log.Info("ChildNotes", $"DeepLink received: {url}");
        // 通过 App 静态方法传递到跨平台层
        App.HandleDeepLink(url);
    }
}

// App 在 OnNewIntent 时也会被调用（已运行时再次点击链接）
protected override void OnNewIntent(Intent? intent)
{
    base.OnNewIntent(intent);
    var data = intent?.Data;
    if (data is not null)
    {
        App.HandleDeepLink(data.ToString());
    }
}
```

#### 2.3.5 跨平台 Deep Link 处理

新建 [Services/DeepLinkService.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/Services/DeepLinkService.cs)：

```csharp
namespace ChildNotes.Services;

/// <summary>
/// Deep Link 解析与处理。
/// 处理两类链接：
/// - https://childnotes.app/invite?babyId=xxx&roleCode=xxx&roleName=xxx
/// - childnotes://invite?babyId=xxx&roleCode=xxx&roleName=xxx
/// </summary>
public sealed class DeepLinkService
{
    private readonly FamilyApiClient _familyApi;
    private readonly AppState _state;
    private readonly AuthService _auth;

    /// <summary>待处理的邀请（已登录前收到的链接，登录后自动处理）。</summary>
    private PendingInvite? _pending;

    public DeepLinkService(FamilyApiClient familyApi, AppState state, AuthService auth)
    {
        _familyApi = familyApi;
        _state = state;
        _auth = auth;
    }

    /// <summary>处理 Deep Link URL。</summary>
    public async Task HandleAsync(string url)
    {
        if (!TryParseInvite(url, out var invite)) return;

        if (!_auth.IsLoggedIn)
        {
            // 未登录：暂存邀请，登录完成后处理
            _pending = invite;
            DevLogger.Log("DeepLink", $"Pending invite stored: babyId={invite.BabyId}");
            return;
        }

        await JoinFamilyAsync(invite);
    }

    /// <summary>登录成功后调用，处理暂存的邀请。</summary>
    public async Task ProcessPendingAsync()
    {
        if (_pending is null) return;
        var invite = _pending;
        _pending = null;
        await JoinFamilyAsync(invite);
    }

    private async Task JoinFamilyAsync(PendingInvite invite)
    {
        try
        {
            var member = await _familyApi.JoinFamilyAsync(invite.BabyId, invite.RoleCode);
            if (member is null)
            {
                ToastNotifier.Show("加入家庭失败，请稍后重试");
                return;
            }
            _familyApi.InvalidateFamiliesCache();
            ToastNotifier.Show($"已加入家庭，角色：{FamilyRoles.GetRoleName(invite.RoleCode)}");
            // 通知 UI 刷新家庭列表（如果 Family 页已打开）
            FamilyJoined?.Invoke();
        }
        catch (Exception ex)
        {
            DevLogger.Log("DeepLink", $"JoinFamily failed: {ex}");
            ToastNotifier.Show("加入家庭失败：" + ex.Message);
        }
    }

    public event Action? FamilyJoined;

    private static bool TryParseInvite(string url, out PendingInvite invite)
    {
        invite = default;
        try
        {
            var uri = new Uri(url);
            if (uri.AbsolutePath != "/invite" && uri.PathAndQuery != "/invite") return false;
            var q = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var babyId = q["babyId"];
            var roleCode = q["roleCode"];
            if (string.IsNullOrEmpty(babyId) || string.IsNullOrEmpty(roleCode)) return false;
            invite = new PendingInvite(babyId, roleCode, q["roleName"]);
            return true;
        }
        catch { return false; }
    }

    public record PendingInvite(string BabyId, string RoleCode, string? RoleName);
}
```

#### 2.3.6 App 入口接入

修改 [App.axaml.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/App.axaml.cs)：

```csharp
public static void HandleDeepLink(string url)
{
    // 主线程执行
    Dispatcher.UIThread.Post(async () =>
    {
        var svc = ServiceProvider.Instance.DeepLinkService;
        await svc.HandleAsync(url);
    });
}

private void OnLoginSucceeded()
{
    // ... 现有代码 ...
    // 登录成功后处理暂存的 Deep Link 邀请
    _ = ServiceProvider.Instance.DeepLinkService.ProcessPendingAsync();
}
```

#### 2.3.7 邀请方：生成分享链接

修改 [FamilyViewModel.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/ViewModels/FamilyViewModel.cs)，新增 `OpenInvite` 方法的升级版：

```csharp
[ObservableProperty] private bool _isInviteSheetOpen;
[ObservableProperty] private string _inviteRoleCode = "mother";
[ObservableProperty] private string _inviteUrl = string.Empty;

public void OpenInvite(BabyFamilyItem family)
{
    _inviteBabyId = family.BabyId;
    InviteRoleCode = "mother";
    InviteUrl = string.Empty;  // 选角色后才生成
    IsInviteSheetOpen = true;
}

[RelayCommand]
private void GenerateInviteUrl()
{
    var serverUrl = ServiceProvider.Instance.SyncConfigRepository.Get().ServerUrl;
    // 优先用配置的服务器地址作为 host，否则用默认域名
    var host = !string.IsNullOrEmpty(serverUrl)
        ? new Uri(serverUrl).Host
        : "childnotes.app";
    InviteUrl = $"https://{host}/invite?babyId={_inviteBabyId}&roleCode={InviteRoleCode}&roleName={FamilyRoles.GetRoleName(InviteRoleCode)}";
}

[RelayCommand]
private async Task ShareInviteAsync()
{
    if (string.IsNullOrEmpty(InviteUrl)) GenerateInviteUrl();
    var clipboard = ServiceProvider.Instance.MainView?.Clipboard;
    if (clipboard is not null)
    {
        await clipboard.SetTextAsync(InviteUrl);
        DisplayToast("邀请链接已复制，可粘贴到微信/QQ发送");
    }
}
```

UI 改造：在 [Views/FamilyView.axaml](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/Views/FamilyView.axaml) 的家庭卡片上添加"邀请家人"按钮，点击后弹出底部 sheet：选角色 → 自动生成链接 → 复制到剪贴板 / 调用系统分享面板。

**系统分享面板**（可选增强）：用 Avalonia 的 `Launcher` 或平台特定 API 调起系统分享：

```csharp
// Android：Android.Net.Uri + Intent.ACTION_SEND
// iOS：UIActivityViewController
// 通过 IShareService 接口抽象，平台实现
```

### 2.4 涉及文件

| 操作 | 文件 |
|---|---|
| 新建 | `Services/DeepLinkService.cs` |
| 新建 | `Services/IShareService.cs` + 平台实现（Android/iOS/Desktop） |
| 修改 | [App.axaml.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/App.axaml.cs)（HandleDeepLink 入口 + 登录后处理） |
| 修改 | [ViewModels/FamilyViewModel.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/ViewModels/FamilyViewModel.cs)（生成邀请链接 + 分享） |
| 修改 | [Views/FamilyView.axaml](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/Views/FamilyView.axaml)（邀请按钮 + 邀请 sheet） |
| 修改 | [ChildNotes.Android/MainActivity.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes.Android/MainActivity.cs)（OnCreate + OnNewIntent 捕获 Intent） |
| 修改 | [ChildNotes.Android/Properties/AndroidManifest.xml](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes.Android/Properties/AndroidManifest.xml)（intent-filter） |
| 修改 | [ChildNotes.iOS/Info.plist](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes.iOS/Info.plist)（associated-domains） |
| 修改 | [ChildNotes.iOS/AppDelegate.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes.iOS/AppDelegate.cs)（Handle Universal Links） |
| 后端 | 部署 `/.well-known/apple-app-site-association` 和 `assetlinks.json` |

### 2.5 工作量评估

- **中**（2-3 人周）
  - 跨平台 Deep Link 解析与处理：0.5 人周
  - Android App Links 配置与测试：0.5 人周
  - iOS Universal Links 配置与测试：0.5 人周（需苹果开发者账号 + 域名 HTTPS）
  - 邀请 UI 改造 + 分享面板：0.5 人周
  - 域名验证文件部署 + 端到端测试：0.5-1 人周

### 2.6 风险

1. **域名要求**：Android App Links 和 iOS Universal Links 都要求 HTTPS 域名 + 部署验证文件。若没有域名，可退化为自定义 scheme（`childnotes://`），但 UX 较差（浏览器会弹"打开 App"确认框）。
2. **iOS 开发者账号**：Universal Links 需要付费 Apple Developer 账号。本项目当前 iOS 不是发布平台（见项目规则），可只做 Android。
3. **国内厂商兼容**：小米/华为/OPPO/Vivo 对 App Links 支持不一，需真机测试。微信内置浏览器可能拦截外部 App 跳转，需要引导用户用系统浏览器打开。

### 2.7 验收标准

- [ ] 邀请方在家人管理页点击"邀请家人"，选角色后生成邀请链接
- [ ] 链接可复制到剪贴板，或调起系统分享面板
- [ ] 被邀请方点击链接，App 已安装时唤起 App
- [ ] App 已登录：自动调用 JoinFamily，弹出"加入成功"提示
- [ ] App 未登录：进入登录页，登录成功后自动处理邀请
- [ ] App 未安装：浏览器展示下载引导页（Android）

---

## 3. 邀请有礼闭环（P1-2）

### 3.1 现状

- **小程序版**：[pages/points/index.js](file:///e:/0_Code/5_Git/AiJi参考/child-notes-front-z-master/pages/points/index.js) 的 `onShareAppMessage` 携带 `referrer_id`，被邀请人打开小程序后 [app.js](file:///e:/0_Code/5_Git/AiJi参考/child-notes-front-z-master/app.js) 捕获 `referrer_id` 并存入 globalData，注册时传给后端，后端 [InviteService.BindReferrerAsync](file:///e:/0_Code/5_Git/AiJi/ChildNotes.Backend/ChildNotes.Infrastructure/Services/InviteService.cs#L34) 给邀请人加分。
- **Avalonia 版**：
  - 前端 [AuthService.Register](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/Services/AuthService.cs#L32) 和 [TryRegisterOnServerAsync](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/Services/AuthService.cs#L83) **没有传 referrerId** 字段。
  - 后端 [AuthDtos.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes.Backend/ChildNotes.Core/Dtos/AuthDtos.cs) 的 `RegisterRequest` **没有 referrerId 字段**。
  - 后端 [AuthService.RegisterAsync](file:///e:/0_Code/5_Git/AiJi/ChildNotes.Backend/ChildNotes.Infrastructure/Services/AuthService.cs#L27) **没有调用 InviteService.BindReferrerAsync**。
  - 前端 [PointsViewModel.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/ViewModels/PointsViewModel.cs) 没有展示"邀请记录"。

### 3.2 目标

- 邀请方在"积分"页点击"邀请好友"，生成专属邀请链接（携带 `referrerId`，即邀请人的用户 ID）。
- 被邀请人通过链接注册后，邀请人获得积分奖励。
- 邀请方可在"积分"页查看邀请记录（已邀请多少人、获得多少积分）。

### 3.3 技术方案

#### 3.3.1 后端：RegisterRequest 增加 referrerId

修改 [ChildNotes.Backend/ChildNotes.Core/Dtos/AuthDtos.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes.Backend/ChildNotes.Core/Dtos/AuthDtos.cs)：

```csharp
public class RegisterRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? NickName { get; set; }
    /// <summary>邀请人用户 ID（可选，新用户注册时绑定）。</summary>
    public string? ReferrerId { get; set; }
}
```

#### 3.3.2 后端：AuthService.RegisterAsync 调用 BindReferrerAsync

修改 [ChildNotes.Backend/ChildNotes.Infrastructure/Services/AuthService.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes.Backend/ChildNotes.Infrastructure/Services/AuthService.cs)：

```csharp
public class AuthService : IAuthService
{
    private readonly ChildNotesDbContext _db;
    private readonly JwtTokenService _jwt;
    private readonly ICurrentUserService _current;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IInviteService _invite;  // 新增

    public AuthService(ChildNotesDbContext db, JwtTokenService jwt, ICurrentUserService current,
                       IPasswordHasher passwordHasher, IInviteService invite)
    {
        _db = db;
        _jwt = jwt;
        _current = current;
        _passwordHasher = passwordHasher;
        _invite = invite;
    }

    public async Task<LoginResponse> RegisterAsync(RegisterRequest req, CancellationToken ct = default)
    {
        // ... 现有校验与用户创建代码 ...

        await EnsureUserPointsAsync(user.Id, ct);

        // 新增：绑定邀请人（新用户才绑定，老用户登录不触发）
        if (!string.IsNullOrEmpty(req.ReferrerId))
        {
            await _invite.BindReferrerAsync(user.Id, req.ReferrerId, newUser: true, ct);
        }

        return await BuildLoginResponseAsync(user, true, ct);
    }
}
```

注意：[InviteService.BindReferrerAsync](file:///e:/0_Code/5_Git/AiJi/ChildNotes.Backend/ChildNotes.Infrastructure/Services/InviteService.cs#L34) 已有完整实现（含事务 + 幂等校验），无需修改。

#### 3.3.3 前端：RegisterRequest 增加 referrerId

修改 [ChildNotes/ChildNotes/Services/AuthService.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/Services/AuthService.cs) 的 `TryRegisterOnServerAsync`：

```csharp
private async Task TryRegisterOnServerAsync(string username, string password, string? nickName)
{
    try
    {
        var cfg = _cfgRepo.Get();
        var serverUrl = cfg.ServerUrl;
        if (string.IsNullOrWhiteSpace(serverUrl)) return;

        // 新增：读取暂存的邀请人 ID
        var referrerId = ReferrerStorage.Load();

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var body = JsonSerializer.Serialize(new
        {
            username,
            password,
            nickName = string.IsNullOrWhiteSpace(nickName) ? username : nickName,
            referrerId,  // 新增字段
        });
        // ... 其余代码不变 ...
    }
    catch { /* ... */ }
}
```

#### 3.3.4 前端：暂存 referrerId

新建 [Services/ReferrerStorage.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/Services/ReferrerStorage.cs)（与 PrivacyConsent 同模式）：

```csharp
namespace ChildNotes.Services;

/// <summary>
/// 暂存从 Deep Link 捕获的邀请人 ID。
/// 注册成功后清除（避免重复绑定）。
/// </summary>
public static class ReferrerStorage
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChildNotes", "referrer.txt");

    public static string? Load()
    {
        try { return File.Exists(FilePath) ? File.ReadAllText(FilePath).Trim() : null; }
        catch { return null; }
    }

    public static void Save(string? referrerId)
    {
        try
        {
            if (string.IsNullOrEmpty(referrerId)) return;
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, referrerId);
        }
        catch { /* 非致命 */ }
    }

    public static void Clear()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); }
        catch { /* 非致命 */ }
    }
}
```

#### 3.3.5 前端：Deep Link 捕获 referrerId

扩展 [DeepLinkService](#235-跨平台-deep-link-处理) 支持邀请链接：

```csharp
private static bool TryParseInvite(string url, out PendingInvite invite)
{
    invite = default;
    try
    {
        var uri = new Uri(url);
        var q = System.Web.HttpUtility.ParseQueryString(uri.Query);

        // 家庭邀请：babyId + roleCode
        var babyId = q["babyId"];
        var roleCode = q["roleCode"];
        if (!string.IsNullOrEmpty(babyId) && !string.IsNullOrEmpty(roleCode))
        {
            invite = new PendingInvite(babyId, roleCode, q["roleName"], null);
            return true;
        }

        // 邀请有礼：referrerId
        var referrerId = q["referrerId"];
        if (!string.IsNullOrEmpty(referrerId))
        {
            invite = new PendingInvite(null, null, null, referrerId);
            return true;
        }

        return false;
    }
    catch { return false; }
}

public record PendingInvite(string? BabyId, string? RoleCode, string? RoleName, string? ReferrerId);
```

在 `HandleAsync` 中分流：

```csharp
public async Task HandleAsync(string url)
{
    if (!TryParseInvite(url, out var invite)) return;

    // 邀请有礼：暂存 referrerId，注册时使用
    if (!string.IsNullOrEmpty(invite.ReferrerId))
    {
        ReferrerStorage.Save(invite.ReferrerId);
        DevLogger.Log("DeepLink", $"ReferrerId stored: {invite.ReferrerId}");
        // 若未登录，提示用户注册
        if (!_auth.IsLoggedIn)
        {
            ToastNotifier.Show("已收到邀请，注册后双方都可获得积分奖励");
        }
        return;
    }

    // 家庭邀请：原有逻辑
    if (!_auth.IsLoggedIn)
    {
        _pending = invite;
        return;
    }
    await JoinFamilyAsync(invite);
}
```

#### 3.3.6 前端：积分页展示邀请记录

修改 [PointsViewModel.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/ViewModels/PointsViewModel.cs) 和 [PointsService](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/Services/PointsService.cs)：

```csharp
// PointsViewModel 新增
public ObservableCollection<InviteRecordItem> InviteRecords { get; } = new();

private void ApplyDashboard(PointsDashboard dashboard)
{
    // ... 现有代码 ...
    InviteRecords.Clear();
    foreach (var r in dashboard.InviteRecords) InviteRecords.Add(new InviteRecordItem(r));
}

// 邀请按钮命令
[RelayCommand]
private async Task InviteFriendAsync()
{
    var me = _auth.CurrentUser;
    if (me is null) return;
    var serverUrl = ServiceProvider.Instance.SyncConfigRepository.Get().ServerUrl;
    var host = !string.IsNullOrEmpty(serverUrl) ? new Uri(serverUrl).Host : "childnotes.app";
    var inviteUrl = $"https://{host}/invite?referrerId={me.Id}";

    var clipboard = ServiceProvider.Instance.MainView?.Clipboard;
    if (clipboard is not null)
    {
        await clipboard.SetTextAsync(inviteUrl);
        DisplayToast("邀请链接已复制，发给好友注册即可获得积分");
    }
}
```

#### 3.3.7 后端：PointsDashboard 返回邀请记录

后端 [PointsService.GetDashboardAsync](file:///e:/0_Code/5_Git/AiJi/ChildNotes.Backend/ChildNotes.Infrastructure/Services/PointsService.cs) 已调用 [InviteService.GetInviteRecordsAsync](file:///e:/0_Code/5_Git/AiJi/ChildNotes.Backend/ChildNotes.Infrastructure/Services/InviteService.cs#L28)，需确认 `PointsDashboardResponse` 包含 `InviteRecords` 字段（若无则补上）。

### 3.4 涉及文件

| 操作 | 文件 |
|---|---|
| 修改 | [ChildNotes.Backend/ChildNotes.Core/Dtos/AuthDtos.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes.Backend/ChildNotes.Core/Dtos/AuthDtos.cs)（RegisterRequest 加 ReferrerId） |
| 修改 | [ChildNotes.Backend/ChildNotes.Infrastructure/Services/AuthService.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes.Backend/ChildNotes.Infrastructure/Services/AuthService.cs)（注入 IInviteService，RegisterAsync 调用 BindReferrerAsync） |
| 新建 | `ChildNotes/ChildNotes/Services/ReferrerStorage.cs` |
| 修改 | [ChildNotes/ChildNotes/Services/AuthService.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/Services/AuthService.cs)（TryRegisterOnServerAsync 传 referrerId） |
| 修改 | `ChildNotes/ChildNotes/Services/DeepLinkService.cs`（支持 referrerId 解析） |
| 修改 | [ChildNotes/ChildNotes/ViewModels/PointsViewModel.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/ViewModels/PointsViewModel.cs)（邀请记录展示 + 邀请按钮） |
| 修改 | [ChildNotes/ChildNotes/Views/PointsView.axaml](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/Views/PointsView.axaml)（邀请按钮 + 邀请记录列表） |
| 修改 | [ChildNotes/ChildNotes/Services/PointsService.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/Services/PointsService.cs)（DTO 含 InviteRecords） |

### 3.5 工作量评估

- **中**（1 人周）
  - 后端 RegisterRequest 扩展 + BindReferrer 接入：0.2 人周
  - 前端 referrer 暂存 + 注册传递 + DeepLink 扩展：0.3 人周
  - 积分页邀请记录展示 + 邀请按钮 UI：0.3 人周
  - 端到端测试：0.2 人周

### 3.6 依赖

- **强依赖第 2 项（家庭邀请 Deep Link）**：复用 Deep Link 基础设施（URL 协议、平台配置、Intent 捕获）
- **弱依赖**：积分系统已有（签到奖励已就绪）

### 3.7 验收标准

- [ ] 积分页展示"邀请好友"按钮，点击后生成邀请链接（含 referrerId）并复制到剪贴板
- [ ] 被邀请人通过链接注册后，邀请人获得积分（后端 InviteService 加分）
- [ ] 积分页展示邀请记录列表（被邀请人昵称/头像/获得积分/时间）
- [ ] 同一邀请人只能绑定一次（幂等，后端已实现）

---

## 4. 推送通知（替代微信订阅消息）

### 4.1 现状

- **小程序版**：对比分析显示小程序版**未使用**微信订阅消息（`wx.requestSubscribeMessage`），无任何推送能力。
- **Avalonia 版**：同样无推送能力。
- **业务诉求**：作为独立 App，需要推送通知来：
  - 提醒用户签到（每日固定时间）
  - 提醒记录宝宝日常（如喂奶/睡眠间隔提醒）
  - 家庭成员加入通知
  - AI 周报生成完成通知
  - 运营活动通知

### 4.2 目标

- Android 端集成 Firebase Cloud Messaging (FCM) 推送（国际通用）+ 国内厂商推送（华为/小米/OPPO/Vivo，国内必备）。
- iOS 端集成 APNs（Apple Push Notification service）。
- 后端实现统一推送服务，根据用户设备平台选择通道。
- 前端处理推送 token 注册与推送点击事件。

### 4.3 技术方案（分阶段）

### 4.3.1 阶段一：FCM + APNs（国际方案，最小可用）

**适用场景**：海外用户、Google Play 上架、iOS App Store 上架。

#### 后端：统一推送抽象

新建后端项目 `ChildNotes.Backend/ChildNotes.Infrastructure/Push/`：

```csharp
// 推送服务接口
public interface IPushService
{
    /// <summary>注册设备的推送 token。</summary>
    Task RegisterTokenAsync(string userId, string token, string platform, CancellationToken ct);
    /// <summary>发送推送。</summary>
    Task SendAsync(PushMessage message, CancellationToken ct);
}

public record PushMessage(
    string UserId,
    string Title,
    string Body,
    Dictionary<string, string>? Data = null);
```

**FCM 实现**：用 `FirebaseAdmin` NuGet 包，后端持有 service account JSON。

**APNs 实现**：用 `DotAPNs` 或直接调 Apple HTTP/2 API。

#### 后端：推送 token 注册接口

新建 `PushController.cs`：

```csharp
[Route("api/push")]
public class PushController : AppBaseController
{
    private readonly IPushService _push;
    public PushController(IPushService push) => _push = push;

    [HttpPost("register-token")]
    public async Task RegisterToken([FromBody] RegisterTokenRequest req, CancellationToken ct)
        => await _push.RegisterTokenAsync(_current.UserId, req.Token, req.Platform, ct);
}
```

新增 `device_token` 表：`user_id` / `token` / `platform` / `created_at` / `last_active_at`。

#### 前端：Android FCM 集成

修改 [ChildNotes.Android.csproj](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes.Android/ChildNotes.Android.csproj) 添加 Firebase NuGet 包：

```xml
<PackageReference Include="Xamarin.Firebase.Messaging" Version="124.0.0" />
```

新建 `ChildNotes.Android/Services/FirebasePushService.cs`：

```csharp
[Service(Exported = false)]
[IntentFilter(new[] { "com.google.firebase.MESSAGING_EVENT" })]
public class FirebasePushService : FirebaseMessagingService
{
    public override void OnNewToken(string token)
    {
        // 上报 token 到后端
        var api = ServiceProvider.Instance.PushApiClient;
        _ = api.RegisterTokenAsync(token, "android");
    }

    public override void OnMessageReceived(RemoteMessage message)
    {
        // 显示通知（用 NotificationManager）
        var notification = message.Notification;
        if (notification is not null)
        {
            ShowNotification(notification.Title, notification.Body, message.Data);
        }
    }
}
```

[MainActivity.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes.Android/MainActivity.cs) `OnCreate` 中初始化：

```csharp
FirebaseMessaging.Instance.AutoInitEnabled = true;
// Android 13+ 需要运行时申请 POST_NOTIFICATIONS 权限
if ((int)Build.VERSION.SdkInt >= 33)
{
    RequestPermissions(new[] { Manifest.Permission.PostNotifications }, 1001);
}
```

#### 前端：iOS APNs 集成

修改 [AppDelegate.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes.iOS/AppDelegate.cs)：

```csharp
public override void RegisteredForRemoteNotifications(UIApplication application, NSData deviceToken)
{
    var token = deviceToken.ToString();  // 转 hex 字符串
    _ = ServiceProvider.Instance.PushApiClient.RegisterTokenAsync(token, "ios");
}

public override void DidReceiveRemoteNotification(UIApplication application,
    NSDictionary userInfo, Action<UIBackgroundFetchResult> completionHandler)
{
    // 处理推送
    completionHandler(UIBackgroundFetchResult.NewData);
}
```

#### 前端：跨平台推送接口

新建 `ChildNotes/Services/IPushPlatform.cs`：

```csharp
public interface IPushPlatform
{
    Task<string?> GetTokenAsync();
    event Action<string, string?, Dictionary<string, string>?>? NotificationReceived;
    event Action<Dictionary<string, string>>? NotificationTapped;
}
```

各平台实现注入到 `ServiceProvider`。

#### 前端：推送点击处理

推送点击事件需要跳转到对应页面（如"家庭成员加入"通知 → 跳家庭页）。在 [App.axaml.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/App.axaml.cs) 注册：

```csharp
protected override void OnFrameworkInitializationCompleted()
{
    // ... 现有代码 ...
    ServiceProvider.Instance.PushPlatform.NotificationTapped += OnNotificationTapped;
}

private void OnNotificationTapped(Dictionary<string, string> data)
{
    if (data.TryGetValue("type", out var type))
    {
        Dispatcher.UIThread.Post(() =>
        {
            switch (type)
            {
                case "family_joined":
                    _shellVm?.OpenFamily();
                    break;
                case "ai_report":
                    _shellVm?.OpenAiAnalysis();
                    break;
                case "points":
                    _shellVm?.OpenPoints();
                    break;
            }
        });
    }
}
```

### 4.3.2 阶段二：国内厂商推送（国内市场必备）

**适用场景**：国内 Android 市场发布（华为/小米/OPPO/Vivo 应用商店）。

国内 Android 手机 FCM 通道不可用（无 Google Play Services），必须接入各厂商推送 SDK：

| 厂商 | SDK | 推送通道 |
|---|---|---|
| 华为 | HMS Push Kit | 华为推送 |
| 小米 | MiPush SDK | 小米推送 |
| OPPO | HeytapPush | OPPO 推送 |
| Vivo | VPush SDK | Vivo 推送 |
| 其他（魅族/三星等） | FCM（如果有 GMS）或统一兜底 | - |

**统一推送接口**：用 `IPushPlatform` 抽象，运行时根据设备品牌选择 SDK：

```csharp
public class AndroidPushPlatform : IPushPlatform
{
    public async Task<string?> GetTokenAsync()
    {
        var brand = Build.Manufacturer?.ToLowerInvariant();
        return brand switch
        {
            "huawei" or "honor" => await HmsPush.GetTokenAsync(),
            "xiaomi" or "redmi" => await MiPush.GetTokenAsync(),
            "oppo" or "realme" or "oneplus" => await OppoPush.GetTokenAsync(),
            "vivo" => await VivoPush.GetTokenAsync(),
            _ => await FcmPush.GetTokenAsync(),  // 兜底
        };
    }
}
```

**后端多通道下发**：根据 `device_token` 表的 `platform` 字段选择通道：

```csharp
public async Task SendAsync(PushMessage message, CancellationToken ct)
{
    var tokens = await _db.DeviceTokens.Where(t => t.UserId == message.UserId).ToListAsync(ct);
    foreach (var token in tokens)
    {
        switch (token.Platform)
        {
            case "android-fcm": await _fcm.SendAsync(token.Token, message, ct); break;
            case "android-hms": await _hms.SendAsync(token.Token, message, ct); break;
            case "android-mi": await _mi.SendAsync(token.Token, message, ct); break;
            case "android-oppo": await _oppo.SendAsync(token.Token, message, ct); break;
            case "android-vivo": await _vivo.SendAsync(token.Token, message, ct); break;
            case "ios": await _apns.SendAsync(token.Token, message, ct); break;
        }
    }
}
```

### 4.3.3 阶段三：本地通知（无需后端，轻量提醒）

对于"签到提醒""喂奶间隔提醒"等本地触发的通知，无需走后端推送，用本地通知即可：

**Android**：`NotificationManager` 直接发通知。
**iOS**：`UNUserNotificationCenter` 发本地通知。
**跨平台抽象**：

```csharp
public interface ILocalNotification
{
    Task ScheduleAsync(string id, string title, string body, DateTime fireAt, Dictionary<string, string>? data = null);
    Task CancelAsync(string id);
    Task CancelAllAsync();
}
```

**业务场景**：
- 用户在"我的"页开启"每日签到提醒"，设置时间 → 每天该时间发本地通知
- 喂奶/吸奶记录后，3 小时未记录 → 提醒
- 睡眠记录开始后 4 小时未结束 → 提醒

### 4.4 涉及文件

| 操作 | 文件 |
|---|---|
| **后端** | |
| 新建 | `ChildNotes.Backend/ChildNotes.Core/Services/IPushService.cs` |
| 新建 | `ChildNotes.Backend/ChildNotes.Infrastructure/Push/FcmPushService.cs` |
| 新建 | `ChildNotes.Backend/ChildNotes.Infrastructure/Push/ApnsPushService.cs` |
| 新建 | `ChildNotes.Backend/ChildNotes.Infrastructure/Push/DeviceTokenRepository.cs` |
| 新建 | `ChildNotes.Backend/ChildNotes.Api/Controllers/PushController.cs` |
| 修改 | `ChildNotes.Backend/ChildNotes.Infrastructure/Data/ChildNotesDbContext.cs`（加 DeviceToken DbSet） |
| 新建 | 数据库迁移（加 device_tokens 表） |
| **前端共享层** | |
| 新建 | `ChildNotes/ChildNotes/Services/IPushPlatform.cs` |
| 新建 | `ChildNotes/ChildNotes/Services/ILocalNotification.cs` |
| 新建 | `ChildNotes/ChildNotes/Services/PushApiClient.cs` |
| **Android** | |
| 修改 | `ChildNotes.Android/ChildNotes.Android.csproj`（加 Firebase NuGet） |
| 新建 | `ChildNotes.Android/Services/FirebasePushService.cs` |
| 新建 | `ChildNotes.Android/Services/AndroidPushPlatform.cs` |
| 新建 | `ChildNotes.Android/Services/AndroidLocalNotification.cs` |
| 修改 | `ChildNotes.Android/MainActivity.cs`（权限申请 + 初始化） |
| 修改 | `ChildNotes.Android/Properties/AndroidManifest.xml`（权限） |
| 新建 | `ChildNotes.Android/Resources/values/google-services.json`（Firebase 配置） |
| **iOS** | |
| 修改 | `ChildNotes.iOS/AppDelegate.cs`（APNs 注册） |
| 修改 | `ChildNotes.iOS/Info.plist`（UIBackgroundModes: remote-notification） |
| 新建 | `ChildNotes.iOS/Services/IosPushPlatform.cs` |
| 新建 | `ChildNotes.iOS/Services/IosLocalNotification.cs` |
| **前端 UI** | |
| 修改 | `ChildNotes/ChildNotes/App.axaml.cs`（NotificationTapped 处理） |
| 修改 | `ChildNotes/ChildNotes/ViewModels/MineViewModel.cs`（通知设置入口） |
| 新建 | `ChildNotes/ChildNotes/ViewModels/NotificationSettingsViewModel.cs` |
| 新建 | `ChildNotes/ChildNotes/Views/NotificationSettingsView.axaml` |

### 4.5 工作量评估

| 阶段 | 工作量 | 说明 |
|---|---|---|
| 阶段一：FCM + APNs | **3-4 人周** | 后端推送抽象 + FCM 集成 + APNs 集成 + 前端 token 上报 + 推送点击跳转 |
| 阶段二：国内厂商推送 | **4-5 人周** | 华为/小米/OPPO/Vivo 四套 SDK 各 1 人周，后端多通道 1 人周 |
| 阶段三：本地通知 | **1-2 人周** | 跨平台抽象 + Android/iOS 实现 + 业务场景（签到/喂奶/睡眠提醒） |

**总计**：8-11 人周（建议分阶段实施，先做阶段一验证流程）

### 4.6 风险

1. **Firebase 配置**：需要 Google 账号 + Firebase 项目，国内网络访问 FCM 服务不稳定（需翻墙）。国内市场建议直接做阶段二。
2. **iOS 推送证书**：需要 Apple Developer 账号 + APNs 证书（开发/生产两套），证书每年续期。
3. **国内厂商 SDK 集成成本高**：四套 SDK 各有申请流程（厂商开发者账号 + 应用审核），审核周期 3-7 天。
4. **通知权限**：Android 13+ 需运行时申请 `POST_NOTIFICATIONS`，用户可能拒绝。需设计降级方案（如应用内消息）。
5. **后台保活**：Android 厂商对后台进程严格限制，推送通道由厂商系统服务托管（无需 App 保活），但需确保 SDK 初始化在 Application.OnCreate 中完成。

### 4.7 替代方案：应用内消息（轻量）

如果推送通知成本过高，可先做**应用内消息**作为过渡：

- 后端存储消息，App 启动时拉取
- 用 [DialogHost](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/Controls/DialogHost.axaml) 展示
- 缺点：用户不打开 App 就收不到

### 4.8 验收标准

#### 阶段一
- [ ] Android 设备能收到 FCM 推送（前台/后台/杀进程三种状态）
- [ ] iOS 设备能收到 APNs 推送
- [ ] 推送点击能跳转到对应页面
- [ ] 后端能按用户 ID 发送推送

#### 阶段二
- [ ] 华为/小米/OPPO/Vivo 设备分别能收到对应厂商通道推送
- [ ] 后端能根据设备品牌选择通道下发

#### 阶段三
- [ ] 用户可开启"每日签到提醒"，到时间收到本地通知
- [ ] 喂奶/睡眠超时提醒按配置触发

---

## 5. 工作量与里程碑

### 5.1 总工作量

| 功能 | 工作量 | 优先级 |
|---|---|---|
| 隐私协议弹窗 | 0.5 人周 | P1-4 |
| 家庭邀请 Deep Link | 2-3 人周 | P1-1 |
| 邀请有礼闭环 | 1 人周 | P1-2 |
| 推送通知（阶段一） | 3-4 人周 | 路线图 |
| 推送通知（阶段二） | 4-5 人周 | 路线图 |
| 推送通知（阶段三） | 1-2 人周 | 路线图 |
| **合计（P1 三项）** | **3.5-4.5 人周** | |

### 5.2 里程碑建议

| 里程碑 | 内容 | 周期 |
|---|---|---|
| M1 | 隐私协议弹窗上线 | 第 1 周 |
| M2 | Deep Link 基础设施 + 家庭邀请闭环 | 第 2-3 周 |
| M3 | 邀请有礼闭环 | 第 4 周 |
| M4 | 推送通知阶段一（FCM + APNs） | 第 5-6 周 |
| M5 | 推送通知阶段二（国内厂商） | 第 7-8 周 |
| M6 | 推送通知阶段三（本地通知） | 第 9 周 |

### 5.3 依赖关系

```
隐私协议（独立） ──────────────> M1
                                 
Deep Link 基础设施 ──> 家庭邀请 ──> M2
                   └─> 邀请有礼 ──> M3
                                 
推送通知阶段一 ──> 阶段二 ──> 阶段三 ──> M4-M6
```

- 隐私协议独立，可立即开始
- 邀请有礼**强依赖** Deep Link 基础设施（复用 URL 协议与平台配置）
- 推送通知三个阶段**串行**，阶段一必须先完成

---

## 6. 风险与依赖

### 6.1 共同风险

| 风险 | 影响 | 缓解措施 |
|---|---|---|
| 国内 Android 厂商碎片化 | Deep Link 与推送在各厂商行为不一 | 真机测试矩阵：华为/小米/OPPO/Vivo 各 1 台 |
| iOS 开发者账号 | Universal Links 和 APNs 都需要 | 当前 iOS 非发布平台，可暂缓 iOS 实现 |
| 域名与 HTTPS | App Links / Universal Links 强制要求 | 提前申请域名 + 配置 HTTPS 证书 |
| 国内网络访问 FCM | 推送阶段一在国内不可用 | 阶段二国内厂商推送为必做项 |

### 6.2 外部依赖

- **域名**：需要一个 HTTPS 域名（如 `childnotes.app`）部署 Deep Link 验证文件和后端 API
- **Firebase 项目**：推送阶段一需要 Google 账号 + Firebase 项目配置
- **Apple Developer 账号**：iOS Universal Links 和 APNs 需要（$99/年）
- **各厂商开发者账号**：推送阶段二需要华为/小米/OPPO/Vivo 开发者账号（免费但审核周期长）

### 6.3 后端配合

- **隐私协议**：无后端改动
- **Deep Link**：后端仅需部署 2 个静态验证文件（apple-app-site-association + assetlinks.json）
- **邀请有礼**：后端需修改 `RegisterRequest` 和 `AuthService.RegisterAsync`（改动小）
- **推送通知**：后端需新建推送服务模块（改动大）

---

## 附录：关键技术决策

### A.1 为什么用 HTTPS 域名而非自定义 scheme？

| 维度 | HTTPS 域名（App Links / Universal Links） | 自定义 scheme（childnotes://） |
|---|---|---|
| Android 支持 | App Links（API 23+） | IntentFilter（全版本） |
| iOS 支持 | Universal Links（iOS 9+） | URL Scheme（全版本） |
| 安全性 | 域名所有权验证，防劫持 | 任何 App 可声明同 scheme，冲突风险 |
| UX | 直接打开 App，无确认框 | 浏览器弹"打开 App"确认框 |
| 未安装处理 | 打开网页（可引导下载） | 无反应（需自行处理） |
| 配置复杂度 | 需域名 + 验证文件 | 仅 manifest 声明 |

**决策**：主路径用 HTTPS 域名，自定义 scheme 作为兜底（部分场景如微信内分享必须用 scheme）。

### A.2 为什么推送通知分三阶段？

- **阶段一（FCM + APNs）**：覆盖海外用户 + iOS 用户，验证推送流程闭环，工作量可控
- **阶段二（国内厂商）**：覆盖国内 Android 用户，必须做但工作量大，可延后
- **阶段三（本地通知）**：不依赖后端，轻量提醒场景，可独立做

### A.3 为什么不用第三方推送聚合服务（如极光/友盟）？

| 方案 | 优势 | 劣势 |
|---|---|---|
| 自建推送（本方案） | 数据自主可控，无第三方依赖 | 工作量大，需对接多个 SDK |
| 极光推送 | 一套 SDK 覆盖所有厂商，集成简单 | 数据共享给第三方，免费版有量级限制 |
| 友盟推送 | 同极光，阿里生态 | 同极光 |

**决策**：项目当前处于 0.x 阶段，用户量小，建议先用极光推送等聚合服务快速验证，用户量上来后再考虑自建。本方案以自建为目标设计，但阶段二可替换为极光 SDK。

---

**方案结束**。如需进入实施阶段，建议从 M1（隐私协议）开始，因其独立且工作量小，可快速验证流程。
