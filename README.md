# SpritMonitor

Monitors German gas station prices via the [Tankerkönig API](https://creativecommons.tankerkoenig.de/) and sends a push notification when prices drop below a configured threshold.

## Features

- Fetches nearby gas station prices (E5, E10, Diesel)
- Sends push notifications via ntfy.sh when prices are cheap
- Logs all prices to a MySQL database for further visualization
- Address geocoding via Nominatim/OpenStreetMap

## Configuration


```csharp
var config = new Config
{
    ApiKey               = "your-api-key",
    Address              = "Your Street, City",
    RadiusKm             = 10,
    FuelType             = FuelType.E5,
    PriceThreshold       = 1.90,
    CheckIntervalMinutes = 30,
    MaxNotifications     = 5
};
```

MySQL connection string

```csharp
private const string ConnStr = "Server=localhost;Database=prices;User ID=root;Password=yourpassword;";
```

## Push Notifications

Notifications are sent via [ntfy.sh](https://ntfy.sh).

<img width="367" height="411" alt="grafik" src="https://github.com/user-attachments/assets/395b321f-075d-4eed-9d3d-1621a824f0cf" />


## External APIs

- **Tankerkönig** — gas prices (free API key required)
- **Nominatim/OpenStreetMap** — address geocoding (no key required)
