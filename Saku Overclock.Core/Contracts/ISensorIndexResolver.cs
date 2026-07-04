using Saku_Overclock.Shared.Models;

namespace Saku_Overclock.Core.Contracts;

public interface ISensorIndexResolver
{
    /// <summary>
    ///     Получить индекс в таблице сенсоров устройства для требуемого индекса
    /// </summary>
    /// <param name="tableVersion">Версия таблицы сенсоров устройства (получить от Smu)</param>
    /// <param name="sensor">Тип требуемого сенсора</param>
    /// <returns>Индекс в таблице сенсоров устройства</returns>
    int ResolveIndex(int tableVersion, SensorId sensor);
}