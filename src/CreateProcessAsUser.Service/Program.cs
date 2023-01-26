using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Logging.EventLog;
using CreateProcessAsUser.Service;

#if !DEBUG || false
if (Environment.UserInteractive)
{
    Console.WriteLine("Cannot start setvice from the command line or debugger."
        + " A Windows service must first be installed (using sc.exe) and then started with the ServerExplorer,"
        + " Windows Services Administrative tool or the NET START commmand.");
    Environment.Exit(-1);
}
#endif

using IHost host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options =>
    {
        options.ServiceName = "CreateProcessAsUser.Service";
    })
    .ConfigureServices(services =>
    {
        LoggerProviderOptions.RegisterProviderOptions<EventLogSettings, EventLogLoggerProvider>(services);
        services.AddHostedService<WindowsBackgroundService>();
    })
    .ConfigureLogging((context, logging) =>
    {
        //See: https://github.com/dotnet/runtime/issues/47303
        logging.AddConfiguration(
        context.Configuration.GetSection("Logging"));
    })
    .Build();

await host.RunAsync();
