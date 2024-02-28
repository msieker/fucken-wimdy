using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MaxMind.GeoIP2;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System.Collections.Generic;

namespace MattSieker.FuckenWimdy
{
    public record GeolocateData(bool Success, string Message, string Country, string State, string City, double Latitude, double Longitude);

    record ClientResponse(bool Success, string Message, PlaceGeoName? Geo, OpenMeteoResponse? Weather);

    record PlaceGeoName(
        string AdminCode1,
        double Lng,
        double Lat,
        string Name,
        double Distance,
        string CountryCode
    )
    {
        public bool PrefersImperial => CountryCode == "US";
    };

    record PlaceResponse
    (
        IEnumerable<PlaceGeoName>? GeoNames
    );

    record OpenMeteoCurrentUnits
    {

    }

    record OpenMeteoCurrent
    {
        public DateTimeOffset Time { get; set; }
        public int Interval { get; set; }
        [JsonPropertyName("wind_speed_10m")]
        public double WindSpeed { get; set; }
        [JsonPropertyName("wind_direction_10m")]
        public double WindDirection { get; set; }
        [JsonPropertyName("wind_gusts_10m")]
        public double WindGusts { get; set; }
    }
    record OpenMeteoResponse
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }

        [JsonPropertyName("current_units")]
        public OpenMeteoCurrentUnits CurrentUnits { get; set; }

        public OpenMeteoCurrent Current { get; set; }
    }
    public class IsItFuckenWimdy
    {
        private readonly HttpClient _httpClient;
        private readonly TelemetryClient _telemetryClient;
        private readonly DatabaseReader _dbReader;

        public IsItFuckenWimdy(HttpClient httpClient, TelemetryConfiguration configuration, DatabaseReader dbReader)
        {
            _httpClient = httpClient;
            _dbReader = dbReader;
            _telemetryClient = new TelemetryClient(configuration);
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

        private GeolocateData Geolocate(string ipAddress, string baseDir)
        {
            using var operation = _telemetryClient.StartOperation<DependencyTelemetry>("Geolocation");
            try
            {
                var loc = _dbReader.City(ipAddress);

                return new GeolocateData(true, "", loc.Country.IsoCode, loc.MostSpecificSubdivision.Name, loc.City.Name, loc.Location.Latitude ?? 0, loc.Location.Longitude ?? 0);
            }
            catch (Exception ex)
            {
                return new GeolocateData(false, ex.Message, "", "", "", 0, 0);
            }
        }

        private async Task<ClientResponse> GetWeather(double lat, double lng)
        {
            var userName = System.Environment.GetEnvironmentVariable("GEONAMES_USER_NAME", EnvironmentVariableTarget.Process);
            if (lat == 0 && lng == 0)
            {
                return new ClientResponse(false, "No location data", null, null);
            }
            try
            {
                var weatherResponse = await _httpClient.GetFromJsonAsync<OpenMeteoResponse>($"https://api.open-meteo.com/v1/forecast?latitude={lat:F4}&longitude={lng:F4}&current=wind_speed_10m,wind_direction_10m,wind_gusts_10m");
                var placeResult = await _httpClient.GetFromJsonAsync<PlaceResponse>($"http://api.geonames.org/findNearbyPlaceNameJSON?formatted=true&lat={weatherResponse.Latitude}&lng={weatherResponse.Longitude}&username={userName}&cities=cities5000");

                return new ClientResponse(true, "", placeResult?.GeoNames?.FirstOrDefault(), weatherResponse);
            }
            catch (Exception ex)
            {
                return new ClientResponse(false, ex.Message, null, null);
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
            
            return new OkObjectResult(await GetWeather(latitude, longitude));
        }
    }
}
