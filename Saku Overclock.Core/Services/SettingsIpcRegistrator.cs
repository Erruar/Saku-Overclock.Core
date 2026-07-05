using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Saku_Overclock.Shared.Ipc;

namespace Saku_Overclock.Core.Services;

public static class SettingsIpcRegistrator
{
    public static void RegisterSimpleSettings<T>(
        IpcHub hub,
        string entityName,
        Func<T> getter,
        Action<T> setterAndSave,
        JsonTypeInfo<T> typeInfo)
    {
        hub.RegisterHandler($"Get_{entityName}", (cmd, _) =>
        {
            var payload = JsonSerializer.Serialize(getter(), typeInfo);
            return Task.FromResult(new IpcMessage { Kind = IpcMessageKind.Response, Id = cmd.Id, Payload = payload });
        });

        hub.RegisterHandler($"Set_{entityName}", async (cmd, ct) =>
        {
            var incoming = JsonSerializer.Deserialize(cmd.Payload, typeInfo);
            if (incoming is null)
                return new IpcMessage { Kind = IpcMessageKind.Response, Id = cmd.Id, IsSuccess = false, Error = "Bad payload" };

            setterAndSave(incoming);
            await hub.BroadcastEventAsync($"{entityName}Changed", cmd.Payload, ct);
            return new IpcMessage { Kind = IpcMessageKind.Response, Id = cmd.Id, Payload = cmd.Payload };
        });
    }
}