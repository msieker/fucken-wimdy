using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MaxMind.GeoIP2;
using Microsoft.ApplicationInsights.Extensibility;
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

            builder.Services.AddSingleton<TelemetryConfiguration>(sp =>
            {
                var telemetryConfiguration = new TelemetryConfiguration();
                telemetryConfiguration.InstrumentationKey = Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY") ?? "";
                telemetryConfiguration.TelemetryInitializers.Add(new OperationCorrelationTelemetryInitializer());
                return telemetryConfiguration;
            });
        }
    }
}
