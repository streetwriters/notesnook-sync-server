using System.Reflection;

namespace Notesnook.API.Tests;

// a fake for interfaces: methods without a handler throw NotImplementedException
public class Stub<T> : DispatchProxy where T : class
{
    private Dictionary<string, Func<object?[], object?>> handlers = [];

    public static T Create(params (string Method, Func<object?[], object?> Handler)[] handlers)
    {
        var proxy = Create<T, Stub<T>>();
        ((Stub<T>)(object)proxy).handlers = handlers.ToDictionary((h) => h.Method, (h) => h.Handler);
        return proxy;
    }

    protected override object? Invoke(MethodInfo? method, object?[]? args)
        => handlers.TryGetValue(method!.Name, out var handler) ? handler(args ?? []) : throw new NotImplementedException(method.Name);
}
