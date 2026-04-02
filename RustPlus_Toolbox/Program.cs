using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace RustPlus_Toolbox
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                "logs/app-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level}] {Message}{NewLine}{Exception}"
            )
            .CreateLogger();

            try
            {
                ApplicationConfiguration.Initialize();

                var host = Host.CreateDefaultBuilder()
                .UseSerilog()
                .ConfigureServices(services =>
                {
                    services.AddTransient<MainWindow>();
                })
                .Build();

                var form = host.Services.GetRequiredService<MainWindow>();

                Application.Run(form);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application crashed");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}