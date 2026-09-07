using Microsoft.Extensions.Logging;
using Saku_Overclock.Core.Contracts;
using Saku_Overclock.Shared;
using Saku_Overclock.Shared.Models;

namespace Saku_Overclock.Core.Services;

public class BristolPstateStrategy : IPstateStrategy
{
    private readonly ICpuService _cpuService;
    private readonly ILogger<IPstateService> _logger;

    private const uint MsrPstateBase = 0xC0010064;

    public bool IsSupportedFamily
    {
        get;
    }

    public BristolPstateStrategy(
        ICpuService cpuService,
        ILogger<IPstateService> logger)
    {
        _cpuService = cpuService;
        _logger = logger;
        IsSupportedFamily =  _cpuService.Family is > CpuFamily.Family10H and < CpuFamily.Family17H;
    }

    public PstateOperationResult ReadPstate(int stateNumber)
    {
        if (stateNumber is < 0 or > 7)
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

            var pstate = ParseBristolPstate(eax, edx, stateNumber);
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
        throw new NotSupportedException();
    }

    public bool ValidateParameters(PstateWriteParams parameters, out string? error)
    {
        error = null;

        if (parameters.StateNumber is < 0 or > 7)
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
    // Приватные методы для Pre-Ryzen
    // ====================================================================

    private static PstateData ParseBristolPstate(uint eax, uint edx, int stateNumber)
    {
        // Family 15h MSRC001_00[64...6B] структура:
        // EAX[5:0]   = CpuFid
        // EAX[8:6]   = CpuDid
        // EAX[16:9]  = CpuVid (Бит 16 - это CpuVid[7], биты 15:9 - это CpuVid[6:0])
        // EAX[22]    = NbPstate (опционально)
        // EDX[7:0]   = IddValue (Биты 39:32 в 64-битном MSR)
        // EDX[9:8]   = IddDiv   (Биты 41:40 в 64-битном MSR)
        // EDX[31]    = PstateEn (Бит 63 в 64-битном MSR)

        var fid = eax & 0x3F;
        var did = (eax >> 6) & 0x07;
        var vid = (eax >> 9) & 0xFF;
        
        var iddValue = edx & 0xFF;
        var iddDiv = (edx >> 8) & 0x03;
        var isEnabled = (edx & 0x80000000) != 0;

        // Расчёт частоты: CoreCOF = 100 * (CpuFid + 10h) / (2^CpuDid)
        // Сдвиг 1 << (int)did эквивалентен 2^CpuDid, что корректно работает для значений 0-4.
        var frequencyMHz = 100.0 * (fid + 0x10) / (1 << (int)did);

        // Расчёт напряжения (8-bit VID SVI2)
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
}