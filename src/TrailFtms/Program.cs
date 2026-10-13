using Blazor.WinOld;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using TrailFtms;
using TrailFtms.Services.Bluetooth;
using TrailFtms.Services.Programs;
using TrailFtms.Services.Settings;
using TrailFtms.Services.Video;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddWinOldComponents();
builder.Services.AddLocalization();
builder.Services.AddScoped<IFtmsTreadmill, FtmsTreadmill>();
builder.Services.AddScoped<IVideoPlayer, YouTubePlayer>();
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<ProgramStore>();
builder.Services.AddScoped<SettingsStore>();

await builder.Build().RunAsync();
