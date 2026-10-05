using AzureCost.Console.Ui;
using AzureCost.Core.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectre.Console;

// Host + DI. A WinUI 3 app would build its own host the same way and call AddAzureCost();
// the Core library is shared verbatim.
var builder = Host.CreateApplicationBuilder(args);

// Keep the console clean — only surface warnings/errors from the framework.
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.Services.AddAzureCost(options =>
{
    // Optional overrides via config / env (AZURECOST__TENANTID, AZURECOST__COSTMETRIC, ...).
    builder.Configuration.GetSection("AzureCost").Bind(options);
});

builder.Services.AddSingleton<ConsoleCostExplorer>();

using var host = builder.Build();

using var cts = new CancellationTokenSource();
System.Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    var explorer = host.Services.GetRequiredService<ConsoleCostExplorer>();
    await explorer.RunAsync(cts.Token);
}
catch (OperationCanceledException)
{
    AnsiConsole.MarkupLine("[grey]Cancelled.[/]");
}
catch (Exception ex)
{
    AnsiConsole.MarkupLine("[red]Unexpected error:[/]");
    AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
    return 1;
}

return 0;
