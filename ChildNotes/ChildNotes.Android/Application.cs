﻿using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Avalonia.Media;
using SQLitePCL;

namespace ChildNotes.Android
{
    [Application]
    public class Application : AvaloniaAndroidApplication<App>
    {
        protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
        }

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            // Android 上必须在任何 Microsoft.Data.Sqlite 操作之前初始化原生库 e_sqlite3，
            // 否则打开连接会抛 "Unable to load DLL 'e_sqlite3'"，登录/注册看似无反应。
            Batteries_V2.Init();
            return base.CustomizeAppBuilder(builder)
                .With(new FontManagerOptions
                {
                    DefaultFamilyName = "avares://ChildNotes/Assets/Fonts/wqy-microhei.ttc#WenQuanYi Micro Hei",
                    FontFallbacks = new[]
                    {
                        new FontFallback { FontFamily = new FontFamily("avares://ChildNotes/Assets/Fonts/wqy-microhei.ttc#WenQuanYi Micro Hei") },
                        new FontFallback { FontFamily = new FontFamily("sans-serif") }
                    }
                });
        }
    }
}
