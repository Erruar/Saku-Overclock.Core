using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using ZenStates.Core;
using Saku_Overclock.Shared;
using Saku_Overclock.Core.Contracts;
using Saku_Overclock.Shared.Models;
using static ZenStates.Core.Cpu;

namespace Saku_Overclock.Core.Services;

public class CpuService : ICpuService
{
    private readonly Cpu? _cpu;
    private readonly CodeName _codeName;
    private readonly ILogger<CpuService> _logger;

    public bool IsAvailable { get; }

    public CpuService(ILogger<CpuService> logger)
    {
        _logger = logger;
        
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
        catch (Exception e)
        {
            logger.LogError(e, "CRITICAL: ZenStates-Core Service start Error");
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
            if (!IsAvailable || _cpu == null) throw new Exception("Core unavailable.");
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
        set => _cpu?.smu.Mp1Smu.SMU_MSG_SetDldoPsmMargin = value;
    }

    public uint SmuCoperCommandRsmu
    {
        get => _cpu?.smu.Rsmu.SMU_MSG_SetDldoPsmMargin ?? 0;
        set => _cpu?.smu.Rsmu.SMU_MSG_SetDldoPsmMargin = value;
    }

    public void SetCoperSingleCore(uint coreMask, int margin) => _cpu?.SetPsmMarginSingleCore(coreMask, margin);
    public void RefreshPowerTable() => _cpu?.RefreshPowerTable();
    public float[] PowerTable => _cpu?.powerTable?.Table ?? [];
    public uint PowerTableVersion => _cpu?.smu?.TableVersion ?? 0; 
    public uint PowerTableSize => _cpu?.RyzenSmu?.PmTableSize ?? 0; 
    public float SocMemoryClock => _cpu?.powerTable?.MCLK ?? 0;
    public float SocFabricClock => _cpu?.powerTable?.FCLK ?? 0;
    public float SocVoltage => _cpu?.powerTable?.VDDCR_SOC ?? 0;
    public double GetCoreMultiplier(int core) => (_cpu?.GetCoreMulti(core) ?? 0) / 10.0;
    public float? GetCpuTemperature() => _cpu?.GetCpuTemperature();
    public double ReturnCpuPowerLimit() => _cpu?.GetSystemPowerLimit()?.PowerLimit ?? -1;
    
    /// <summary>
    ///  Проверка доступности андервольтинга
    /// </summary>
    public bool ReturnUndervoltingAvailability()
    {
        if (GetCodenameGeneration() is CodenameGeneration.Fp5 or CodenameGeneration.Am4V1 or CodenameGeneration.Am5
            || _cpu?.GetPsmMarginSingleCore(0u) != 0u)
            return true;

        return _cpu?.SetPsmMarginSingleCore(0,0) == true;
    }
    
    
    public List<ApplyResult> ApplyPresetInternal(Preset preset)
    {
        var results = new List<ApplyResult>();

        if (_cpu == null)
        {
            results.Add(new ApplyResult("Preset_ApplyFailed", false, SmuStatus.CoreUnavailable));
            return results;
        }
            
        var generation = GetCodenameGeneration();
        var isBristol = generation == CodenameGeneration.Fp4;

        // Вспомогательная локальная функция для безопасного применения и записи лога
        void TryApply(string paramName, Func<SMU.Status> action, string[]? affectedParameters = null)
        {
            try
            {
                var status = action();
                var isSuccess = status == SMU.Status.OK; 
                results.Add(new ApplyResult(paramName, isSuccess, (SmuStatus)status, affectedParameters));
                
                if (!isSuccess)
                    _logger.LogWarning("Command {ParamName} returned status {Status}", paramName, status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception applying command {ParamName}", paramName);
                results.Add(new ApplyResult(paramName, false, SmuStatus.CoreFailed, affectedParameters));
            }
        }

        // ==========================================
        // 1. CPU Settings
        // ==========================================
        if (preset.CpuSettings.CpuMaximumTemperature.IsEnabled)
            TryApply("Param_CPU_c1/Text", () => _cpu.SetTctlMax((uint)preset.CpuSettings.CpuMaximumTemperature.Value));

        if (preset.CpuSettings.CpuSustainedPowerLimit.IsEnabled
            || (isBristol && preset.CpuSettings.CpuBoostTimeSlow.IsEnabled))
        {
            var limit = (uint)preset.CpuSettings.CpuSustainedPowerLimit.Value;
            if (isBristol)
                TryApply("Param_CPU_c2/Text", () => _cpu.SetBristolStapmLimit(limit, (uint)preset.CpuSettings.CpuBoostTimeSlow.Value), [ "Param_CPU_c5/Text"]);
            else
                TryApply("Param_CPU_c2/Text", () => _cpu.SetStapmLimit(limit));
        }

        if (preset.CpuSettings.CpuActualPowerLimit.IsEnabled)
            TryApply("Param_CPU_c3/Text", () => _cpu.SetFastLimit((uint)preset.CpuSettings.CpuActualPowerLimit.Value));
        
        if (preset.CpuSettings.CpuAveragePowerLimit.IsEnabled)
            TryApply("Param_CPU_c4/Text", () => _cpu.SetSlowLimit((uint)preset.CpuSettings.CpuAveragePowerLimit.Value));
        
        if (preset.CpuSettings.CpuBoostTimeSlow.IsEnabled && !isBristol)
            TryApply("Param_CPU_c5/Text", () => _cpu.SetStapmTime((uint)preset.CpuSettings.CpuBoostTimeSlow.Value));
        
        if (preset.CpuSettings.CpuBoostTimeFast.IsEnabled)
            TryApply("Param_CPU_c6/Text", () => _cpu.SetSlowTime((uint)preset.CpuSettings.CpuBoostTimeFast.Value));
        
        if (preset.CpuSettings.LaptopPowerLimit.IsEnabled)
            TryApply("Param_ADV_a9/Text", () => _cpu.SetSttLimit((uint)preset.CpuSettings.LaptopPowerLimit.Value));
        
        if (preset.CpuSettings.IntegratedGpuMaximumTemperature.IsEnabled)
            TryApply("Param_ADV_a6/Text", () => _cpu.SetApuSkinTempLimit((uint)preset.CpuSettings.IntegratedGpuMaximumTemperature.Value));
           
        if (preset.CpuSettings.DiscreteGpuMaximumTemperature.IsEnabled)
            TryApply("Param_ADV_a7/Text", () => _cpu.SetGpuSkinTempLimit((uint)preset.CpuSettings.DiscreteGpuMaximumTemperature.Value));
           
        if (preset.CpuSettings.IntegratedGpuPowerLimit.IsEnabled)
            TryApply("Param_ADV_a8/Text", () => _cpu.SetApuSlowLimit((uint)preset.CpuSettings.IntegratedGpuPowerLimit.Value));
        
        // ==========================================
        // 2. VRM Settings 
        // ==========================================
        if (preset.VrmSettings.VrmCpuEdcCurrentLimit.IsEnabled 
            || (isBristol && preset.VrmSettings.VrmSocEdcCurrentLimit.IsEnabled))
        {
            var edc = (uint)preset.VrmSettings.VrmCpuEdcCurrentLimit.Value;
            if (isBristol)
                TryApply("Param_VRM_v1/Text", () => 
                    _cpu.SetBristolEdcLimit(edc, (uint)preset.VrmSettings.VrmSocEdcCurrentLimit.Value), [ "Param_VRM_v3/Text" ]);
            else
                TryApply("Param_VRM_v1/Text", () => _cpu.SetEDCVDDLimit(edc));
        }
        
        if (preset.VrmSettings.VrmCpuTdcCurrentLimit.IsEnabled 
            || (isBristol && preset.VrmSettings.VrmSocTdcCurrentLimit.IsEnabled))
        {
            var tdc = (uint)preset.VrmSettings.VrmCpuTdcCurrentLimit.Value;
            if (isBristol)
                TryApply("Param_VRM_v2/Text", () => 
                    _cpu.SetBristolTdcLimit(tdc, (uint)preset.VrmSettings.VrmSocTdcCurrentLimit.Value), [ "Param_VRM_v4/Text" ]);
            else
                TryApply("Param_VRM_v2/Text", () => _cpu.SetTDCVDDLimit(tdc));
        }
        
        if (preset.VrmSettings.VrmSocEdcCurrentLimit.IsEnabled && !isBristol)
            TryApply("Param_VRM_v3/Text", () => _cpu.SetEDCSOCLimit((uint)preset.VrmSettings.VrmSocEdcCurrentLimit.Value));
        
        if (preset.VrmSettings.VrmSocTdcCurrentLimit.IsEnabled && !isBristol)
            TryApply("Param_VRM_v4/Text", () => _cpu.SetTDCSOCLimit((uint)preset.VrmSettings.VrmSocTdcCurrentLimit.Value));

        if (preset.VrmSettings.VrmPowerSaveVddCurrentLimit.IsEnabled 
            || (isBristol && preset.VrmSettings.VrmPowerSaveSocCurrentLimit.IsEnabled))
        {
            var psi = (uint)preset.VrmSettings.VrmPowerSaveVddCurrentLimit.Value;
            if (isBristol)
                TryApply("Param_VRM_v5/Text", () => 
                    _cpu.SetBristolPsi0Limit(psi, (uint)preset.VrmSettings.VrmPowerSaveSocCurrentLimit.Value), [ "Param_VRM_v6/Text" ]);
            else
                TryApply("Param_VRM_v5/Text", () => _cpu.SetPsi0Current(psi));
        }
        
        if (preset.VrmSettings.VrmPowerSaveSocCurrentLimit.IsEnabled && !isBristol)
            TryApply("Param_VRM_v6/Text", () => _cpu.SetPsi0SocCurrent((uint)preset.VrmSettings.VrmPowerSaveSocCurrentLimit.Value));

        if (preset.VrmSettings.VrmPowerSaveCpuCurrentLimit.IsEnabled)
            TryApply("Param_ADV_a4/Text", () => _cpu.Psi3CpuCurrent((uint)preset.VrmSettings.VrmPowerSaveCpuCurrentLimit.Value));

        if (preset.VrmSettings.VrmPowerSaveGpuCurrentLimit.IsEnabled)
            TryApply("Param_ADV_a5/Text", () => _cpu.Psi3GfxCurrent((uint)preset.VrmSettings.VrmPowerSaveGpuCurrentLimit.Value));

        if (preset.VrmSettings.VrmCpuFrequencyRestoreTime.IsEnabled)
            TryApply("Param_VRM_v7/Text", () => _cpu.SetProchotDeassertionRamp((uint)preset.VrmSettings.VrmCpuFrequencyRestoreTime.Value));
        
        // ==========================================
        // 3. iGPU and CPU subsystems settings
        // ==========================================
        if (preset.SubsystemsSettings.MinimumDataLatchFrequency.IsEnabled)
            TryApply("Param_GPU_g7/Text", () => _cpu.SetCpuSubsystemFrequencyLimit(CpuSubsystem.Lclk,(uint)preset.SubsystemsSettings.MinimumDataLatchFrequency.Value, false));
        
        if (preset.SubsystemsSettings.MinimumFabricFrequency.IsEnabled)
            TryApply("Param_GPU_g3/Text", () => _cpu.SetCpuSubsystemFrequencyLimit(CpuSubsystem.Fclk,(uint)preset.SubsystemsSettings.MinimumFabricFrequency.Value, false));
        
        if (preset.SubsystemsSettings.MinimumIntegratedGraphicsFrequency.IsEnabled)
            TryApply("Param_GPU_g9/Text", () => _cpu.SetCpuSubsystemFrequencyLimit(CpuSubsystem.Gpu,(uint)preset.SubsystemsSettings.MinimumIntegratedGraphicsFrequency.Value, false));
        
        if (preset.SubsystemsSettings.MinimumSocFrequency.IsEnabled)
            TryApply("Param_GPU_g1/Text", () => _cpu.SetCpuSubsystemFrequencyLimit(CpuSubsystem.Soc,(uint)preset.SubsystemsSettings.MinimumSocFrequency.Value, false));
        
        if (preset.SubsystemsSettings.MinimumVideoCodecFrequency.IsEnabled)
            TryApply("Param_GPU_g5/Text", () => _cpu.SetCpuSubsystemFrequencyLimit(CpuSubsystem.Vcn,(uint)preset.SubsystemsSettings.MinimumVideoCodecFrequency.Value, false));
        
        if (preset.SubsystemsSettings.MaximumDataLatchFrequency.IsEnabled)
            TryApply("Param_GPU_g8/Text", () => _cpu.SetCpuSubsystemFrequencyLimit(CpuSubsystem.Lclk,(uint)preset.SubsystemsSettings.MaximumDataLatchFrequency.Value));
        
        if (preset.SubsystemsSettings.MaximumFabricFrequency.IsEnabled)
            TryApply("Param_GPU_g4/Text", () => _cpu.SetCpuSubsystemFrequencyLimit(CpuSubsystem.Fclk,(uint)preset.SubsystemsSettings.MaximumFabricFrequency.Value));
        
        if (preset.SubsystemsSettings.MaximumIntegratedGraphicsFrequency.IsEnabled)
            TryApply("Param_GPU_g10/Text", () => _cpu.SetCpuSubsystemFrequencyLimit(CpuSubsystem.Gpu,(uint)preset.SubsystemsSettings.MaximumIntegratedGraphicsFrequency.Value));
        
        if (preset.SubsystemsSettings.MaximumSocFrequency.IsEnabled)
            TryApply("Param_GPU_g2/Text", () => _cpu.SetCpuSubsystemFrequencyLimit(CpuSubsystem.Soc,(uint)preset.SubsystemsSettings.MaximumSocFrequency.Value));
        
        if (preset.SubsystemsSettings.MaximumVideoCodecFrequency.IsEnabled)
            TryApply("Param_GPU_g6/Text", () => _cpu.SetCpuSubsystemFrequencyLimit(CpuSubsystem.Vcn,(uint)preset.SubsystemsSettings.MaximumVideoCodecFrequency.Value));
        
        // ==========================================
        // 4. Advanced Options
        // ==========================================
        if (preset.CpuModesSettings.OverclockMode is { IsEnabled: true, Value: 1 })
            TryApply("Param_ADV_a14_E/Content", () => _cpu.EnableOcMode());
        
        if (preset.CpuModesSettings.OverclockMode is { IsEnabled: true, Value: 0 })
            TryApply("Param_ADV_a14_E/Content", () => _cpu.DisableOcMode());
        
        if (preset.CpuModesSettings.PboScalar.IsEnabled)
            TryApply("Param_ADV_a15/Text", () => _cpu.SetPBOScalar((uint)preset.CpuModesSettings.PboScalar.Value));
        
        if (preset.CpuModesSettings.PreferredMode is { IsEnabled: true, Value: 1 })
            TryApply("Param_ADV_a13_U/Content", () => _cpu.SetPowerSavingMode(true));
        
        if (preset.CpuModesSettings.PreferredMode is { IsEnabled: true, Value: 2 })
            TryApply("Param_ADV_a13_E/Content", () => _cpu.SetPowerSavingMode(false));
        
        if (preset.CpuModesSettings.CpuFrequency04Fix.IsEnabled)
        {
            var mode = preset.CpuModesSettings.CpuFrequency04Fix.Value == 0;
            switch (generation)
            {
                case CodenameGeneration.Fp6 or CodenameGeneration.Ff3:
                case CodenameGeneration.Fp5 when IsRaven:
                    TryApply("Param_GPU_g16/Text", () => _cpu.ManageSmuFeatureState(mode, 37));
                    break;
                case CodenameGeneration.Fp7 or CodenameGeneration.Fp8:
                    TryApply("Param_GPU_g16/Text", () => _cpu.ManageSmuFeatureState(mode, 36));
                    break;
                case CodenameGeneration.Am5:
                    TryApply("Param_GPU_g16/Text", () => _cpu.ManageSmuFeatureState(mode, 7));
                    break;
                default:
                    TryApply(
                        "Param_GPU_g16/Text",
                        mode ? () => _cpu.StopBtcMode()
                             : () => _cpu.StartBtcMode());
                    break;
            }
        } 
        
        // ==========================================
        // 5. Curve Optimizer
        // ==========================================
        if (preset.CurveOptimizerOptions.CpuCurveOptimizerUndervoltingLevel.IsEnabled)
            TryApply("Param_CO_O1/Text", () => _cpu.SetPsmMarginAllCores((int)preset.CurveOptimizerOptions.CpuCurveOptimizerUndervoltingLevel.Value) ? SMU.Status.OK : SMU.Status.CMD_REJECTED_PREREQ); 
        
        if (preset.CurveOptimizerOptions.IntegratedGpuCurveOptimizerUndervoltingLevel.IsEnabled)
            TryApply("Param_CO_O2/Text", () => _cpu.SetGpuPsmMargin((int)preset.CurveOptimizerOptions.IntegratedGpuCurveOptimizerUndervoltingLevel.Value) ? SMU.Status.OK : SMU.Status.CMD_REJECTED_PREREQ); 
        
        // ==========================================
        // 6. Curve Optimizer Advanced
        // ==========================================
        if (preset.CurveOptimizerAdvancedOptions.CurveOptimizerPreferredMode is { IsEnabled: true, Value: 1 })
        {
            var coreSettings =
                preset.CurveOptimizerAdvancedOptions.CurveOptimizerCores;

            var coreCount = Math.Min(
                PhysicalCores,
                Math.Min(
                    coreSettings.IsEnabled.Length,
                    coreSettings.Value.Length));
            for (var i = 0; i < coreCount; i++)
            {
                if (!coreSettings.IsEnabled[i])
                    continue;

                var margin = Convert.ToInt32(coreSettings.Value[i]);

                var ccd = (uint)(i / 8);
                var core = (uint)(i % 8);

                TryApply(
                    $"Param_CPU_CO_CCD{ccd}_{core}/Text",
                    () => _cpu.SetPsmMarginSingleCore(
                        core,
                        ccd,
                        0,
                        margin) ? SMU.Status.OK : SMU.Status.CMD_REJECTED_PREREQ, [ "Param_CCD1_CO_Section/Text" ]);
                
            }
        }
        
        // ==========================================
        // 7. Frequencies Settings
        // ==========================================
        if (preset.FrequenciesSettings.CpuFrequency.IsEnabled)
            TryApply("Param_ADV_a11/Text", () => _cpu.SetFrequencyAllCore((uint)preset.FrequenciesSettings.CpuFrequency.Value) ? SMU.Status.OK : SMU.Status.CMD_REJECTED_PREREQ);

        if (preset.FrequenciesSettings.CpuVoltage.IsEnabled)
            TryApply("Param_ADV_a12/Text", () => _cpu.SetOverclockCpuVid((uint)((1.55 - Math.Min(1.55, preset.FrequenciesSettings.CpuVoltage.Value)) / 0.00625)));

        if (preset.FrequenciesSettings.IntegratedGraphicsFrequency.IsEnabled)
        {
            if (_cpu.smu?.Rsmu?.SMU_MSG_SetFixedGfxClkFreq > 0)
                TryApply("Param_ADV_a10/Text", () => _cpu.SetFixedGfxClkFreq((uint)preset.FrequenciesSettings.IntegratedGraphicsFrequency.Value));
            else
                TryApply("Param_ADV_a10/Text", () => _cpu.SetGfxClkOverdrive((uint)preset.FrequenciesSettings.IntegratedGraphicsFrequency.Value, 56));
        }
        
        return results;
    }
}