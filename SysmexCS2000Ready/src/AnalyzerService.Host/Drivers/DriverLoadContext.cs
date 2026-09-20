using System.Reflection;
using System.Runtime.Loader;
using AnalyzerService.Contracts;

namespace AnalyzerService.Host.Drivers;

/// <summary>
/// Загружает зависимости драйвера в выгружаемый контекст, но разделяет
/// сборку контрактов с default context для сохранения совместимости типов.
/// </summary>
public sealed class DriverLoadContext(string mainAssemblyPath) : AssemblyLoadContext(isCollectible: true)
{
    private readonly AssemblyDependencyResolver resolver = new(mainAssemblyPath);

    /// <summary>Синхронно разрешает managed-зависимость DLL.</summary><param name="assemblyName">Имя сборки.</param><returns>Сборка или null для default context.</returns>
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name == typeof(IAnalyzerDriver).Assembly.GetName().Name) return null;
        string? path = resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    /// <summary>Синхронно разрешает нативную зависимость рядом с драйвером.</summary><param name="unmanagedDllName">Имя библиотеки.</param><returns>Дескриптор или zero.</returns>
    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        string? path = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? nint.Zero : LoadUnmanagedDllFromPath(path);
    }
}
