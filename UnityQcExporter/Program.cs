using UnityQcExporter;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});
builder.Services.AddWindowsService(options => options.ServiceName = "UnityQcExporter");
builder.Services.AddSingleton(ExportSettings.Load(Path.Combine(AppContext.BaseDirectory, "exporter.json")));
builder.Services.AddHostedService<ExportWorker>();
await builder.Build().RunAsync();
