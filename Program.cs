using MySqlConnector;
using System.Collections.Generic;
using System.Text.Json;

var config = new Config
{
    ApiKey               = "", //Tankerkönig
    Address              = "Lübbecker Str. 2, Minden",
    RadiusKm             = 10,
    FuelType             = FuelType.E5,
    PriceThreshold       = 1.93,
    CheckIntervalMinutes = 30,
    MaxNotifications     = 5
};

// Nominatim API - Geocoding 
HttpClient geoHttp = new();
geoHttp.DefaultRequestHeaders.Add("User-Agent", "TankstellenMonitor/1.0");
JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
string? geoJson = await geoHttp.GetStringAsync($"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(config.Address)}&format=json&limit=1&countrycodes=de");
List<NominatimResult>? geoResult = JsonSerializer.Deserialize<List<NominatimResult>>(geoJson, Options);
if (geoResult is null || geoResult.Count == 0) 
{ 
    Console.WriteLine($"Address not found: {config.Address}"); 
    return; 
}
config.Latitude  = double.Parse(geoResult[0].Lat, System.Globalization.CultureInfo.InvariantCulture);
config.Longitude = double.Parse(geoResult[0].Lon, System.Globalization.CultureInfo.InvariantCulture);

await new FuelPriceMonitor { _cfg = config }.Run();

// Structures and classes
public enum FuelType { E5, E10, Diesel }

public class Config
{
    public string   ApiKey               { get; set; }
    public string   Address              { get; set; }
    public double   Latitude             { get; set; }
    public double   Longitude            { get; set; }
    public int      RadiusKm             { get; set; }
    public FuelType FuelType             { get; set; }
    public double   PriceThreshold       { get; set; }
    public int      CheckIntervalMinutes { get; set; }
    public int      MaxNotifications     { get; set; }
}

public record NominatimResult(string Lat, string Lon);
public record StationListResponse(bool Ok, string? Message, List<Station>? Stations);

public record Station(
    string Id, string Name, string Brand,
    string Street, string HouseNumber, string Place,
    bool IsOpen, double? Price, double? E5, double? E10, double? Diesel,
    double Lat, double Lng, double Dist);

