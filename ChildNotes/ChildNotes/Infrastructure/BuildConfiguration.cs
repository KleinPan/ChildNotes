namespace ChildNotes.Infrastructure;

/// <summary>
/// 构建配置信息。由 csproj 属性 <c>DevBuild</c> 在编译时注入到 <c>BuildConfiguration.IsDevelopmentBuild</c>。
/// 用于运行时区分开发版/正式版，控制开发者选项、日志导出、服务器地址配置等功能的可见性。
/// </summary>
public static class BuildConfiguration
{
    /// <summary>
    /// 是否为开发版构建。
    /// - true：保留开发者选项、服务器地址可编辑、日志导出可见，ApplicationId 为 com.babydiary.app.dev
    /// - false：正式版，移除开发者选项入口、服务器地址只读、隐藏日志导出，ApplicationId 为 com.babydiary.app
    /// 该值由 csproj 在编译时通过 DevBuild 属性注入（见 ChildNotes.csproj 的 BuildConfigurationDevBuild 常量）。
    /// </summary>
    public const bool IsDevelopmentBuild =
#if DEV_BUILD
        true
#else
        false
#endif
        ;

    /// <summary>
    /// 日志文件名前缀，用于区分开发版/正式版的日志文件。
    /// 开发版为 "dev-"，正式版为 "app-"。
    /// </summary>
    public static string LogFilePrefix => IsDevelopmentBuild ? "dev-" : "app-";

    /// <summary>构建版本标识（用于日志头部信息等场景）。</summary>
    public static string BuildVariant => IsDevelopmentBuild ? "dev" : "prod";
}
