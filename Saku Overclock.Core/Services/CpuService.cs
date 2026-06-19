using Microsoft.Win32;
using ZenStates.Core;
using Saku_Overclock.Shared;
using Saku_Overclock.Core.Contracts;
using static ZenStates.Core.Cpu;

namespace Saku_Overclock.Core.Services;

public class CpuService : ICpuService
{
    private readonly Cpu? _cpu;
    private readonly CodeName _codeName;

    public bool IsAvailable { get; }

    public CpuService()
    {
        try
        {
            if (!PawnIo.IsInstalled)
            {
                IsAvailable = false;
                return;
            }

            _cpu = new Cpu();
            _codeName = _cpu.info.codeName;

            IsAvailable = true;
        }
        catch
        {
            IsAvailable = false;
        }
    }

    public bool? IsPlatformPc()
    {
        if (IsPlatformPcByCodename() == true)
        {
            if (_codeName is CodeName.RavenRidge or CodeName.Picasso or CodeName.Renoir or CodeName.Cezanne or CodeName.Phoenix or CodeName.Phoenix2)
            {
                if (_cpu?.info.packageType == PackageType.FPX)
                {
                    if (_cpu.info.cpuName.Contains('G') ||
                            _cpu.info.cpuName.Contains("GE") ||
                            (_cpu.info.cpuName.Contains('X') && !_cpu.info.cpuName.Contains("HX")) ||
                            _cpu.info.cpuName.Contains('F') ||
                            (_cpu.info.cpuName.Contains("X3D") && !_cpu.info.cpuName.Contains("HX3D")) ||
                            _cpu.info.cpuName.Contains("XT")
                    )
                    {
                        return true;
                    }
                    return false;
                }
            }
            return true;
        }
        return null;
    }

    public bool? IsPlatformPcByCodename()
    {
        return _codeName switch
        {
            CodeName.BristolRidge or CodeName.SummitRidge or CodeName.PinnacleRidge => true,
            CodeName.RavenRidge or CodeName.Picasso or CodeName.Dali or CodeName.FireFlight => false,
            CodeName.Matisse or CodeName.Vermeer => true,
            CodeName.Renoir or CodeName.Lucienne or CodeName.Cezanne => false,
            CodeName.VanGogh => false,
            CodeName.KrackanPoint or CodeName.KrackanPoint2 => false,
            CodeName.Mendocino or CodeName.Rembrandt or CodeName.Phoenix or CodeName.Phoenix2 or CodeName.HawkPoint or CodeName.StrixPoint or CodeName.StrixHalo => false,
            CodeName.GraniteRidge or CodeName.Genoa or CodeName.Bergamo or CodeName.Raphael or CodeName.DragonRange => true,
            _ => null,
        };
    }

    public byte SendSmuCommand(SmuAddressSet mailbox, uint command, ref uint[] arguments)
    {
        if (_cpu == null) return 48; // TimeoutMutexLock status byte representation

        var normalizedMailbox = new Mailbox
        {
            SMU_ADDR_MSG = mailbox.MsgAddress,
            SMU_ADDR_RSP = mailbox.RspAddress,
            SMU_ADDR_ARG = mailbox.ArgAddress
        };

        return (byte)_cpu.smu.SendSmuCommand(normalizedMailbox, command, ref arguments);
    }

    public CodenameGeneration GetCodenameGeneration()
    {
        switch (_codeName)
        {
            case CodeName.BristolRidge: return CodenameGeneration.Fp4;
            case CodeName.SummitRidge:
            case CodeName.PinnacleRidge: return CodenameGeneration.Am4V1;
            case CodeName.RavenRidge:
            case CodeName.Picasso:
            case CodeName.Dali:
            case CodeName.FireFlight: return CodenameGeneration.Fp5;
            case CodeName.Matisse:
            case CodeName.Vermeer: return CodenameGeneration.Am4V2;
            case CodeName.Renoir:
            case CodeName.Lucienne:
            case CodeName.Cezanne: return CodenameGeneration.Fp6;
            case CodeName.VanGogh: return CodenameGeneration.Ff3;
            case CodeName.Mendocino:
            case CodeName.Rembrandt:
            case CodeName.Phoenix:
            case CodeName.Phoenix2:
            case CodeName.HawkPoint:
            case CodeName.KrackanPoint:
            case CodeName.KrackanPoint2: return CodenameGeneration.Fp7;
            case CodeName.StrixPoint:
            case CodeName.StrixHalo: return CodenameGeneration.Fp8;
            case CodeName.Raphael:
            case CodeName.GraniteRidge:
            case CodeName.Genoa:
            case CodeName.StormPeak:
            case CodeName.DragonRange:
            case CodeName.Bergamo: return CodenameGeneration.Am5;
        }
        return CodenameGeneration.Unknown;
    }

    public bool IsRaven => _codeName == CodeName.RavenRidge;
    public bool IsDragonRange => _codeName == CodeName.DragonRange;
    public uint PhysicalCores => _cpu?.info.topology.physicalCores ?? (uint)Environment.ProcessorCount;
    public uint[] CoreDisableMap => _cpu?.info.topology.coreDisableMap ?? [];
    public uint Cores => _cpu?.info.topology.cores ?? (uint)Environment.ProcessorCount;

