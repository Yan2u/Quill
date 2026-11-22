using Windows.UI.Text.Core;

namespace Quill.Playground.ImePrototype;

/// <summary>
/// The notifications the controller sends to the text service: <see cref="CoreTextEditContext"/>
/// in the app, a scripted fake in the probes. The service's requests come back through the
/// controller's <c>On*</c> handlers, possibly re-entrantly from inside these calls (07, rule 3).
/// </summary>
internal interface ITextServiceContext
{
    void NotifyFocusEnter();

    void NotifyFocusLeave();

    void NotifyTextChanged(CoreTextRange modifiedRange, int newLength, CoreTextRange newSelection);

    void NotifySelectionChanged(CoreTextRange selection);

    void NotifyLayoutChanged();

    void Detach();
}
