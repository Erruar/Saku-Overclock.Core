using Microsoft.Extensions.Logging;
using Saku_Overclock.Core.Contracts;
using Saku_Overclock.Core.Helpers;
using Saku_Overclock.Shared;
using Saku_Overclock.Shared.Models;

namespace Saku_Overclock.Core.Services;
public class Zen4PstateStrategy : IPstateStrategy
{
    private readonly ICpuService _cpuService;
    private readonly ILogger<IPstateService> _logger;

    private const uint MsrPstateBase = 0xC0010064;
    private const uint MsrHwcr = 0xC0010015;
    private const uint HwcrTscFreqSel = 0x200000; // Bit 21

    public bool IsSupportedFamily
    {
        get;
    }

    public Zen4PstateStrategy(
        ICpuService cpuService,
        ILogger<IPstateService> logger)
    {
        _cpuService = cpuService;
        _logger = logger;
        IsSupportedFamily =  _cpuService.Family is > CpuFamily.Family16H and < CpuFamily.Family1Ah;
    }

    public PstateOperationResult ReadPstate(int stateNumber)
    {
        if (stateNumber < 0 || stateNumber > 2)
        {
            return PstateOperationResult.Fail($"Invalid P-State number: {stateNumber}");
        }

        try
        {
            uint eax = 0, edx = 0;
            var msr = MsrPstateBase + (uint)stateNumber;

            if (!_cpuService.ReadMsr(msr, ref eax, ref edx))
            {
                _logger.LogError("Failed to read MSR 0x{msr:X} for P-State {stateNumber}", msr, stateNumber);
                return PstateOperationResult.Fail("MSR read failed");
            }

            var pstate = ParseZen4Pstate(eax, edx, stateNumber);
            return PstateOperationResult.Ok(pstate);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,  "Failed to read P-State for P-State {stateNumber}", stateNumber);
            return PstateOperationResult.Fail(ex.Message);
        }
    }

    public PstateOperationResult WritePstate(PstateWriteParams parameters)
    {
        if (!ValidateParameters(parameters, out var error))
        {
            return PstateOperationResult.Fail(error);
        }

        try
        {
            // Читаем текущее состояние для получения IddValue и IddDiv
            var currentResult = ReadPstate(parameters.StateNumber);
            if (!currentResult.Success)
            {
                return currentResult;
            }

            var current = currentResult.Data;

            // Рассчитываем новые значения
            var fid = CalculateFidFromFrequency(parameters.FrequencyMHz);
            var did = CalculateDidFromFrequency(parameters.FrequencyMHz, fid);
            var vid = CalculateVidFromVoltage(parameters.VoltageMillivolts);

            // Формируем EAX
            var eax = BuildZen4Eax(
                fid: fid,
                did: did,
                vid: vid,
                iddValue: current.IddValue,
                iddDiv: current.IddDiv
            );

            // EDX - старший DWORD, содержит бит Enable (bit 31 в EDX = bit 63 в MSR)
            var edx = parameters.Enable.HasValue && parameters.Enable.Value
                ? 0x80000000u
                : (parameters.Enable.HasValue ? 0u : (current.IsEnabled ? 0x80000000u : 0u));

            // Применяем TSC workaround
            if (!ApplyTscWorkaround())
            {
                _logger.LogError("Failed to apply TSC workaround");
                return PstateOperationResult.Fail("TSC workaround failed");
            }

            // Записываем на все NUMA nodes если нужно
            if (!WritePstateToAllNodes(parameters.StateNumber, eax, edx))
            {
                return PstateOperationResult.Fail("Failed to write P-State to all nodes");
            }

            // Читаем обратно для проверки
            return ReadPstate(parameters.StateNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write P-State for P-State {stateNumber}", parameters.StateNumber);
            return PstateOperationResult.Fail(ex.Message);
        }
    }

    public bool ValidateParameters(PstateWriteParams parameters, out string? error)
    {
        error = null;

        if (parameters.StateNumber is < 0 or > 2)
        {
            error = "P-State number must be 0, 1, or 2";
            return false;
        }

        if (parameters.FrequencyMHz is < 400 or > 6000)
        {
            error = "Frequency must be between 400 and 6000 MHz";
            return false;
        }

        if (parameters.VoltageMillivolts is < 200 or > 1550)
        {
            error = "Voltage must be between 200 and 1550 mV";
            return false;
        }

        return true;
    }

    // ====================================================================
    // Приватные методы для Zen 4
    // ====================================================================

    private static PstateData ParseZen4Pstate(uint eax, uint edx, int stateNumber)
    {
        // Zen 4 структура:
        // EAX[7:0]   = Fid
        // EAX[13:8]  = Did (CpuDfsId)
        // EAX[21:14] = Vid (SVI2)
        // EAX[29:22] = IddValue
        // EAX[31:30] = IddDiv
        // EDX[31]    = PstateEn (bit 63 в 64-битном MSR)

        var fid = eax & 0xFF;
        var did = (eax >> 8) & 0x3F;
        var vid = (eax >> 14) & 0xFF;
        var iddValue = (eax >> 22) & 0xFF;
        var iddDiv = (eax >> 30) & 0x3;
        var isEnabled = (edx & 0x80000000) != 0;

        // Расчёт частоты: Freq = (Fid * 25) / (Did * 12.5) * 100
        var frequencyMHz = did > 0 ? (fid * 25.0 / (did * 12.5)) * 100.0 : 0;

        // Расчёт напряжения: Voltage = 1.55 - (Vid * 0.00625)
        var voltageVolts = 1.55 - (vid * 0.00625);
        var voltageMillivolts = voltageVolts * 1000.0;

        return new PstateData
        {
            StateNumber = stateNumber,
            IsEnabled = isEnabled,
            FrequencyMHz = Math.Round(frequencyMHz, 2),
            VoltageMillivolts = Math.Round(voltageMillivolts, 2),
            Fid = fid,
            Did = did,
            Vid = vid,
            IddValue = iddValue,
            IddDiv = iddDiv
        };
    }

    private static uint BuildZen4Eax(uint fid, uint did, uint vid, uint iddValue, uint iddDiv)
    {
        return ((iddDiv & 0xFF) << 30) |
               ((iddValue & 0xFF) << 22) |
               ((vid & 0xFF) << 14) |
               ((did & 0xFF) << 8) |
               (fid & 0xFF);
    }

    private static uint CalculateFidFromFrequency(double frequencyMHz)
    {
        // Fid = (Freq * Did * 12.5) / 25 / 100
        // Для упрощения используем Did = 8 как базу, потом корректируем
        // Обычно Fid в диапазоне 16-255
        var fid = (uint)Math.Round(frequencyMHz / 100.0 * 8);
        return Math.Clamp(fid, 16u, 255u);
    }

    private static uint CalculateDidFromFrequency(double frequencyMHz, uint fid)
    {
        // Did = (Fid * 25 * 100) / (Freq * 12.5)
        var did = (fid * 25.0 * 100.0) / (frequencyMHz * 12.5);
        return (uint)Math.Clamp(Math.Round(did), 8.0, 63.0);
    }

    private static uint CalculateVidFromVoltage(double voltageMillivolts)
    {
        // Vid = (1.55 - Voltage) / 0.00625
        var voltageVolts = voltageMillivolts / 1000.0;
        var vid = (uint)Math.Round((1.55 - voltageVolts) / 0.00625);
        return Math.Clamp(vid, 0u, 255u);
    }

    private bool ApplyTscWorkaround()
    {
        try
        {
            uint eax = 0, edx = 0;
            if (!_cpuService.ReadMsr(MsrHwcr, ref eax, ref edx))
            {
                _logger.LogError("Failed to read HWCR MSR");
                return false;
            }

            eax |= HwcrTscFreqSel; // Bit 21
            return _cpuService.WriteMsr(MsrHwcr, eax, edx);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write HWCR MSR");
            return false;
        }
    }

    private bool WritePstateToAllNodes(int stateNumber, uint eax, uint edx)
    {
        var msr = MsrPstateBase + (uint)stateNumber;

        if (NumaUtil.HighestNumaNode > 0)
        {
            // Мультисокетная система
            for (var node = 0u; node <= NumaUtil.HighestNumaNode; node++)
            {
                NumaUtil.SetThreadProcessorAffinity(
                    (ushort)(node + 1),
                    [.. Enumerable.Range(0, Environment.ProcessorCount)]
                );

                if (!_cpuService.WriteMsr(msr, eax, edx))
                {
                    _logger.LogError("Failed to write P-State {stateNumber} on NUMA node {node}", stateNumber, node);
                    return false;
                }
            }
        }
        else
        {
            // Одиночная система
            if (!_cpuService.WriteMsr(msr, eax, edx))
            {
                _logger.LogError("Failed to write P-State {stateNumber}", stateNumber);
                return false;
            }
        }

        return true;
    }
}