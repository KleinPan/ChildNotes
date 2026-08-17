using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Provider;
using Android.Util;
using JavaIO = Java.IO;
using AndroidUri = Android.Net.Uri;

namespace ChildNotes.Android.Services;

/// <summary>
/// Android 图片选择器实现：调起系统 Photo Picker。
///
/// API 33+（Android 13+）：使用 MediaStore.ActionPickImages 调起原生相册网格界面，
///                          支持 Android 13+ 的 Photo Picker（无需任何运行时权限）。
/// API &lt;33：回退到 Intent.ActionOpenDocument + image/* 类型，使用系统 Documents UI
///          （Avalonia StorageProvider 在 Android 上也是走这条路径，行为一致）。
///
/// 两种路径都通过 StartActivityForResult 启动，在 MainActivity.OnActivityResult 中回调本类。
/// 用 TaskCompletionSource&lt;List&lt;Uri&gt;&gt; 桥接回调到异步方法，调用方 await 即可。
///
/// 设计说明：
/// - 不依赖 AndroidX Activity 1.7.0 的 PickVisualMedia Contract，避免引入新 NuGet 包的版本兼容风险。
/// - 返回的 Uri 是 content:// 协议，必须通过 ContentResolver.OpenInputStream 读取后复制到 App 私有目录，
///   转为真实文件路径返回（与 IPhotoPicker 接口契约一致：返回本地绝对路径）。
/// - 复制到 CacheDir/ChildNotes/picked/ 临时目录，调用方（UploadService）会进一步压缩到 images/ 目录，
///   本临时文件用完即弃（可后续清理，但当前不做主动清理避免引入复杂度）。
/// </summary>
public sealed class AndroidPhotoPicker : ChildNotes.Services.PhotoPicker.IPhotoPicker
{
    private const string Tag = "ChildNotes";
    private const int RequestCodeSingle = 9001;
    private const int RequestCodeMulti = 9002;

    private readonly MainActivity _activity;
    private TaskCompletionSource<List<AndroidUri>>? _tcs;

    public AndroidPhotoPicker(MainActivity activity)
    {
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
    }

    public Task<string?> PickImageAsync()
    {
        return PickImagesAsync(1).ContinueWith(t => t.Result.Count > 0 ? t.Result[0] : null);
    }

    public async Task<List<string>> PickImagesAsync(int maxCount)
    {
        if (maxCount <= 0) return new List<string>();
        if (_tcs is not null)
        {
            // 上一次选择尚未结束（理论上不会发生，UI 层会阻止重复点击）
            Log.Warn(Tag, "[PhotoPicker] 已有未完成的选择请求，忽略新请求");
            return new List<string>();
        }

        var intent = BuildPickImagesIntent(maxCount);
        var requestCode = maxCount > 1 ? RequestCodeMulti : RequestCodeSingle;
        _tcs = new TaskCompletionSource<List<AndroidUri>>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            _activity.StartActivityForResult(intent, requestCode);
        }
        catch (Exception ex)
        {
            Log.Error(Tag, $"[PhotoPicker] StartActivityForResult 失败: {ex.Message}");
            _tcs.TrySetResult(new List<AndroidUri>());
            _tcs = null;
            return new List<string>();
        }

        var uris = await _tcs.Task;
        _tcs = null;

