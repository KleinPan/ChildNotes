using System;
using System.IO;
using System.Threading.Tasks;
using Android;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.Content;
using AndroidX.Core.App;
using Application = Android.App.Application;

namespace ChildNotes.Android.Services;

/// <summary>
/// Android 日志导出与分享服务。
///
/// 背景：targetSdk=36 强制 Scoped Storage，直接 File.WriteAllTextAsync 写公共目录
/// (/storage/emulated/0/Aiji) 会被系统拒绝。AndroidManifest 也没声明 WRITE_EXTERNAL_STORAGE。
/// 改为写 App 私有目录 + FileProvider 生成 content:// URI + ACTION_SEND 分享弹窗。
///
/// 流程：
/// 1. 写文件到 Context.GetExternalFilesDir(null) → /storage/emulated/0/Android/data/{pkg}/files/
///    （私有目录，无需权限，卸载时随 App 一起清除）
/// 2. 用 FileProvider.GetUriForFile 生成 content:// URI
/// 3. 创建 ACTION_SEND Intent，type=text/plain，EXTRA_STREAM=URI，FLAG_GRANT_READ_URI_PERMISSION
/// 4. Intent.CreateChooser 包装后 StartActivity，弹出系统分享面板
///
/// 返回：展示给用户的相对路径（external-files 目录下文件名），用于 toast 提示。
/// </summary>
public static class AndroidLogShareService
{
    /// <summary>
    /// 将日志内容写入 App 私有目录并弹出系统分享面板。
    /// 返回展示路径（"外部文件目录/{fileName}"），调用方用于 toast 提示。
    /// </summary>
    public static async Task<string> WriteAndShareAsync(string fileName, string content)
    {
        var ctx = Application.Context;
        if (ctx is null) throw new InvalidOperationException("Application.Context is null");

        // 1. 写入 App 私有外部目录（无需权限）
        // GetExternalFilesDir(null) 在 .NET Android 绑定里返回 Java.IO.File?，可能为 null（罕见，无外部存储）
        var extFilesDir = ctx.GetExternalFilesDir(null)
            ?? throw new InvalidOperationException("GetExternalFilesDir(null) returned null");
        var dir = extFilesDir.AbsolutePath; // /storage/emulated/0/Android/data/{pkg}/files/
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        await File.WriteAllTextAsync(path, content);

        // 2. 通过 FileProvider 生成 content:// URI
        // authority 必须与 AndroidManifest 中声明的 ${applicationId}.fileprovider 一致
        var authority = ctx.PackageName + ".fileprovider";
        var javaFile = new Java.IO.File(path);
        var uri = FileProvider.GetUriForFile(ctx, authority, javaFile);

        // 3. 构造 ACTION_SEND Intent 并启动系统分享面板
        var intent = new Intent(Intent.ActionSend);
        intent.SetType("text/plain");
        intent.PutExtra(Intent.ExtraStream, uri);
        // 临时授权：让接收方（如微信/邮件/文件管理器）能读这个 content:// URI
        intent.AddFlags(ActivityFlags.GrantReadUriPermission);

        var chooser = Intent.CreateChooser(intent, "分享日志");
        // NEW_TASK：从非 Activity 上下文 StartActivity 必须加此 flag
        chooser.AddFlags(ActivityFlags.NewTask);

        ctx.StartActivity(chooser);

        // 4. 返回展示路径（不暴露完整路径，只显示目录名+文件名，避免路径泄露 App 私有目录结构）
        return Path.GetFileName(path);
    }
}
