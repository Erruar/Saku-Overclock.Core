using Saku_Overclock.Shared.Models;

namespace Saku_Overclock.Core.Contracts;

public interface IPresetManagerService
{
    /// <summary>
    ///     Загружает настройки и обработчики
    /// </summary>
    void RegisterIpcHandlers();
    
    /// <summary>
    ///     Коллекция пресетов
    /// </summary>
    Preset[] Presets
    {
        get;
        set;
    }

    /// <summary>
    ///     Загрузить пресеты
    /// </summary>
    void LoadSettings();

    /// <summary>
    ///     Сохранить пресеты
    /// </summary>
    void SaveSettings();

    /// <summary>
    ///     Добавить новый пресет
    /// </summary>
    void AddPresetInternal(Preset preset);

    /// <summary>
    ///     Удалить пресет по индексу
    /// </summary>
    void RemovePresetInternal(int index);

    /// <summary>
    ///     Удалить несколько пресетов по индексам
    /// </summary>
    void RemovePresetsInternal(int[]? indices);

    /// <summary>
    ///     Обновить существующий пресет
    /// </summary>
    bool UpdatePresetInternal(int index, Preset preset);

    /// <summary>
    ///     Выдаст информацию о следующем кастомном пресете (используется в горячих клавишах)
    /// </summary>
    /// <returns>Конфигурация следующего кастомного пресета</returns>
    PresetId GetNextPreset();
    
    /// <summary>
    ///     Удалить виртуальное состояние применённого пресета после применения горячими клавишами
    /// </summary>
    void ResetPresetStateAfterApply();
}