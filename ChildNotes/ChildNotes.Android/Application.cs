using Android.App;
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

        public override void OnCreate()
        {
            base.OnCreate();

            // Family-centric（阶段 2）：注入 ANDROID_ID 提供者（进程级，早于 Avalonia 启动 /
            // ServiceProvider 构造），DeviceId / LocalDataSpaceId 按设计文档第 4 节 SHA256 派生，
            // 同设备卸载重装后 Id 连续（数据归属不漂移）。失败回退 GUID（上层处理）。
            ChildNotes.Infrastructure.DeviceIdentityProvider.Current =
                new Services.AndroidDeviceIdentityProvider(this);
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
