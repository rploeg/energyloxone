# Multi-Source Energy & Resource Platform - Configuration & Grafana Guide

## Architecture Overview

Your application is now a **multi-source data aggregation platform** that collects:
- **Solar Energy Data** (Production, Consumption, Grid Import/Export) → Loxone
- **Water Usage** (Flow rate, Total consumption) → HomeWizard Watermeter
- Future: Electricity grid data, weather stations, battery systems, etc.

### Data Collection Flow
```
Loxone (API) → LoxoneService → InfluxDB (electricity measurements)
HomeWizard (API) → HomeWizardCollector → InfluxDB (water measurements)
         ↓
    Grafana Visualization
```

---

## Configuration in settings.json

### HomeWizard Watermeter Setup

```json
{
  "HomeWizard": {
    "Enabled": true,
    "IpAddress": "172.16.1.156",
    "UpdateIntervalMinutes": 5
  }
}
```

**Parameters:**
- `Enabled`: Set to `true` to activate water meter collection
- `IpAddress`: IP address of your HomeWizard watermeter (must be on same network)
- `UpdateIntervalMinutes`: How often to collect data (5 mins = 12 readings/hour)

### Data Collection Schedule (Independent Intervals)

Each data source runs independently:
- **Loxone**: Every 1-5 minutes (per LoxoneDataSource config)
- **HomeWizard**: Every X minutes (configured in HomeWizard.UpdateIntervalMinutes)
- **Forecasts**: Every 60 minutes (configurable in General.ForecastIntervalMinutes)

---

## InfluxDB Measurements

### Electricity Data (from Loxone)
```
Measurement: production
Fields: value (watts)
Tags: source=Loxone

Measurement: consumption
Fields: value (watts)
Tags: source=Loxone

Measurement: grid
Fields: export (watts), import (watts)
Tags: source=Loxone
```

### Water Data (from HomeWizard)
```
Measurement: water
Fields: value (liters or m³)
Tags: source=HomeWizard, type=flow OR type=consumption

Examples:
- water,source=HomeWizard,type=flow value=2.34       (L/min current flow)
- water,source=HomeWizard,type=consumption value=1234.56  (m³ total)
```

---

## Grafana Queries for Water Data

### 1. Current Water Flow (Real-time gauge)
```flux
from(bucket:"loxone")
  |> range(start: -5m)
  |> filter(fn: (r) => r._measurement == "water" and r._field == "value" and r.type == "flow")
  |> last()
  |> map(fn: (r) => ({value: r._value}))
```
**Display as:** Gauge (0-10 L/min)

---

### 2. Daily Water Consumption (kWh-style stat)
```flux
from(bucket:"loxone")
  |> range(start: -24h)
  |> filter(fn: (r) => r._measurement == "water" and r.type == "consumption")
  |> last()
```
**Display as:** Stat panel showing total m³ consumed today

---

### 3. Water Usage Trend (Line chart - last 7 days)
```flux
from(bucket:"loxone")
  |> range(start: -7d)
  |> filter(fn: (r) => r._measurement == "water" and r.type == "consumption")
  |> aggregateWindow(every: 1h, fn: last)
  |> map(fn: (r) => ({
    _time: r._time,
    consumption_m3: r._value
  }))
```
**Display as:** Time Series graph

---

### 4. Water Usage Spike Detection (Yesterday vs Today)
```flux
yesterday = from(bucket:"loxone")
  |> range(start: -2d, stop: -1d)
  |> filter(fn: (r) => r._measurement == "water" and r.type == "consumption")
  |> last()

today = from(bucket:"loxone")
  |> range(start: -1d)
  |> filter(fn: (r) => r._measurement == "water" and r.type == "consumption")
  |> last()

union(tables: [yesterday, today])
  |> map(fn: (r) => ({
    _time: r._time,
    value: r._value,
    day: if r._time < now() - 1d then "Yesterday" else "Today" end
  }))
```
**Display as:** Bar chart comparison

---

### 5. Energy vs Water Correlation (Advanced - 2 axis chart)
```flux
// Energy production (top axis)
production = from(bucket:"loxone")
  |> range(start: -24h)
  |> filter(fn: (r) => r._measurement == "production")
  |> aggregateWindow(every: 1h, fn: mean)
  |> map(fn: (r) => ({_time: r._time, energy_w: r._value, series: "Production"}))

// Water flow (bottom axis)
water = from(bucket:"loxone")
  |> range(start: -24h)
  |> filter(fn: (r) => r._measurement == "water" and r.type == "flow")
  |> aggregateWindow(every: 1h, fn: mean)
  |> map(fn: (r) => ({_time: r._time, flow_lpm: r._value, series: "Water Flow"}))

union(tables: [production, water])
```
**Display as:** Time Series with 2 Y-axes (Energy vs Water usage patterns)

