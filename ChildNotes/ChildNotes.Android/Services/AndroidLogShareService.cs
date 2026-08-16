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

    /// <summary>
    /// 将本地数据库文件复制到 external-files 目录，并通过 FileProvider 弹出系统分享面板。
    /// 由共享层 DatabaseExportService 通过反射调用，避免主项目直接引用 Android 项目。
    ///
    /// 关键点：调用方（共享层）在调用本方法前必须已执行 PRAGMA wal_checkpoint(TRUNCATE)，
    /// 否则源文件可能只有 1KB schema，活跃数据还在 -wal 里。
    ///
    /// 复制源：<paramref name="sourceDbPath"/> = Context.GetFilesDir() + "/ChildNotes/childnotes.db"
    /// 复制目标：external-files 目录 = /storage/emulated/0/Android/data/{pkg}/files/
    /// FileProvider 路径：<external-files-path name="external_files" path="." />（file_paths.xml 已配置）
    ///
    /// 返回展示路径（仅文件名），用于 toast 提示。
    /// </summary>
    public static async Task<string> WriteDbAndShareAsync(string sourceDbPath)
    {
        var ctx = Application.Context;
        if (ctx is null) throw new InvalidOperationException("Application.Context is null");

        if (!File.Exists(sourceDbPath))
            throw new FileNotFoundException("源数据库文件不存在", sourceDbPath);

        // 1. 复制到 App 私有外部目录（无需权限）
        var extFilesDir = ctx.GetExternalFilesDir(null)
            ?? throw new InvalidOperationException("GetExternalFilesDir(null) returned null");
        var dir = extFilesDir.AbsolutePath;
        Directory.CreateDirectory(dir);

        // 时间戳文件名：用户多次导出不会冲突，便于区分
        var fileName = $"childnotes_{DateTime.Now:yyyyMMdd_HHmmss}.db";
        var destPath = Path.Combine(dir, fileName);

        // 异步复制：避免阻塞 UI；File.Copy 内部用流式 IO。
        // overwrite=true：用户可能重名（同一秒），直接覆盖
        await using (var src = new FileStream(sourceDbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        await using (var dst = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await src.CopyToAsync(dst);
        }

        // 2. 通过 FileProvider 生成 content:// URI
        var authority = ctx.PackageName + ".fileprovider";
        var javaFile = new Java.IO.File(destPath);
        var uri = FileProvider.GetUriForFile(ctx, authority, javaFile);

        // 3. 构造 ACTION_SEND Intent 并启动系统分享面板
        // type 用 application/octet-stream：SQLite 是二进制文件，文本类应用（微信/邮件）
        // 也能识别并支持"另存为"操作
        var intent = new Intent(Intent.ActionSend);
        intent.SetType("application/octet-stream");
        intent.PutExtra(Intent.ExtraStream, uri);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission);

        var chooser = Intent.CreateChooser(intent, "分享数据库");
        chooser.AddFlags(ActivityFlags.NewTask);

        ctx.StartActivity(chooser);

        return fileName;
    }

    /// <summary>
    /// 从 external-files 目录读取 childnotes_import.db 文件，返回其本地路径。
    /// 由共享层 DatabaseExportService.ImportAsync 通过反射调用。
    ///
    /// 设计：不用 SAF（需要 Activity 上下文和回调注册，跨平台抽象复杂），
    /// 改用 external-files 目录作为中转：用户先通过文件管理器或 adb push 把备份的
    /// childnotes.db 放到 /storage/emulated/0/Android/data/{pkg}/files/childnotes_import.db，
    /// 然后调用本方法返回该路径，供 ImportAsync 做后续验证和替换。
    ///
    /// external-files 目录无需任何权限，app 自己可读写，用户也可通过系统文件管理器访问。
    /// </summary>
    public static Task<string?> FindImportableDbAsync()
    {
        var ctx = Application.Context;
        if (ctx is null) throw new InvalidOperationException("Application.Context is null");

        var extFilesDir = ctx.GetExternalFilesDir(null)
            ?? throw new InvalidOperationException("GetExternalFilesDir(null) returned null");

        var importPath = Path.Combine(extFilesDir.AbsolutePath, "childnotes_import.db");
        return Task.FromResult<string?>(File.Exists(importPath) ? importPath : null);
    }
}