// Main monitoring class
public class FuelPriceMonitor
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    private const string ConnStr = "Server=localhost;Database=prices;User ID=root;Password=1234;";
    private const string ApiBase = "https://creativecommons.tankerkoenig.de/json";

    public required Config _cfg;
    private readonly HttpClient _http = new() { DefaultRequestHeaders = { { "User-Agent", "TankstellenMonitor/1.0" } } };
    private readonly Dictionary<string, double> _notified = [];

    public async Task Run(CancellationToken cancellationtoken = default)
    {
        Console.WriteLine($"Monitoring {_cfg.FuelType} | {_cfg.RadiusKm}km | Threshold {_cfg.PriceThreshold:F3} €/L | Interval {_cfg.CheckIntervalMinutes} min");
        Console.WriteLine("Press CTRL+C to exit.\n");
        await InitDB();

        while (!cancellationtoken.IsCancellationRequested)
        {
            await CheckPrices();
            await Task.Delay(TimeSpan.FromMinutes(_cfg.CheckIntervalMinutes), cancellationtoken);
        }
    }

    private async Task CheckPrices()
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Checking prices...");
        try
        {
            string fuel = _cfg.FuelType switch { FuelType.E5 => "e5", FuelType.E10 => "e10", _ => "diesel" };
            string? apiJson    = await _http.GetStringAsync($"{ApiBase}/list.php?lat={_cfg.Latitude}&lng={_cfg.Longitude}&rad={_cfg.RadiusKm}&sort=price&type={fuel}&apikey={_cfg.ApiKey}");
            StationListResponse? apiResult  = JsonSerializer.Deserialize<StationListResponse>(apiJson, Options);
            if (apiResult is null || !apiResult.Ok) 
                throw new Exception(apiResult?.Message ?? "API-Error");
            
            var stations = apiResult.Stations ?? [];
            if (stations.Count == 0) 
            { 
                Console.WriteLine("  No gas stations found."); 
                return; 
            }

            List<(Station s, double p)> cheap = [];
            List<(Station s, double p)> rest   = [];

            foreach (Station? station in stations)
            {
                double? price = station.Price ?? _cfg.FuelType switch { FuelType.E5 => station.E5, FuelType.E10 => station.E10, _ => station.Diesel };
                if (price is null) 
                    continue;
                rest.Add((station, price.Value));
                
                string? marker = price < _cfg.PriceThreshold ? "  CHEAP!" : "";
                Console.WriteLine($"  {station.Name,-35} {price:F3} €/L  ({station.Dist:F1} km){marker}");
                if (price < _cfg.PriceThreshold)
                {
                    if (!_notified.TryGetValue(station.Id, out double last) || last != price.Value)
                    { 
                        _notified[station.Id] = price.Value; cheap.Add((station, price.Value)); 
                    }
                }
                else _notified.Remove(station.Id);
            }

            await LogPrices(rest);
            Console.WriteLine($"  {rest.Count} Saved in DB.");

            if (cheap.Count > 0) 
                await NotifyPhone(cheap);
            else if (!rest.Any(e => e.p < _cfg.PriceThreshold))
                Console.WriteLine("  No stations under the threshold");
        }
        catch (Exception ex) { Console.WriteLine($"  Fehler: {ex.Message}"); }
    }

    private async Task NotifyPhone(List<(Station s, double p)> entries)
    {
        List<(Station s, double p)> entriesTop  = [.. entries.Take(_cfg.MaxNotifications)];
        string? body = string.Join("\n", entriesTop.SelectMany((e, i) => new[]
        {
            $"{i + 1}. {e.s.Name}",
            $"   {e.p:F3} €/L  |  {e.s.Dist:F1} km",
            $"   {e.s.Street} {e.s.HouseNumber}, {e.s.Place}",
            ""
        })).TrimEnd();

        string countString = entries.Count > entriesTop.Count ? $"Top {entriesTop.Count} von {entries.Count}" : $"{entries.Count}";
        var request = new HttpRequestMessage(HttpMethod.Post, "https://ntfy.sh/Tankstellen") { Content = new StringContent(body) };
        request.Headers.Add("Title",    $"Gunstiger Sprit - {countString} Tankstelle(n)");
        request.Headers.Add("Priority", "high");
        request.Headers.Add("Tags",     "fuelpump");
        try
        {
            var response = await _http.SendAsync(request);
            Console.WriteLine($"  ntfy gesendet ({entries.Count} Tankstellen) – HTTP {(int)response.StatusCode}.");
        }
        catch (Exception ex) 
        { 
            Console.WriteLine($"  ntfy-Fehler: {ex.Message}"); 
        }
    }

    private async Task InitDB()
    {
        MySqlConnection connection = new(ConnStr);
        await connection.OpenAsync();
        MySqlCommand? command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS price_history (
                id           INT AUTO_INCREMENT PRIMARY KEY,
                timestamp    DATETIME NOT NULL,
                station_id   VARCHAR(100) NOT NULL,
                station_name VARCHAR(255) NOT NULL,
                brand        VARCHAR(100), place VARCHAR(255), distance_km DOUBLE,
                price        DOUBLE NOT NULL, fuel_type VARCHAR(10) NOT NULL)";
        await command.ExecuteNonQueryAsync();
        Console.WriteLine("  DB initialisiert (MySQL: prices.price_history)");
    }

    private async Task LogPrices(IEnumerable<(Station s, double p)> entries)
    {
        MySqlConnection connection = new(ConnStr);
        await connection.OpenAsync();
        MySqlTransaction? transaction = await connection.BeginTransactionAsync();
        foreach (var (station, price) in entries)
        {
            MySqlCommand? command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO price_history (timestamp, station_id, station_name, brand, place, distance_km, price, fuel_type) VALUES (@ts, @sid, @sname, @brand, @place, @dist, @price, @ft)";
            command.Parameters.AddWithValue("@ts",    DateTime.Now);
            command.Parameters.AddWithValue("@sid",   station.Id);
            command.Parameters.AddWithValue("@sname", station.Name);
            command.Parameters.AddWithValue("@brand", station.Brand);
            command.Parameters.AddWithValue("@place", station.Place);
            command.Parameters.AddWithValue("@dist",  station.Dist);
            command.Parameters.AddWithValue("@price", price);
            command.Parameters.AddWithValue("@ft",    _cfg.FuelType.ToString());
            await command.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }
}
