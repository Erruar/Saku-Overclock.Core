namespace Saku_Overclock.Core.Contracts;

public interface IFileService
{
    T? Read<T>(string folderPath, string fileName);

    void Save<T>(string folderPath, string fileName, T content);
}