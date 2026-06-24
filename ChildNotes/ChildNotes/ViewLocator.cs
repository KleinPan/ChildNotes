using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
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
        if (param is null) return null;

        return param switch
        {
            HomeViewModel => new HomeView(),
            FeedingViewModel => new FeedingView(),
            GrowthViewModel => new GrowthView(),
            MineViewModel => new MineView(),
            _ => ResolveByName(param),
        };
    }

    private static Control ResolveByName(object param)
    {
        var name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);
        if (type != null) return (Control)Activator.CreateInstance(type)!;
        return new TextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
