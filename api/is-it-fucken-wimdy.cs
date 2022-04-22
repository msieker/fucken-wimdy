using System;
using System.IO;
using System.Threading.Tasks;
using MaxMind.GeoIP2;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace MattSieker.FuckenWimdy
{
    public record FuckenWimdyResponse(string IpAddress, GeolocateData Geolocation);

    public record GeolocateData(bool Success, string Message, string Country, string State, string City, double Latitude, double Longitude);

    public static class IsItFuckenWimdy
    {
        private static GeolocateData Geolocate(string ipAddress, string baseDir)
        {
            try
            {
                using var reader = new DatabaseReader(Path.Combine(baseDir, "GeoLite2-City.mmdb"));

                var loc = reader.City(ipAddress);

                return new GeolocateData(true, "", loc.Country.IsoCode, loc.MostSpecificSubdivision.Name, loc.City.Name, loc.Location.Latitude ?? 0, loc.Location.Longitude ?? 0);
            }
            catch (Exception ex)
            {
                return new GeolocateData(false, ex.Message, "", "", "", 0, 0);
            }
        }

        [FunctionName("is_it_fucken_wimdy")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            ILogger log,
            ExecutionContext context)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");
            var locationIp = req.HttpContext.Connection.RemoteIpAddress.ToString();

            if (req.Query.TryGetValue("remoteIp", out var v))
            {
                locationIp = v.ToString();
            }
            
            var loc = Geolocate(locationIp, context.FunctionAppDirectory);
            log.LogInformation("Ip Geolocation {ip} -> {locInfo}", locationIp, loc);
            var response = new FuckenWimdyResponse(locationIp, loc);
            return new OkObjectResult(response);
        }
    }
}
