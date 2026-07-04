using Saku_Overclock.Shared.Models;

namespace Saku_Overclock.Core.Contracts;

public interface IDataProvider
{
    /// <summary>
    ///     Получает и обновляет данные сенсоров для мониторинга.
    /// </summary>
    void GetData(ref SensorsInformation sensorsInformation);

    /// <summary>
    ///     Получить таблицу сенсоров устройства
    /// </summary>
    /// <returns>Таблица сенсоров устройства</returns>
    float[]? GetPowerTable();
}