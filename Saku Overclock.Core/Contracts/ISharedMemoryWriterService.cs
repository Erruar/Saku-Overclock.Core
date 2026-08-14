using Saku_Overclock.Shared.Models;

namespace Saku_Overclock.Core.Contracts;

public interface ISharedMemoryWriterService
{
    /// <summary>
    ///     Update Shared Memory
    /// </summary>
    /// <param name="data">Sensors data</param>
    void UpdateSharedMemory(SensorsInformation data);
}