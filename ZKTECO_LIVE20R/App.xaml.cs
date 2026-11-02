using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using System.IO;
using System.Windows;
using ZKTECO_LIVE20R.Services;
using ZKTECO_LIVE20R.Web;

namespace ZKTECO_LIVE20R
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private IHost? _webHost;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            TelegramService.StartListening();


            _webHost = Host.CreateDefaultBuilder()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseContentRoot(Directory.GetCurrentDirectory());
                    webBuilder.UseUrls("http://0.0.0.0:5000");
                    webBuilder.UseStartup<WebStartup>();
                })
                .Build();
            await _webHost.StartAsync();
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            TelegramService.StopListening();
            if (_webHost != null)
            {
                await _webHost.StopAsync();
                _webHost.Dispose();
            }
            base.OnExit(e);
        }
    }

}
