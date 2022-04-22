using System.IO;
using MaxMind.GeoIP2;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
[assembly: FunctionsStartup(typeof(api.Startup))]
namespace api
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            var rootPath = builder.GetContext().ApplicationRootPath;
            builder.Services.AddSingleton<DatabaseReader>(sp => new DatabaseReader(Path.Combine(rootPath, "GeoLite2-City.mmdb")));
            builder.Services.AddHttpClient();
            builder.Services.AddApplicationInsightsTelemetry();
        }
    }
}
