using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using ChildNotes.Infrastructure;
using ChildNotes.ViewModels;
using ChildNotes.Views;

namespace ChildNotes;

[RequiresUnreferencedCode(
    "Default implementation of ViewLocator involves reflection which may be trimmed away.",
    Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
        {
            DevLogger.Log("VL", "Build: param is null");
            return null;
        }
        var typeName = param.GetType().Name;
        DevLogger.Log("VL", $"Build: {typeName}");
        try
        {
            var view = param switch
            {
                HomeViewModel => (Control)new HomeView(),
                FeedingViewModel => new FeedingView(),
                GrowthViewModel => new GrowthView(),
                MineViewModel => new MineView(),
                _ => ResolveByName(param),
            };
            DevLogger.Log("VL", $"Build done: {typeName} -> {view.GetType().Name}");
            return view;
        }
        catch (Exception ex)
        {
            DevLogger.Log("VL", $"Build EXCEPTION for {typeName}");
            DevLogger.Log("VL", ex);
            return new TextBlock { Text = "Build failed: " + ex.Message };
        }
    }

    private static Control ResolveByName(object param)
    {
        var name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        DevLogger.Log("VL", $"ResolveByName: {name}");
        var type = Type.GetType(name);
        if (type != null) return (Control)Activator.CreateInstance(type)!;
        DevLogger.Log("VL", $"ResolveByName: type not found for {name}");
        return new TextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
