using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;

namespace Quill.Shell;

/// <summary>
/// Elements whose brushes are theme resources. A brush looked up in code is the one of the theme at that
/// moment; markup keeps the <c>{ThemeResource}</c> reference, so the element follows theme changes.
/// </summary>
public static class Themed
{
    private const string Namespaces = "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'";

    /// <summary>Loads one element of markup; <paramref name="tag"/> with <paramref name="attributes"/>.</summary>
    public static T Load<T>(string tag, string attributes)
        where T : DependencyObject => (T)XamlReader.Load($"<{tag} {Namespaces} {attributes} />");
}
