using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using PrintTrack.Agent;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(o => o.ServiceName = "PrintTrackAgent");

builder.Services.AddOptions<AgentOptions>()
    .Bind(builder.Configuration.GetSection(AgentOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.ServerUrl), "Agent:ServerUrl es obligatorio.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), "Agent:ApiKey es obligatorio.");

builder.Services.AddHttpClient<ServerClient>((sp, http) =>
{
    var opt = sp.GetRequiredService<IOptions<AgentOptions>>().Value;
    http.BaseAddress = new Uri(opt.ServerUrl);
    http.Timeout = TimeSpan.FromSeconds(Math.Max(5, opt.ServerTimeoutSeconds) + 5);
    http.DefaultRequestHeaders.Add("X-Api-Key", opt.ApiKey);
    http.DefaultRequestHeaders.Add("X-Workstation", Environment.MachineName);
    http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AuditorImpresiones",
        typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0"));
});

builder.Services.AddHostedService<PrintMonitorService>();

var host = builder.Build();
host.Run();

// exposed for assembly-version lookups above
public partial class Program;
