using System.Text.Json;
using Microsoft.Extensions.Logging;
using Saku_Overclock.Core.Contracts;
using Saku_Overclock.Shared;
using Saku_Overclock.Shared.Ipc;

namespace Saku_Overclock.Core.Services;

public class CoreIpcHandlers(
    ICpuService cpu,
    IPstateService powerState,
    IApplyerService applyer,
    IOcFinderService ocFinder,
    IBackgroundDataUpdater dataUpdater,
    ILogger<CoreIpcHandlers> logger,
    IpcHub hub)
{
    public void RegisterIpcHandlers()
    {
        // ---- статичный аппаратный снапшот — один запрос на весь сеанс клиента ----
        hub.RegisterHandler("Get_HardwareInfo", (cmd, _) =>
        {
            var snapshot = new HardwareInfoSnapshot
            {
                IsAvailable = cpu.IsAvailable,
                PhysicalCores = cpu.PhysicalCores,
                CoreDisableMap = cpu.CoreDisableMap,
                Cores = cpu.Cores,
                CpuName = cpu.CpuName,
                Smt = cpu.Smt,
                MotherBoardInfo = cpu.MotherBoardInfo,
                Avx512AvailableByCodename = cpu.Avx512AvailableByCodename,
                CpuCodeName = cpu.CpuCodeName,
                SmuVersion = cpu.SmuVersion,
                PowerTableVersion = cpu.PowerTableVersion,
                CodenameGeneration = cpu.GetCodenameGeneration(),
                MemoryConfig = cpu.GetMemoryConfig(),
                PstateSupported = powerState.IsSupported
            };
            var payload = JsonSerializer.Serialize(snapshot, IpcJsonContext.Default.HardwareInfoSnapshot);
            return Task.FromResult(Ok(cmd.Id, payload));
        });
// TODO: Fix Cpu debug
        /*hub.RegisterHandler("Generate_Debug_Report", (cmd, _) =>
            Ok(cmd.Id, cpu.GenerateDebugReport()));*/

        // ---- вызывается по требованию ----
        hub.RegisterHandler("Get_Pstates", (cmd, _) =>
        {
            try
            {
                var results = powerState.ReadAllPstates();
                var payload = JsonSerializer.Serialize(results.ToList(), IpcJsonContext.Default.ListPstateOperationResult);
                return Task.FromResult(Ok(cmd.Id, payload));
            }
            catch (Exception exception)
            {
                return Task.FromException<IpcMessage>(exception);
            }
        });

        hub.RegisterHandler("Get_Preset_Recommendations", (cmd, _) =>
        {
            try
            {
                var data = ocFinder.GetPerformanceRecommendationData();
                return Task.FromResult(Ok(cmd.Id, JsonSerializer.Serialize(data, IpcJsonContext.Default.PresetRecommendations)));
            }
            catch (Exception exception)
            {
                return Task.FromException<IpcMessage>(exception);
            }
        });

        hub.RegisterHandler("Get_Is_Undervolting_Available", (cmd, _) =>
        {
            try
            {
                return Task.FromResult(Ok(cmd.Id, JsonSerializer.Serialize(ocFinder.IsUndervoltingAvailable(), IpcJsonContext.Default.Boolean)));
            }
            catch (Exception exception)
            {
                return Task.FromException<IpcMessage>(exception);
            }
        });

        hub.RegisterHandler("Get_Cpu_Power", (cmd, _) =>
        {
            try
            {
                return Task.FromResult(Ok(cmd.Id, JsonSerializer.Serialize(ocFinder.GetCpuPower(), IpcJsonContext.Default.Int32)));
            }
            catch (Exception exception)
            {
                return Task.FromException<IpcMessage>(exception);
            }
        });

        hub.RegisterHandler("Get_Is_Battery_Unavailable", (cmd, _) =>
            Task.FromResult(Ok(cmd.Id, JsonSerializer.Serialize(dataUpdater.IsBatteryUnavailable(), IpcJsonContext.Default.Boolean))));

        // ---- применение пресета: команда ждёт завершения + broadcast результата ВСЕМ клиентам ----
        hub.RegisterHandler("Apply_Preset", async (cmd, _) =>
        {
            var req = JsonSerializer.Deserialize(cmd.Payload, IpcJsonContext.Default.ApplyPresetRequest);
            if (req is null) return Fail(cmd.Id, "Bad payload");

            try
            {
                await applyer.ApplyPreset(req.Preset, req.SaveInfo);
                return Ok(cmd.Id, "");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ApplyPreset failed");
                return Fail(cmd.Id, ex.Message);
            }
        });

        hub.RegisterHandler("Apply_SwitchNextPreset", (cmd, _) =>
        {
            try
            {
                var next = applyer.SwitchNextPreset();
                return Task.FromResult(Ok(cmd.Id, JsonSerializer.Serialize(next, IpcJsonContext.Default.PresetId)));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "SwitchNextPreset failed");
                return Task.FromResult(Fail(cmd.Id, ex.Message));
            }
        });

        // OnSettingsApplied подписывается ОДИН раз здесь, при регистрации,
        // и рассылает результат всем подключённым клиентам — неважно, кто вызвал apply
        // (сейчас только Client, но завтра сюда добавится Companion по хоткею)
        applyer.OnSettingsApplied += results =>
        {
            var payload = JsonSerializer.Serialize(results, IpcJsonContext.Default.ListApplyResult);
            _ = hub.BroadcastEventAsync("PresetApplied", payload);
        };
    }

    private static IpcMessage Ok(string id, string payload) =>
        new() { Kind = IpcMessageKind.Response, Id = id, Payload = payload };

    private static IpcMessage Fail(string id, string error) =>
        new() { Kind = IpcMessageKind.Response, Id = id, IsSuccess = false, Error = error };
}