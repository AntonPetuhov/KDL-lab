using System.Reflection;
using System.Runtime.Loader;

namespace AnalyzerService.Host.Drivers;

/// <summary>
/// класс изолированного контекста загрузки dll драйвера
/// </summary>
public class DriverLoadContext(string mainAssemblyPath) : AssemblyLoadContext(isCollectible: true)
{
    private readonly AssemblyDependencyResolver resolver = new(mainAssemblyPath); // позволяет изолировать зависимости каждого плагина в отдельном контексте загрузки (AssemblyLoadContext)

    // Переопределяем метод Load, чтобы разрешать зависимости сборок
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Только контракт принадлежит хосту; транспорт, БД и прочие зависимости
        // разрешаются из каталога конкретного драйвера.
        if (assemblyName.Name is "AnalyzerService.Contracts")
            return Default.LoadFromAssemblyName(assemblyName);

        // Пытаемся разрешить сборку через Resolver
        // принимает объект сборки AssemblyName
        // ищет соответствующий .dll
        // возвращает полный путь к найденному .dll
        string? assemblyPath = resolver.ResolveAssemblyToPath(assemblyName);
        return assemblyPath is null ? null : LoadFromAssemblyPath(assemblyPath);
    }

    /// <summary>Синхронно разрешает нативную зависимость рядом с драйвером.</summary><param name="unmanagedDllName">Имя библиотеки.</param><returns>Дескриптор или zero.</returns>
    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        string? path = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? nint.Zero : LoadUnmanagedDllFromPath(path);
    }
}
