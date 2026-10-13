using Blazor.WinOld;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using TrailFtms;
using TrailFtms.Services.Bluetooth;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddWinOldComponents();
builder.Services.AddLocalization();
builder.Services.AddScoped<IFtmsTreadmill, FtmsTreadmill>();

await builder.Build().RunAsync();
