# Tankstellen Preis Monitor 🔔

Ein C# Konsolen-Programm, das dich benachrichtigt wenn der Spritpreis in deiner Nähe unter einen bestimmten Wert fällt.

## Voraussetzungen

- .NET 8 SDK
- Kostenloser API-Key von [Tankerkönig](https://creativecommons.tankerkoenig.de/)

## Setup

### 1. API-Key holen
→ https://creativecommons.tankerkoenig.de/ → Registrieren → API-Key erhalten

### 2. Konfiguration in `Program.cs` anpassen

```csharp
var config = new MonitorConfig
{
    ApiKey = "dein-api-key-hier",   // ← Deinen Key eintragen
    Latitude = 52.49718,             // ← Dein Breitengrad (z.B. von Google Maps)
    Longitude = 7.32897,             // ← Dein Längengrad
    RadiusKm = 5,                    // Umkreis in Kilometern
    FuelType = FuelType.E5,          // E5, E10 oder Diesel
    PriceThreshold = 1.75,           // Benachrichtigung wenn Preis UNTER diesem Wert
    CheckIntervalMinutes = 10        // Wie oft prüfen?
};
```

### 3. Starten
```bash
cd TankstellenMonitor
dotnet run
```

## Benachrichtigungsarten

Das Programm schreibt aktuell auf die Konsole. Du kannst weitere Benachrichtigungen einbauen:

### Windows Toast Notifications
NuGet installieren: `dotnet add package Microsoft.Toolkit.Uwp.Notifications`
Dann im Code kommentieren:
```csharp
new ToastContentBuilder()
    .AddText("Günstiger Sprit! 🔔")
    .AddText(message)
    .Show();
```

### E-Mail
```csharp
using var smtp = new SmtpClient("smtp.gmail.com", 587);
smtp.EnableSsl = true;
smtp.Credentials = new NetworkCredential("deine@email.de", "passwort");
await smtp.SendMailAsync("deine@email.de", "deine@email.de", "Günstiger Sprit!", message);
```

### Telegram Bot
```csharp
// Bot erstellen über @BotFather auf Telegram
string telegramUrl = $"https://api.telegram.org/bot{botToken}/sendMessage?chat_id={chatId}&text={Uri.EscapeDataString(message)}";
await _http.GetAsync(telegramUrl);
```

## API Details

Genutzte API: **Tankerkönig** (https://creativecommons.tankerkoenig.de/)
- Kostenlos und frei zugänglich (Creative Commons)
- Alle 14.000+ Tankstellen in Deutschland
- Daten direkt vom Bundeskartellamt (MTS-K)
- Kraftstoffe: Super E5, Super E10, Diesel

### Genutzte Endpoints

**Tankstellen in der Nähe suchen:**
```
GET /json/list.php?lat={lat}&lng={lng}&rad={km}&sort=price&type={e5|e10|diesel}&apikey={key}
```

**Nur Preise für bekannte Tankstellen:**
```
GET /json/prices.php?ids={id1,id2,...}&apikey={key}
```