---

### 6. Water Cost Calculator (if metered)
```flux
from(bucket:"loxone")
  |> range(start: -30d)
  |> filter(fn: (r) => r._measurement == "water" and r.type == "consumption")
  |> last()
  |> map(fn: (r) => ({
    consumption_m3: r._value,
    cost_eur: (r._value * 1.50),  // €1.50 per m³ - adjust to your rate
    unit: "€"
  }))
```
**Display as:** Stat showing monthly water cost

---

### 7. Hourly Water Usage Breakdown (Heatmap)
```flux
from(bucket:"loxone")
  |> range(start: -7d)
  |> filter(fn: (r) => r._measurement == "water" and r.type == "flow")
  |> aggregateWindow(every: 1h, fn: mean)
  |> map(fn: (r) => ({
    _time: r._time,
    hour: int(v: r._time) % 24,
    flow: r._value
  }))
```
**Display as:** Heatmap (shows which hours use most water)

---

## Complete Home Dashboard Configuration

Suggested layout combining energy + water:

### Row 1: Real-time Status
- ☀️ **Solar Production** (Gauge: kW)
- 🏠 **House Consumption** (Gauge: kW)
- 💧 **Water Flow** (Gauge: L/min)
- ⚡ **Grid Status** (Stat: Import/Export)

### Row 2: 24-Hour Trends
- Production vs Consumption (Time Series, 2 lines)
- Water Usage (Time Series)
- Grid Exchange (Area Chart)

### Row 3: Daily Totals
- 📊 Energy Production (kWh)
- 📊 Energy Consumption (kWh)
- 📊 Water Consumption (m³)
- 💰 Estimated Costs

### Row 4: Analytics
- Solar Forecast (Next 7 days)
- Water Usage Patterns (Heatmap by hour)
- Production/Consumption Balance (Pie)

---

## Monitoring & Health Dashboard

Check data collector status:

```flux
// Last collection timestamp for each source
from(bucket:"loxone")
  |> range(start: -1h)
  |> group(columns: ["source"])
  |> last()
  |> map(fn: (r) => ({
    source: r.source,
    last_reading: r._time,
    measurement: r._measurement
  }))
```

**Expected sources:** `Loxone`, `HomeWizard`

---

## Troubleshooting

### HomeWizard Data Not Appearing

1. **Check IP connectivity:**
   ```bash
   ping 172.16.1.156
   curl http://172.16.1.156/api/v1/data
   ```

2. **Verify settings.json:**
   ```json
   "HomeWizard": {
     "Enabled": true,
     "IpAddress": "172.16.1.156",
     "UpdateIntervalMinutes": 5
   }
   ```

3. **Check container logs:**
   ```bash
   docker logs loxone-solar-forecast | grep -i homewizard
   ```

4. **Expected log output:**
   ```
   HomeWizard collection scheduled every 5 minutes
   HomeWizard water data collected: X L/min, Y m³
   ```

### No Data in InfluxDB

Verify water measurements exist:
```flux
from(bucket:"loxone")
  |> range(start: -24h)
  |> filter(fn: (r) => r._measurement == "water")
  |> group(columns: ["source", "type"])
```

Should show:
- `source=HomeWizard, type=flow`
- `source=HomeWizard, type=consumption`

---

## Future Expansion

The architecture supports adding more sources:

### Gas Meter (P1 Protocol)
```csharp
public class GasCollector : IDataCollector
{
    public string SourceName => "Gas";
    public async Task CollectAsync() { ... }
}
```

### Heat Pump Energy
```csharp
public class HeatPumpCollector : IDataCollector { ... }
```

### Grid Stability Data
```csharp
public class GridAPICollector : IDataCollector { ... }
```

Just implement `IDataCollector`, register in `Program.cs`, and add config settings!

---

## Configuration Summary

**Key new app features:**
- ✅ Multi-source architecture (extensible to N sources)
- ✅ Per-source collection intervals
- ✅ Unified InfluxDB storage with source tagging
- ✅ HomeWizard watermeter integration
- ✅ Health monitoring per collector
- ✅ Configurable via settings.json

**Database:**
- New measurements: `water` (flow + consumption)
- Existing: `production`, `consumption`, `grid`, `forecast_*`
