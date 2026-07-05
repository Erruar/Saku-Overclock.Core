using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Saku_Overclock.Shared;
using Saku_Overclock.Shared.Ipc;

namespace Saku_Overclock.Core.Services;

public sealed class IpcHub(ILogger<IpcHub> logger)
{
    private readonly ConcurrentDictionary<Guid, PipeConnection> _clients = new();

    public delegate Task<IpcMessage> CommandHandler(IpcMessage command, CancellationToken ct);
    private readonly ConcurrentDictionary<string, CommandHandler> _handlers = new();

    public void RegisterHandler(string commandName, CommandHandler handler) =>
        _handlers[commandName] = handler;

    public void AddClient(Guid id, PipeConnection conn) => _clients[id] = conn;
    public void RemoveClient(Guid id) => _clients.TryRemove(id, out _);

    // Пуш события ВСЕМ подключённым клиентам
    public async Task BroadcastEventAsync(string name, string payload, CancellationToken ct = default)
    {
        var msg = new IpcMessage { Kind = IpcMessageKind.Event, Name = name, Payload = payload };
        var json = JsonSerializer.Serialize(msg, IpcJsonContext.Default.IpcMessage);

        foreach (var (_, conn) in _clients)
        {
            try { await conn.WriteLineAsync(json, ct); }
            catch { /* клиент отвалится сам, RemoveClient вызовет его собственный read-loop */ }
        }
    }

    public async Task<IpcMessage> DispatchCommandAsync(IpcMessage command, CancellationToken ct)
    {
        if (_handlers.TryGetValue(command.Name, out var handler))
        {
            try
            {
                return await handler(command, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Handler '{Name}' failed", command.Name);
                return new IpcMessage { Kind = IpcMessageKind.Response, Id = command.Id, IsSuccess = false, Error = ex.Message };
            }
        }

        return new IpcMessage { Kind = IpcMessageKind.Response, Id = command.Id, IsSuccess = false, Error = $"Unknown command: {command.Name}" };
    }
}

public sealed class PipeConnection(NamedPipeServerStream pipe)
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly StreamWriter _writer = new(pipe, leaveOpen: true) { AutoFlush = true };
    public StreamReader Reader { get; } = new(pipe, leaveOpen: true);

    public async Task WriteLineAsync(string line, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try { await _writer.WriteLineAsync(line.AsMemory(), ct); }
        finally { _writeLock.Release(); }
    }
}