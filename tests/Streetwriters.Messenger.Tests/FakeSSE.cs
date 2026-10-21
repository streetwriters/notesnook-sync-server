using System.Reflection;
using System.Security.Claims;
using Lib.AspNetCore.ServerSentEvents;

namespace Streetwriters.Messenger.Tests;

public class FakeClient(string userId, string tokenId = "token") : IServerSentEventsClient
{
    public List<string> Received { get; } = [];
    // when set, sends wait for it (or for cancellation) before completing
    public TaskCompletionSource? Gate { get; set; }

    public Guid Id { get; } = Guid.NewGuid();
    public ClaimsPrincipal User { get; } = new(new ClaimsIdentity([new Claim("sub", userId), new Claim("jti", tokenId)]));
    public bool IsConnected { get; set; } = true;

    public Task SendEventAsync(string text) => SendEventAsync(text, CancellationToken.None);

    public async Task SendEventAsync(string text, CancellationToken cancellationToken)
    {
        if (Gate != null) await Gate.Task.WaitAsync(cancellationToken);
        lock (Received) Received.Add(text);
    }

    public Task SendEventAsync(ServerSentEvent serverSentEvent) => throw new NotImplementedException();
    public Task SendEventAsync(ServerSentEvent serverSentEvent, CancellationToken cancellationToken) => throw new NotImplementedException();
    public void Disconnect() => throw new NotImplementedException();
    public Task DisconnectAsync() => throw new NotImplementedException();
    public T GetProperty<T>(string name) => throw new NotImplementedException();
    public T RemoveProperty<T>(string name) => throw new NotImplementedException();
    public bool SetProperty(string name, object value, bool overwrite = true) => throw new NotImplementedException();
}

// Only GetClients is needed by SSEHelper; everything else throws.
public class FakeService : DispatchProxy
{
    public List<FakeClient> Clients { get; } = [];

    public static (IServerSentEventsService, FakeService) Create()
    {
        var service = Create<IServerSentEventsService, FakeService>();
        return (service, (FakeService)(object)service);
    }

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method?.Name != nameof(IServerSentEventsService.GetClients)) throw new NotImplementedException(method?.Name);
        if (args is null || args.Length == 0) return Clients.Cast<IServerSentEventsClient>().ToList();
        var group = (string)args[0]!;
        return Clients.Where(c => c.User.FindFirstValue("sub") == group).Cast<IServerSentEventsClient>().ToList();
    }
}
