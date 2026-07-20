# Quick Start: HomeWizard Watermeter Integration

## ✅ What's New

Your application is now a **multi-source data aggregation platform**:

- **Loxone** → Solar production, consumption, grid data
- **HomeWizard** → Water usage (flow + consumption) 
- **Future:** Gas, heat pump, grid APIs, weather stations, etc.

## 🚀 Enable HomeWizard Water Meter

### Step 1: Verify HomeWizard API Endpoint

Test connectivity from your server:

```bash
# SSH to your Portainer host
ssh remco@172.16.1.47

# Test HomeWizard API
curl http://172.16.1.156/api/v1/data

# Expected response:
# {"water":{"current_liter_per_minute":0.5,"total_liter_m3":1234.56}}
```

### Step 2: Update settings.json

Edit the config in the Docker volume:

```bash
# Access the config
docker exec -it loxone-solar-forecast bash
vi /config/settings.json
```

Change the HomeWizard section from:
```json
{
  "HomeWizard": {
    "Enabled": false,
    "IpAddress": "172.16.1.156",
    "UpdateIntervalMinutes": 5
  }
}
```

To:
```json
{
  "HomeWizard": {
    "Enabled": true,
    "IpAddress": "172.16.1.156",
    "UpdateIntervalMinutes": 5
  }
}
```

Save with `:wq` (vi editor)

### Step 3: Restart Container

```bash
docker restart loxone-solar-forecast
```

### Step 4: Verify in Logs

```bash
docker logs loxone-solar-forecast -f | grep -i homewizard
```

Expected output:
```
HomeWizard collection scheduled every 5 minutes
Collecting HomeWizard water data
HomeWizard water data collected: 0.5 L/min, 1234.56 m³
InfluxDB write success (NoContent): water,source=HomeWizard,type=flow value=0.5
InfluxDB write success (NoContent): water,source=HomeWizard,type=consumption value=1234.56
```

---

## 📊 Add Water Data to Grafana Dashboard

### Real-Time Water Flow (Gauge)

Go to Grafana → Explore → Run this query:

```flux
from(bucket:"loxone")
  |> range(start: -5m)
  |> filter(fn: (r) => r._measurement == "water" and r.type == "flow")
  |> last()
```

Display as: **Gauge** (Max: 10 L/min)

---

### Water Consumption Trend (24h)

```flux
from(bucket:"loxone")
  |> range(start: -24h)
  |> filter(fn: (r) => r._measurement == "water" and r.type == "consumption")
  |> aggregateWindow(every: 1h, fn: last)
```

Display as: **Time Series** graph

---

### Combined Energy & Water View

**Production vs Water Usage:**

```flux
production = from(bucket:"loxone")
  |> range(start: -24h)
  |> filter(fn: (r) => r._measurement == "production")
  |> aggregateWindow(every: 1h, fn: mean)
  |> map(fn: (r) => ({_time: r._time, value: r._value, series: "Production"}))

water = from(bucket:"loxone")
  |> range(start: -24h)
  |> filter(fn: (r) => r._measurement == "water" and r.type == "flow")
  |> aggregateWindow(every: 1h, fn: mean)
  |> map(fn: (r) => ({_time: r._time, value: r._value, series: "Water Flow"}))

union(tables: [production, water])
```

Display as: **Time Series** with 2 Y-axes

---

## 🔍 Verify Data in InfluxDB

Check if water measurements are being written:

```bash
# SSH to server
ssh remco@172.16.1.47

# Query InfluxDB bucket
docker exec -it influxdb influx query 'from(bucket:"loxone") |> range(start: -1h) |> filter(fn: (r) => r._measurement == "water")' --org myorg
```

Should show:
```
_measurement  _field  source      type           value   _time
water         value   HomeWizard  flow           0.5     2026-07-20T11:35:00Z
water         value   HomeWizard  consumption   1234.56 2026-07-20T11:35:00Z
```

---

## ⚙️ Configuration Reference

| Setting | Default | Purpose |
|---------|---------|---------|
| `HomeWizard.Enabled` | `false` | Enable/disable water meter collection |
| `HomeWizard.IpAddress` | `172.16.1.156` | HomeWizard device IP (must be on same network) |
| `HomeWizard.UpdateIntervalMinutes` | `5` | Collection frequency (12 readings/hour = 5min interval) |

---

## 🔧 Troubleshooting

### HomeWizard data not appearing?

1. **Check connectivity:**
   ```bash
   ping 172.16.1.156
   curl http://172.16.1.156/api/v1/data
   ```

2. **Verify settings.json:**
   ```bash
   docker exec loxone-solar-forecast cat /config/settings.json | grep -A3 HomeWizard
   ```
   Should show: `"Enabled": true`

3. **Check logs:**
   ```bash
   docker logs loxone-solar-forecast 2>&1 | grep -i "error\|homewizard"
   ```

4. **Ensure container restarted after config change:**
   ```bash
   docker restart loxone-solar-forecast
   sleep 5
   docker logs loxone-solar-forecast | grep -i homewizard
   ```

---

## 📈 Architecture Overview

```
┌─────────────────────────────────────────────────────────┐
│         Multi-Source Data Aggregation Platform          │
└─────────────────────────────────────────────────────────┘
           │                               │
      ┌────▼──────┐              ┌────────▼──────┐
      │   Loxone   │              │ HomeWizard    │
      │  (Solar)   │              │  (Water)      │
      └────┬──────┘              └────────┬──────┘
           │                               │
    ┌──────▼───────────────────────────────▼───────┐
    │     DataCollectionWorker (Independent       │
    │      Intervals: 1-5min Loxone, 5min HW)     │
    └──────┬──────────────────────────────────────┘
           │
    ┌──────▼────────────────────────────────────┐
    │    InfluxDB (Time-Series Database)        │
    │  • production (source=Loxone)             │
    │  • consumption (source=Loxone)            │
    │  • grid (source=Loxone)                   │
    │  • water (source=HomeWizard)              │
    │  • forecast_hourly, forecast_daily        │
    └──────┬────────────────────────────────────┘
           │
    ┌──────▼────────────────────────────────────┐
    │   Grafana Visualization Dashboard         │
    │  • Real-time gauges                       │
    │  • Time-series trends                     │
    │  • Comparative analysis                   │
    └────────────────────────────────────────────┘
```

---

## 🚀 Next Steps

1. **Enable HomeWizard** (follow steps above)
2. **Add water gauges** to Grafana dashboard
3. **Monitor daily usage** (cost analysis potential)
4. **Future expansions ready to add:**
   - Gas meter (P1 protocol)
   - Heat pump energy
   - Grid stability data
   - Additional HomeWizard devices

---

## 📚 Additional Resources

- Full documentation: [MULTI_SOURCE_SETUP.md](MULTI_SOURCE_SETUP.md)
- HomeWizard API docs: http://172.16.1.156/api/v1/data
- InfluxDB Flux queries: [Grafana Query Examples](MULTI_SOURCE_SETUP.md#grafana-queries-for-water-data)

---

**Your platform is now ready for real-time water + energy monitoring!** 💧⚡
