using System.Linq.Expressions;
using System.Reflection;

namespace Quill.VariableXaml.Metadata;

/// <summary>
/// Turns event attributes written in markup (<c>Click="Increment"</c>) into real handlers that
/// forward to a single dispatcher with the handler name. Delegates are cached per
/// (handler type, name) so unchanged attributes never re-subscribe.
/// </summary>
internal sealed class MarkupEventBinder
{
    private readonly Action<string, object?, object?> _dispatch;
    private readonly Dictionary<(Type HandlerType, string Name), Delegate> _handlers = [];

    public MarkupEventBinder(Action<string, object?, object?> dispatch) => _dispatch = dispatch;

    public Delegate GetHandler(Type handlerType, string handlerName)
    {
        if (_handlers.TryGetValue((handlerType, handlerName), out Delegate? handler))
        {
            return handler;
        }

        MethodInfo invoke = handlerType.GetMethod("Invoke") ?? throw new ArgumentException($"{handlerType} is not a delegate type.");
        if (invoke.ReturnType != typeof(void))
        {
            throw new NotSupportedException($"Event handler type {handlerType.Name} returns a value.");
        }

        ParameterExpression[] parameters = invoke.GetParameters().Select(p => Expression.Parameter(p.ParameterType, p.Name)).ToArray();
        Expression sender = parameters.Length > 0 ? Expression.Convert(parameters[0], typeof(object)) : Expression.Constant(null);
        Expression args = parameters.Length > 1 ? Expression.Convert(parameters[1], typeof(object)) : Expression.Constant(null);
        Expression body = Expression.Invoke(Expression.Constant(_dispatch), Expression.Constant(handlerName), sender, args);
        handler = Expression.Lambda(handlerType, body, parameters).Compile();
        _handlers[(handlerType, handlerName)] = handler;
        return handler;
    }
}
