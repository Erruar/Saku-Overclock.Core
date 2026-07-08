using Saku_Overclock.Shared.Models;

namespace Saku_Overclock.Core.Contracts;

public interface IRtssSettingsService
{
    void RegisterIpcHandlers();
    
    /// <summary>
    ///     Был ли RTSS обновлён
    /// </summary>
    public bool IsRtssUpdated
    {
        get; 
        set;
    }

    /// <summary>
    ///     Обновление отображаемых параметров оверлея
    /// </summary>
    /// <param name="sensorsInformation">Данные сенсоров</param>
    /// <param name="appliedPreset">Выбранный пресет</param>
    /// <param name="coreCount">Количество ядер</param>
    public void UpdateRtssMetrics(SensorsInformation sensorsInformation, string? appliedPreset, int? coreCount);
}