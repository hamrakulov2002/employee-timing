using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using ZKTECO_LIVE20R.Services;
using ZKTECO_LIVE20R.Web;

namespace ZKTECO_LIVE20R
{
    public partial class App : Application
    {
        private IHost? _webHost;

        private static readonly string CrashLogPath =
            Path.Combine(AppContext.BaseDirectory, "crash.log");

        private static void Log(string source, object ex)
        {
            try { File.AppendAllText(CrashLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex}\n\n"); }
            catch { /* лог не должен ронять приложение */ }
        }

        public App()
        {
            // Исключения в UI-потоке
            DispatcherUnhandledException += (s, e) =>
            {
                Log("UI", e.Exception);
                e.Handled = true;
            };

            // Исключения в фоновых потоках (в т.ч. async void вне UI)
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                Log("Domain", e.ExceptionObject);

            // Необработанные исключения в Task
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Log("Task", e.Exception);
                e.SetObserved();
            };
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                // Единственный запуск Telegram-бота (из MainViewModel вызов убрать!)
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
            catch (Exception ex)
            {
                Log("Startup", ex);
                MessageBox.Show("Не удалось запустить веб-сервер:\n" + ex.Message,
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                TelegramService.StopListening();

                if (_webHost != null)
                {
                    _webHost.StopAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                    _webHost.Dispose();
                }
            }
            catch (Exception ex)
            {
                Log("Exit", ex);
            }

            base.OnExit(e);
        }
    }
}