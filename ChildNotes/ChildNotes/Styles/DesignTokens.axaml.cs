using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ChildNotes.Styles;

public class DesignTokens : ResourceDictionary
{
    public DesignTokens()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
