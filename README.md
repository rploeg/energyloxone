# LoxoneSolarForecast

A **production-ready, self-hosted Solar Energy Intelligence platform** for [Loxone](https://www.loxone.com/) home automation systems. Built with ASP.NET Core 9, designed to run as a Docker container in Portainer.

---

## Features

| Feature | Description |
|---|---|
| **Solar Forecast** | Hourly & 7-day production forecasting via Open-Meteo API |
| **Learning Engine** | Self-adapts correction factor from actual vs forecast data |
| **Shading Detection** | Detects recurring shading patterns from history |
| **Loxone Integration** | Pull data from Loxone & push forecasts to Virtual HTTP Inputs |
| **Energy Optimization** | EV charging, heat pump & appliance recommendations |
| **Confidence Score** | 0–100% per-day confidence metric |
| **Dashboard** | Real-time production, consumption, battery & grid status |
| **History** | Daily/monthly/yearly production charts and tables |
| **Monitoring** | Scheduler status, connection health, system statistics |
| **Logs** | Filterable Serilog logs stored in files |
| **REST API** | `/api/status`, `/api/forecast`, `/api/history`, `/api/recommendations` |
| **Docker** | Portainer-ready with persistent `/config` volume |

---

## Quick Start

### Docker Compose (Recommended)

```bash
# Create data directory for persistence
mkdir -p data

# Build and start
docker compose up -d --build

# Access at http://localhost:5000
# Default login: admin / solar123
```

### Local Development

```bash
# Requires .NET 9 SDK
mkdir -p data
dotnet run
# Access at http://localhost:5000
```

---

## Configuration

All settings are persisted in `/config/settings.json` inside the container (mapped to `./data/`).

### First-Run Setup

1. Open **Settings → Configuration** — configure Loxone IP, credentials, update interval
2. Open **Settings → Solar Arrays** — add your PV arrays with orientation and power
3. Open **Settings → Location** — click your panel location on the map
4. The forecast engine starts automatically and generates its first forecast

### Environment Variables

| Variable | Default | Description |
|---|---|---|
| `Auth__Username` | `admin` | Login username |
| `Auth__Password` | `solar123` | Login password — **change this!** |
| `Storage__ConfigPath` | `/config` | Persistent storage path |
| `ASPNETCORE_ENVIRONMENT` | `Production` | `Production` or `Development` |

### Portainer Deployment

1. In Portainer → Stacks → Add Stack
2. Paste the contents of `docker-compose.yml`
3. Set environment variables for `Auth__Password`
4. Deploy

---

## Architecture

```
LoxoneSolarForecast/
├── Controllers/
│   └── ApiController.cs          # REST API endpoints
├── Data/
│   └── AppDbContext.cs           # EF Core DbContext (SQLite)
├── Models/
│   ├── Configuration/            # AppConfiguration, SolarArray, etc.
│   ├── Entities/                 # EF entities (history, forecasts, logs)
│   └── ViewModels/               # Page view models
├── Services/
│   ├── ConfigurationService.cs   # JSON config persistence
│   ├── WeatherService.cs         # Open-Meteo API client
│   ├── ForecastService.cs        # Solar production calculation
│   ├── LearningService.cs        # Adaptive correction engine
│   ├── LoxoneService.cs          # Loxone HTTP API integration
│   ├── RecommendationService.cs  # Energy optimization engine
│   ├── DashboardService.cs       # Dashboard data aggregator
│   └── HistoryService.cs         # History + monitoring queries
├── Workers/
│   ├── DataCollectionWorker.cs   # Background: collect from Loxone every N minutes
│   ├── ForecastWorker.cs         # Background: generate forecasts every hour
│   └── LoxonePushWorker.cs       # Background: push values to Loxone
├── Pages/
│   ├── Index.cshtml              # Dashboard
│   ├── Forecast.cshtml           # 7-day forecast + charts
│   ├── Recommendations.cshtml    # Energy optimization
│   ├── History.cshtml            # Production history
│   ├── Monitoring.cshtml         # System health
│   ├── Logs.cshtml               # Application logs
│   ├── Configuration.cshtml      # App + Loxone settings
│   ├── SolarArrays.cshtml        # PV array configuration
│   ├── Location.cshtml           # Leaflet map location picker
│   └── Login.cshtml              # Authentication
├── Dockerfile
├── docker-compose.yml
└── Program.cs
```

---

## REST API

| Endpoint | Description |
|---|---|
| `GET /api/status` | Current production, consumption, battery, connection status |
| `GET /api/forecast` | Full 7-day hourly forecast |
| `GET /api/forecast/today` | Today's forecast + remaining |
| `GET /api/forecast/tomorrow` | Tomorrow's forecast |
| `GET /api/history?period=week` | Historical production (`today/week/month/year`) |
| `GET /api/recommendations` | Energy optimization recommendations |
| `GET /api/health` | Health check (used by Docker) |
| `GET /health` | ASP.NET health check endpoint |

---

## Loxone Integration

### Mode 1 — Pull (Loxone reads from this app)

Loxone can poll the REST API endpoints above using a **Virtual HTTP Input**.

### Mode 2 — Push (this app writes to Loxone)

Enable in Configuration → **Enable Push to Loxone**. The app will push these values to Loxone Virtual HTTP Inputs every update cycle:

| Key | Description | Unit |
|---|---|---|
| `SolarForecastToday` | Today's total forecast | Wh |
| `SolarForecastTomorrow` | Tomorrow's total forecast | Wh |
| `SolarRemainingToday` | Remaining production today | Wh |
| `SolarConfidence` | Forecast confidence | % |
| `SolarPeakTime` | Expected peak production time | HH:mm |
| `SolarPeakPower` | Expected peak power | W |
| `SolarExpectedSurplus` | Expected surplus energy | Wh |

Default URL format: `http://{loxone}/dev/sps/io/SolarForecastToday/{value}`

---

## Persistent Storage

All data survives container updates via the `/config` volume:

| File/Dir | Contents |
|---|---|
| `/config/settings.json` | Application configuration |
| `/config/solar.db` | SQLite database (all history, forecasts, learning data) |
| `/config/logs/` | Rolling log files (30-day retention) |
| `/config/data-protection-keys/` | ASP.NET Data Protection keys |

---

## Security

- Cookie-based authentication (change default password!)
- Credentials configurable via environment variables (Portainer secrets)
- Data Protection keys persisted across restarts
- Anti-forgery tokens on all forms
- Non-root Docker container user
- Input validation on all API endpoints

---

## Tech Stack

- **ASP.NET Core 9** — Web framework
- **Razor Pages** — Server-side UI
- **Entity Framework Core 9 + SQLite** — Data persistence
- **Serilog** — Structured logging (file)
- **Bootstrap 5** — UI framework
- **Chart.js** — Production/forecast charts
- **Leaflet + OpenStreetMap** — Interactive location picker
- **Open-Meteo API** — Free weather & solar radiation data
- **Docker** — Container deployment

---

## License

MIT
