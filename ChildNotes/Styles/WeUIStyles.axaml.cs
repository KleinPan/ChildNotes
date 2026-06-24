using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace ChildNotes.Styles;

public class WeUIStyles : global::Avalonia.Styling.Styles
{
    public WeUIStyles()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
