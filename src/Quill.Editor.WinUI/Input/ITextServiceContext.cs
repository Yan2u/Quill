using Windows.UI.Text.Core;

namespace Quill.Editor.WinUI.Input;

/// <summary>
/// The notifications the text input controller sends to the text service: CoreTextEditContext in
/// the app, a scripted fake in tests. The service's requests come back through the controller's
/// <c>On*</c> handlers, possibly re-entrantly from inside these calls (07, rule 3).
/// </summary>
public interface ITextServiceContext
{
    void NotifyFocusEnter();

    void NotifyFocusLeave();

    void NotifyTextChanged(CoreTextRange modifiedRange, int newLength, CoreTextRange newSelection);

    void NotifySelectionChanged(CoreTextRange selection);

    void NotifyLayoutChanged();

    void Detach();
}
