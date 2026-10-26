using AnalyzerService.Host;
using AnalyzerService.Host.Configuration;
using AnalyzerService.Host.Logging;
using AnalyzerService.Host.Runtime;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "Analyzer Configuration Service");
builder.Services.AddSingleton<JsonAnalyzerSettingsProvider>();
builder.Services.AddSingleton<AnalyzerSettingsValidator>();
builder.Services.AddSingleton<AnalyzerLoggerFactory>();
builder.Services.AddSingleton<AnalyzerManager>();
builder.Services.AddHostedService<Worker>();
await builder.Build().RunAsync();
