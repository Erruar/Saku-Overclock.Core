using Saku_Overclock.Shared.Models;

namespace Saku_Overclock.Core.Contracts;

public interface INotifyIconsService
{
    void RegisterIpcHandlers();
    
    /// <summary>
    ///     Создаёт трей иконки
    /// </summary>
    public void CreateNotifyIcons();

    /// <summary>
    ///     Обновить данные в иконках
    /// </summary>
    /// <param name="sensorsInformation">Данные сенсоров</param>
    public void UpdateNotifyIcons(SensorsInformation sensorsInformation);

    /// <summary>
    ///     Уничтожит все активные иконки
    /// </summary>
    public void DisposeAllNotifyIcons();

    /// <summary>
    ///     Были ли созданы иконки
    /// </summary>
    public bool IsIconsCreated
    {
        get; 
        set;
    }
    
    
    /// <summary>
    ///     Были ли обновлены иконки
    /// </summary>
    public bool IsIconsUpdated
    {
        get;
        set;
    }
}