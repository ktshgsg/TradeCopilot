using TradeCopilot.Service;
using Microsoft.Extensions.Hosting.WindowsServices;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "TradeCopilot";
});
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
