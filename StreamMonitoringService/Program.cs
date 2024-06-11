using Amazon.SQS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace StreamMonitoringService
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var host = CreateHostBuilder(args).Build();
            await host.RunAsync();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton<StreamMonitoringService>();
                    services.AddHostedService<Worker>();
                    services.Configure<AppSettings>(context.Configuration.GetSection("AppSettings"));
                    services.AddAWSService<IAmazonSQS>();
                });
    }
}