        // 把 content:// URI 复制到本地文件，返回路径列表
        var paths = new List<string>(uris.Count);
        foreach (var uri in uris)
        {
            var path = CopyUriToLocalFile(uri);
            if (path is not null) paths.Add(path);
        }
        return paths;
    }

    /// <summary>
    /// 由 MainActivity.OnActivityResult 调用，把选择结果转发到 TaskCompletionSource。
    /// </summary>
    internal void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        // ★ 无条件 logcat 输出：确认本方法是否被调用（排查 deliverResultsIfNeeded NPE 是否阻止回调）
        Log.Info(Tag, $"[PhotoPicker] OnActivityResult: requestCode={requestCode}, resultCode={resultCode}, data={(data is null ? "null" : "not null")}");
        if (_tcs is null)
        {
            Log.Warn(Tag, "[PhotoPicker] OnActivityResult: _tcs is null（无待处理请求）");
            return;
        }
        if (requestCode != RequestCodeSingle && requestCode != RequestCodeMulti)
        {
            Log.Warn(Tag, $"[PhotoPicker] OnActivityResult: requestCode={requestCode} 不匹配，忽略");
            return;
        }

        var uris = new List<AndroidUri>();
        if (resultCode == Result.Ok && data is not null)
        {
            // 多选：ClipData 含多个 Uri
            if (data.ClipData is { } clipData)
            {
                for (int i = 0; i < clipData.ItemCount; i++)
                {
                    var item = clipData.GetItemAt(i);
                    if (item?.Uri is { } uri) uris.Add(uri);
                }
                Log.Info(Tag, $"[PhotoPicker] ClipData 多选: count={uris.Count}");
            }
            // 单选：Data 是单个 Uri
            else if (data.Data is { } singleUri)
            {
                uris.Add(singleUri);
                Log.Info(Tag, $"[PhotoPicker] Data 单选: uri={singleUri}");
            }
            else
            {
                Log.Warn(Tag, "[PhotoPicker] resultCode=Ok 但 ClipData 和 Data 均为 null");
            }
        }
        else
        {
            Log.Warn(Tag, $"[PhotoPicker] 非 OK 结果或 data 为 null: resultCode={resultCode}, data={(data is null ? "null" : "not null")}");
        }

        _tcs.TrySetResult(uris);
    }

    /// <summary>
    /// 构建 Photo Picker Intent：
    /// - Android 13+（API 33+）：MediaStore.ActionPickImages 原生相册
    /// - Android 7-12（API 24-32）：Intent.ActionOpenDocument + image/* 回退
    /// </summary>
    private static Intent BuildPickImagesIntent(int maxCount)
    {
        Intent intent;
        if ((int)Build.VERSION.SdkInt >= 33)
        {
            // Android 13+ 原生 Photo Picker
            intent = new Intent(MediaStore.ActionPickImages);
            intent.SetType("image/*");
            if (maxCount > 1)
            {
                // 多选上限：Android 13 Photo Picker 最多支持 MediaStore.GetPickImagesMaxLimit()
                // 该 API 在 .NET Android 绑定中不可用，用硬编码上限 100（Android 规范值）
                const int platformMax = 100;
                var actual = Math.Min(maxCount, platformMax);
                intent.PutExtra(MediaStore.ExtraPickImagesMax, actual);
            }
        }
        else
        {
            // Android 7-12：SAF Documents UI（与 Avalonia StorageProvider 行为一致）
            intent = new Intent(Intent.ActionOpenDocument);
            intent.SetType("image/*");
            intent.AddCategory(Intent.CategoryOpenable);
            if (maxCount > 1)
            {
                // ExtraAllowMultiple 在 API 18+ 可用，但并非所有设备/ROM 都尊重
                intent.PutExtra(Intent.ExtraAllowMultiple, true);
            }
        }
        return intent;
    }

    /// <summary>
    /// 通过 ContentResolver 读取 content:// URI 的流，复制到 CacheDir/ChildNotes/picked/ 目录，
    /// 返回本地绝对路径。失败返回 null（不抛异常，调用方按"未选"处理）。
    ///
    /// HEIC/HEIF 特殊处理：
    /// Avalonia 的 Bitmap 不支持 HEIC 解码，Android 9.0+（API 28+）原生 BitmapFactory 支持。
    /// 检测到 HEIC 时，用 BitmapFactory 解码后压缩为 JPEG 再返回，避免上游解码失败。
    /// </summary>
    private string? CopyUriToLocalFile(AndroidUri uri)
    {
        try
        {
            var dir = new Java.IO.File(_activity.CacheDir, "ChildNotes/picked");
            if (!dir.Exists()) dir.Mkdirs();

            var mime = GetMimeType(uri);
            var isHeic = mime is "image/heic" or "image/heif";
            // HEIC 需要转码为 JPEG，扩展名用 .jpg；其他格式按 mime 取扩展名
            var ext = isHeic ? ".jpg" : GuessExtension(uri);
            var fileName = $"picked_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}{ext}";
            var target = new Java.IO.File(dir, fileName);

            if (isHeic)
            {
                // HEIC → JPEG 转码：用 Android 原生 BitmapFactory（API 28+ 支持 HEIC）
                return DecodeHeicToJpeg(uri, target);
            }

            using var input = _activity.ContentResolver?.OpenInputStream(uri);
            if (input is null)
            {
                Log.Warn(Tag, "[PhotoPicker] ContentResolver.OpenInputStream 返回 null");
                return null;
            }
            // input 是 System.IO.Stream，output 是 Java.IO.FileOutputStream
            // 不能用 CopyTo（类型不匹配），手动用缓冲区复制
            using var output = new JavaIO.FileOutputStream(target);
            var buffer = new byte[8192];
            int read;
            long total = 0;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, read);
                total += read;
            }
            output.Flush();
            Log.Info(Tag, $"[PhotoPicker] 复制成功: uri={uri}, path={target.AbsolutePath}, size={total}");
            return target.AbsolutePath;
        }
        catch (Exception ex)
        {
            Log.Error(Tag, $"[PhotoPicker] 复制 URI 到本地文件失败: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// HEIC/HEIF → JPEG 转码：用 Android 原生 BitmapFactory 解码 HEIC（API 28+ 原生支持），
    /// 再以 JPEG 压缩写入目标文件。失败返回 null。
    /// </summary>
    private string? DecodeHeicToJpeg(AndroidUri uri, Java.IO.File target)
    {
        try
        {
            using var input = _activity.ContentResolver?.OpenInputStream(uri);
            if (input is null)
            {
                Log.Warn(Tag, "[PhotoPicker] HEIC 转码：OpenInputStream 返回 null");
                return null;
            }
            // BitmapFactory.DecodeStream 在 API 28+ 原生支持 HEIC 解码
            var androidBmp = BitmapFactory.DecodeStream(input);
            if (androidBmp is null)
            {
                Log.Error(Tag, "[PhotoPicker] HEIC 转码：BitmapFactory.DecodeStream 返回 null");
                return null;
            }
            using (androidBmp)
            using (var output = new JavaIO.FileOutputStream(target))
            {
                // 以质量 90 压缩为 JPEG（后续 UploadService 还会再次压缩到目标尺寸，这里保留较高质量）
                var ok = androidBmp.Compress(Bitmap.CompressFormat.Jpeg, 90, output);
                if (!ok)
                {
                    Log.Error(Tag, "[PhotoPicker] HEIC 转码：Bitmap.Compress 返回 false");
                    return null;
                }
                output.Flush();
            }
            var size = target.Length();
            Log.Info(Tag, $"[PhotoPicker] HEIC→JPEG 转码成功: uri={uri}, path={target.AbsolutePath}, size={size}");
            return target.AbsolutePath;
        }
        catch (Exception ex)
        {
            Log.Error(Tag, $"[PhotoPicker] HEIC 转码失败: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>获取 URI 的 MIME 类型，失败返回 null。</summary>
    private string? GetMimeType(AndroidUri uri)
    {
        try
        {
            return _activity.ContentResolver?.GetType(uri);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 从 URI 推断图片扩展名（HEIC 已在上层提前转码，此处只处理非 HEIC 格式）。
    /// </summary>
    private string GuessExtension(AndroidUri uri)
    {
        var mime = GetMimeType(uri);
        return mime switch
        {
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            _ => ".jpg",
        };
    }
}
