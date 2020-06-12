using System;
using System.Net.Http;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebAppHosted.Client.Services;

namespace WebAppHosted.Client
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            builder.RootComponents.Add<App>("app");

            builder.Services.AddBlazoredLocalStorage();
            builder.Services.AddTransient(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
            builder.Services.AddOidcAuthentication(options =>
            {
                builder.Configuration.Bind("Local", options.ProviderOptions);
                options.ProviderOptions.ResponseType = "token id_token";
                options.ProviderOptions.DefaultScopes.Add("email https://www.googleapis.com/auth/drive.appdata");
            });
            builder.Services.AddSingleton<IStorageState, StorageState>();
            builder.Services.AddScoped<IRemoteStorage, GoogleDriveRemoteStorage>();
            builder.Services.AddScoped<IStorage, Storage>();

            await builder.Build().RunAsync();
        }
    }
}
