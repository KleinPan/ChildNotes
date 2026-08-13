using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace ChildNotes.Styles;

public class DesignSystemStyles : global::Avalonia.Styling.Styles
{
    public DesignSystemStyles()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
