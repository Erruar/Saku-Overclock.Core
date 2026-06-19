using Saku_Overclock.Shared;

namespace Saku_Overclock.Core.Contracts;

public interface ICpuService
{
    bool IsAvailable { get; }
    bool IsRaven { get; }
    bool IsDragonRange { get; }
    uint PhysicalCores { get; }
    uint[] CoreDisableMap { get; }
    uint Cores { get; }
    SmuAddressSet Rsmu { get; }
    SmuAddressSet Mp1 { get; }
    SmuAddressSet Hsmp { get; }
    CpuFamily Family { get; }
    string CpuName { get; }
    bool Smt { get; }
    CommonMotherBoardInfo MotherBoardInfo { get; }
    bool Avx512AvailableByCodename { get; }
    string CpuCodeName { get; }
    string SmuVersion { get; }
    uint SmuCoperCommandMp1 { get; set; }
    uint SmuCoperCommandRsmu { get; set; }
    float[] PowerTable { get; }
    uint PowerTableVersion { get; }
    float SocMemoryClock { get; }
    float SocFabricClock { get; }
    float SocVoltage { get; }

    byte SendSmuCommand(SmuAddressSet mailbox, uint command, ref uint[] arguments);
    CodenameGeneration GetCodenameGeneration();
    bool? IsPlatformPc();
    bool? IsPlatformPcByCodename();
    bool ReadMsr(uint index, ref uint eax, ref uint edx);
    bool WriteMsr(uint msr, uint eax, uint edx);
    MemoryConfig GetMemoryConfig();
    uint MakeCoreMask(uint core = 0u, uint ccd = 0u, uint ccx = 0u);
    void SetCoperSingleCore(uint coreMask, int margin);
    void RefreshPowerTable();
    double GetCoreMultiplier(int core);
    float? GetCpuTemperature();
    void GenerateDebugReport();
}