using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ChildNotes.Styles;

public class WeUIColors : ResourceDictionary
{
    public WeUIColors()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
