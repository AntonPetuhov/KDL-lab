using System.Reflection;
using AnalyzerService.Contracts;

namespace AnalyzerService.Host.Drivers;

/// <summary>
/// Загружает одну реализацию IAnalyzerDriver из указанной DLL.
/// </summary>
public class DriverLoader
{
    /// <summary>
    /// Reflection. Синхронно загружает сборку и создаёт драйвер; 
    /// </summary>
    public LoadedDriver Load(string configuredPath)
    {
        // проверка, существует ли dll файл по заданному пути
        string assemblyPath = Path.IsPathRooted(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.GetFullPath(configuredPath, AppContext.BaseDirectory);

        if (!File.Exists(assemblyPath)) throw new FileNotFoundException("DLL файл драйвера не найден.", assemblyPath);

        // Создаём изолированный контекст
        DriverLoadContext context = new(assemblyPath);
        try
        {
            // Загружаем сборку в контекст
            Assembly assembly = context.LoadFromAssemblyPath(assemblyPath);

            // Можно найти все тип
            //Type[] types = assembly.GetTypes().Where(t => !t.IsAbstract && !t.IsInterface && typeof(IAnalyzerDriver).IsAssignableFrom(t)).ToArray();
            //if (types.Length != 1) throw new InvalidOperationException($"Ожидалась одна реализация IAnalyzerDriver, найдено: {types.Length}.");
            //var driver = (IAnalyzerDriver?)Activator.CreateInstance(types[0]) ?? throw new InvalidOperationException("Не удалось создать драйвер.");

            // Ищем класс, реализующий IAnalyzerDriver, не интерфейс и не абстрактный класс
            var driverType = assembly.GetTypes().FirstOrDefault(t => typeof(IAnalyzerDriver).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

            if (driverType is null) 
                throw new InvalidOperationException($"Не найден тип, реализующий интерфейс IAnalyzerDriver в сборке {assemblyPath}");

            // Создаём экземпляр драйвера (вызов конструктора без параметров)
            var driverInstance = (IAnalyzerDriver?)Activator.CreateInstance(driverType) ?? throw new InvalidOperationException("Не удалось создать драйвер.");

            return new LoadedDriver(driverInstance, context);
        }
        catch
        {
            context.Unload();
            throw;
        }
    }
}
