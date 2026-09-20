using System.Reflection;
using AnalyzerService.Contracts;

namespace AnalyzerService.Host.Drivers;

/// <summary>Загружает ровно одну реализацию IAnalyzerDriver из указанной DLL.</summary>
public sealed class DriverLoader
{
    /// <summary>
    /// Синхронно загружает сборку и создаёт драйвер; reflection не требует async.
    /// </summary>
    /// <param name="configuredPath">Абсолютный либо относительный путь из JSON.</param>
    /// <returns>Объект владения драйвером.</returns>
    /// <exception cref="FileNotFoundException">DLL отсутствует.</exception>
    /// <exception cref="InvalidOperationException">Реализация отсутствует или неоднозначна.</exception>
    public LoadedDriver Load(string configuredPath)
    {
        string path = Path.IsPathRooted(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.GetFullPath(configuredPath, AppContext.BaseDirectory);
        if (!File.Exists(path)) throw new FileNotFoundException("DLL драйвера не найдена.", path);

        DriverLoadContext context = new(path);
        try
        {
            Assembly assembly = context.LoadFromAssemblyPath(path);
            Type[] types = assembly.GetTypes().Where(t => !t.IsAbstract && !t.IsInterface && typeof(IAnalyzerDriver).IsAssignableFrom(t)).ToArray();
            if (types.Length != 1) throw new InvalidOperationException($"Ожидалась одна реализация IAnalyzerDriver, найдено: {types.Length}.");
            var driver = (IAnalyzerDriver?)Activator.CreateInstance(types[0]) ?? throw new InvalidOperationException("Не удалось создать драйвер.");
            return new LoadedDriver(driver, context);
        }
        catch
        {
            context.Unload();
            throw;
        }
    }
}