    public SmuAddressSet Rsmu => new(_cpu?.smu.Rsmu?.SMU_ADDR_MSG ?? 0, _cpu?.smu.Rsmu?.SMU_ADDR_RSP ?? 0, _cpu?.smu.Rsmu?.SMU_ADDR_ARG ?? 0);
    public SmuAddressSet Mp1 => new(_cpu?.smu.Mp1Smu?.SMU_ADDR_MSG ?? 0, _cpu?.smu.Mp1Smu?.SMU_ADDR_RSP ?? 0, _cpu?.smu.Mp1Smu?.SMU_ADDR_ARG ?? 0);
    public SmuAddressSet Hsmp => new(_cpu?.smu.Hsmp?.SMU_ADDR_MSG ?? 0, _cpu?.smu.Hsmp?.SMU_ADDR_RSP ?? 0, _cpu?.smu.Hsmp?.SMU_ADDR_ARG ?? 0);

    public CpuFamily Family => (CpuFamily)(_cpu?.info.family ?? 0);
    public bool ReadMsr(uint index, ref uint eax, ref uint edx) => _cpu?.ReadMsr(index, ref eax, ref edx) ?? false;
    public bool WriteMsr(uint msr, uint eax, uint edx) => _cpu?.WriteMsr(msr, eax, edx) ?? false;

    public string CpuName => ReadCpuInformation().name;
    public bool Smt => _cpu?.systemInfo.SMT ?? true;

    private static (string name, string baseClock) ReadCpuInformation()
    {
        const string key = @"HARDWARE\DESCRIPTION\System\CentralProcessor\0";
        using var reg = Registry.LocalMachine.OpenSubKey(key);
        var name = reg?.GetValue("ProcessorNameString") as string ?? "";
        var mhz = reg?.GetValue("~MHz")?.ToString() ?? "";
        return (name, mhz);
    }

    public CommonMotherBoardInfo MotherBoardInfo => new() 
    { 
        MotherBoardName = _cpu?.systemInfo.MbName, 
        MotherBoardVendor = _cpu?.systemInfo.MbVendor, 
        BiosVersion = _cpu?.systemInfo.BiosVersion 
    };

    public MemoryConfig GetMemoryConfig()
    {
        try
        {
            if (!IsAvailable || _cpu == null) throw new Exception();
            var memoryConfig = _cpu.GetMemoryConfig();
            var convertedModules = new List<MemoryModule>();
            foreach (var module in memoryConfig.Modules)
            {
                convertedModules.Add(new MemoryModule
                {
                    Capacity = module.Capacity.ToString(),
                    Manufacturer = module.Manufacturer,
                    PartNumber = module.PartNumber
                });
            }

            var umcBase = _cpu.ReadDword(0x50200);
            var umcOffset1 = _cpu.ReadDword(0x50204);
            var umcOffset2 = _cpu.ReadDword(0x50208);

            var freqFromRatio = ((MemType)memoryConfig.Type == MemType.Ddr4 ? (umcBase & 0x7F) / 3 : (umcBase & 0xFFFF) / 100) * 200;

            return new MemoryConfig
            {
                Type = (MemType)memoryConfig.Type,
                TotalCapacity = (int)(memoryConfig.TotalCapacity.SizeInBytes / 1073741824),
                Modules = convertedModules,
                MemorySpeed = (int)_cpu.powerTable.MCLK * 2,
                FrequencyFromTimings = (int)freqFromRatio,
                MemoryTimings = new MemoryTimings
                {
                    Tcl = (umcOffset1 & 0x3F) + "T",
                    Trcdwr = ((umcOffset1 >> 24) & 0x3F) + "T",
                    Trcdrd = ((umcOffset1 >> 16) & 0x3F) + "T",
                    Tras = ((umcOffset1 >> 8) & 0x7F) + "T",
                    Trp = ((umcOffset2 >> 16) & 0x3F) + "T",
                    Trc = (umcOffset2 & 0xFF) + "T"
                }
            };
        }
        catch
        {
            return new MemoryConfig { Type = MemType.Unknown, Modules = [] };
        }
    }

    public bool Avx512AvailableByCodename => _codeName >= CodeName.Raphael;
    public string CpuCodeName => _codeName.ToString();
    public string SmuVersion => _cpu?.systemInfo.SmuVersionString ?? "0.0.0";

    public uint MakeCoreMask(uint core = 0u, uint ccd = 0u, uint ccx = 0u) => _cpu?.MakeCoreMask(core, ccd, ccx) ?? 0;

    public uint SmuCoperCommandMp1
    {
        get => _cpu?.smu.Mp1Smu.SMU_MSG_SetDldoPsmMargin ?? 0;
        set { if (_cpu != null) _cpu.smu.Mp1Smu.SMU_MSG_SetDldoPsmMargin = value; }
    }

    public uint SmuCoperCommandRsmu
    {
        get => _cpu?.smu.Rsmu.SMU_MSG_SetDldoPsmMargin ?? 0;
        set { if (_cpu != null) _cpu.smu.Rsmu.SMU_MSG_SetDldoPsmMargin = value; }
    }

    public void SetCoperSingleCore(uint coreMask, int margin) => _cpu?.SetPsmMarginSingleCore(coreMask, margin);
    public void RefreshPowerTable() => _cpu?.RefreshPowerTable();
    public float[] PowerTable => _cpu?.powerTable?.Table ?? [];
    public uint PowerTableVersion => _cpu?.smu.TableVersion ?? 0;
    public float SocMemoryClock => _cpu?.powerTable?.MCLK ?? 0;
    public float SocFabricClock => _cpu?.powerTable?.FCLK ?? 0;
    public float SocVoltage => _cpu?.powerTable?.VDDCR_SOC ?? 0;
    public double GetCoreMultiplier(int core) => (_cpu?.GetCoreMulti(core) ?? 0) / 10.0;
    public float? GetCpuTemperature() => _cpu?.GetCpuTemperature();

    public void GenerateDebugReport()
    {
        // Перемещено логирование отчетов в общую директорию ProgramData / Logs
    }
}