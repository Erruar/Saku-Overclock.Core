namespace Saku_Overclock.Core.Contracts;

public interface IRawSharedMemoryWriterService
{
    bool IsRawUpdateActive { get; }
    void RegisterIpcHandlers();
    void UpdateRawData(float[]? rawData);
}