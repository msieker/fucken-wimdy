using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using MaxMind.GeoIP2;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace MattSieker.FuckenWimdy
{
    public record FuckenWimdyResponse(string IpAddress, GeolocateData Geolocation, ForecastData forecast);
    public record GeolocateData(bool Success, string Message, string Country, string State, string City, double Latitude, double Longitude);
    public record ForecastData(bool success, string message, string gridId, string city, string state, DateTimeOffset forecastTime, double windDirection, double windSpeed, double windGust);

    record LocationGeomertry(string type, double[] coordinates);
    record LocationProperties(string city, string state);
    record RelativeLocation(string type, LocationGeomertry geometry, LocationProperties properties);

    record PointsProperties(string cwa, string gridId, string forecastGridData, RelativeLocation relativeLocation);

    record PointsResponse(string id, string type, PointsProperties properties);

    record GridMeasurementValue(string validTime, double value);
    record GridMeasurement(string uom, GridMeasurementValue[] values);

    record GridProperties(DateTimeOffset updateTime, string gridId, string gridX, string gridY, GridMeasurement windDirection, GridMeasurement windSpeed, GridMeasurement windGust);

    record GridResponse(string id, string type, GridProperties properties);



    public class IsItFuckenWimdy
    {
        private readonly HttpClient _httpClient;
        public IsItFuckenWimdy(HttpClient httpClient)
        {
            _httpClient = httpClient;

            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("isitfuckenwimdy.com", "1.0"));
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(Matt Sieker <matt@siekernet.com>)"));
        }

        private static string GetIpAddress(HttpRequest req)
        {
            IPAddress result = null;
            if (req.Headers.TryGetValue("X-Forwarded-For", out StringValues values))
            {
                var ipn = values.First().Split(new char[] { ',' }).FirstOrDefault()?.Split(new char[] { ':' }).FirstOrDefault();
                IPAddress.TryParse(ipn, out result);
            }
            result ??= req.HttpContext.Connection.RemoteIpAddress;
            return result.ToString();
        }

        private async Task<ForecastData> GetForecast(double latitude, double longitude)
        {
            try
            {

                var pointResponseStream = await _httpClient.GetStreamAsync($"https://api.weather.gov/points/{latitude:F4},{longitude:F4}");
                var pointResponse = await JsonSerializer.DeserializeAsync<PointsResponse>(pointResponseStream);
                var gridResponseStream = await _httpClient.GetStreamAsync(pointResponse.properties.forecastGridData);
                var gridResponse = await JsonSerializer.DeserializeAsync<GridResponse>(gridResponseStream);

                return new ForecastData(true, "", 
                    pointResponse.properties.gridId, 
                    pointResponse.properties.relativeLocation.properties.city, pointResponse.properties.relativeLocation.properties.state,
                    gridResponse.properties.updateTime,
                    gridResponse.properties.windDirection.values.Last().value,
                    gridResponse.properties.windSpeed.values.Last().value,
                    gridResponse.properties.windGust.values.Last().value);
            }
            catch (Exception ex)
            {
                return new ForecastData(false, ex.Message, "", "", "", DateTimeOffset.Now, 0, 0, 0);
            }
        }

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
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            ILogger log,
            ExecutionContext context)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");
            var locationIp = GetIpAddress(req);

            if (req.Query.TryGetValue("remoteIp", out var v))
            {
                locationIp = v.ToString();
            }

            var loc = Geolocate(locationIp, context.FunctionAppDirectory);
            log.LogInformation("Ip Geolocation {ip} -> {locInfo}", locationIp, loc);

            var latitude = loc.Latitude;
            var longitude = loc.Longitude;

            if (req.Query.TryGetValue("lat", out var lat))
            {
                _ = double.TryParse(lat.ToString(), out latitude);
            }

            if (req.Query.TryGetValue("lng", out var lon))
            {
                _ = double.TryParse(lon.ToString(), out longitude);
            }

            ForecastData forecast;
            if (latitude != 0 && longitude != 0)
            {
                forecast = await GetForecast(latitude, longitude);
            }
            else
            {
                forecast = new ForecastData(false, "No location data", "", "", "", DateTimeOffset.Now, 0, 0, 0);
            }

            var response = new FuckenWimdyResponse(locationIp, loc, forecast);
            return new OkObjectResult(response);
        }
    }
}
