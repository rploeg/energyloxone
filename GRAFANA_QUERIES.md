# Grafana Flux Queries

**Datasource:** InfluxDB (Flux language)  
**Bucket:** `loxone`  
**Organization:** `myorg`  
**URL:** `http://172.16.1.47:8086`

---

## Solar Production

### Current Solar Production (Gauge / Stat)
```flux
from(bucket: "loxone")
  |> range(start: -15m)
  |> filter(fn: (r) => r._measurement == "production")
  |> last()
```

### Solar Production Today (Stat — kWh)
```flux
from(bucket: "loxone")
  |> range(start: today())
  |> filter(fn: (r) => r._measurement == "production")
  |> integral(unit: 1h)
  |> map(fn: (r) => ({r with _value: r._value / 1000.0}))
```

### Solar Production — 24h Trend (Time Series)
```flux
from(bucket: "loxone")
  |> range(start: -24h)
  |> filter(fn: (r) => r._measurement == "production")
  |> aggregateWindow(every: 5m, fn: mean, createEmpty: false)
```

### Solar Production — Last 7 Days (Bar Chart)
```flux
from(bucket: "loxone")
  |> range(start: -7d)
  |> filter(fn: (r) => r._measurement == "production")
  |> aggregateWindow(every: 1d, fn: sum, createEmpty: false)
  |> map(fn: (r) => ({r with _value: r._value / 1000.0}))
```

---

## Electricity Consumption

### Current House Consumption (Gauge / Stat)
```flux
from(bucket: "loxone")
  |> range(start: -15m)
  |> filter(fn: (r) => r._measurement == "consumption")
  |> last()
```

### Consumption Today (Stat — kWh)
```flux
from(bucket: "loxone")
  |> range(start: today())
  |> filter(fn: (r) => r._measurement == "consumption")
  |> map(fn: (r) => ({r with _value: float(v: r._value) * if r._value < 0.0 then -1.0 else 1.0}))
  |> integral(unit: 1h)
  |> map(fn: (r) => ({r with _value: r._value / 1000.0}))
```

### Consumption — 24h Trend (Time Series)
```flux
from(bucket: "loxone")
  |> range(start: -24h)
  |> filter(fn: (r) => r._measurement == "consumption")
  |> aggregateWindow(every: 5m, fn: mean, createEmpty: false)
```

---

## Grid Import / Export

### Current Grid Status (Gauge — negative = export)
```flux
from(bucket: "loxone")
  |> range(start: -15m)
  |> filter(fn: (r) => r._measurement == "grid")
  |> last()
```

### Grid Export Today (Stat — kWh)
```flux
from(bucket: "loxone")
  |> range(start: today())
  |> filter(fn: (r) => r._measurement == "grid" and r._field == "export")
  |> integral(unit: 1h)
  |> map(fn: (r) => ({r with _value: r._value / 1000.0}))
```

### Grid Import Today (Stat — kWh)
```flux
from(bucket: "loxone")
  |> range(start: today())
  |> filter(fn: (r) => r._measurement == "grid" and r._field == "import")
  |> integral(unit: 1h)
  |> map(fn: (r) => ({r with _value: r._value / 1000.0}))
```

### Grid Export vs Import — 24h (Time Series, 2 series)
```flux
from(bucket: "loxone")
  |> range(start: -24h)
  |> filter(fn: (r) => r._measurement == "grid")
  |> aggregateWindow(every: 5m, fn: mean, createEmpty: false)
```

---

## Solar Forecast

### Hourly Forecast — Today (Bar Chart)
```flux
from(bucket: "loxone")
  |> range(start: today(), stop: tomorrow())
  |> filter(fn: (r) => r._measurement == "forecast_hourly" and r._field == "forecastedWh")
  |> aggregateWindow(every: 1h, fn: mean, createEmpty: false)
```

### Daily Forecast — 7 Days (Bar Chart)
```flux
from(bucket: "loxone")
  |> range(start: today(), stop: 8d)
  |> filter(fn: (r) => r._measurement == "forecast_daily" and r._field == "forecastedWh")
```

### Forecast vs Actual — Today (Time Series, 2 series)
```flux
forecast = from(bucket: "loxone")
  |> range(start: today(), stop: tomorrow())
  |> filter(fn: (r) => r._measurement == "forecast_hourly" and r._field == "forecastedWh")
  |> aggregateWindow(every: 1h, fn: mean, createEmpty: false)
  |> set(key: "series", value: "Forecast (Wh)")

actual = from(bucket: "loxone")
  |> range(start: today())
  |> filter(fn: (r) => r._measurement == "production")
  |> aggregateWindow(every: 1h, fn: mean, createEmpty: false)
  |> set(key: "series", value: "Actual (W)")

union(tables: [forecast, actual])
```

### Forecast Confidence — 7 Days (Time Series)
```flux
from(bucket: "loxone")
  |> range(start: today(), stop: 8d)
  |> filter(fn: (r) => r._measurement == "forecast_daily" and r._field == "confidence")
```

---

## Water Usage (HomeWizard)

### Current Water Flow (Gauge — L/min)
```flux
from(bucket: "loxone")
  |> range(start: -15m)
  |> filter(fn: (r) => r._measurement == "water" and r.type == "flow")
  |> last()
```

### Total Water Consumption (Stat — m³)
```flux
from(bucket: "loxone")
  |> range(start: -30d)
  |> filter(fn: (r) => r._measurement == "water" and r.type == "consumption")
  |> last()
```

### Water Usage Today (Stat — liters)
```flux
from(bucket: "loxone")
  |> range(start: today())
  |> filter(fn: (r) => r._measurement == "water" and r.type == "flow")
  |> integral(unit: 1m)
```

### Water Flow — 24h Trend (Time Series)
```flux
from(bucket: "loxone")
  |> range(start: -24h)
  |> filter(fn: (r) => r._measurement == "water" and r.type == "flow")
  |> aggregateWindow(every: 5m, fn: mean, createEmpty: false)
```

### Water Usage — Last 7 Days per Day (Bar Chart)
```flux
from(bucket: "loxone")
  |> range(start: -7d)
  |> filter(fn: (r) => r._measurement == "water" and r.type == "flow")
  |> aggregateWindow(every: 1d, fn: integral, unitColumn: "_value")
```

### Water Usage Spikes (Events / Time Series)
Shows only moments with active water flow (> 0):
```flux
from(bucket: "loxone")
  |> range(start: -24h)
  |> filter(fn: (r) => r._measurement == "water" and r.type == "flow")
  |> filter(fn: (r) => r._value > 0)
  |> aggregateWindow(every: 1m, fn: max, createEmpty: false)
```

---

## Combined / Advanced

### Solar Production + House Consumption (2-axis Time Series)
```flux
production = from(bucket: "loxone")
  |> range(start: -24h)
  |> filter(fn: (r) => r._measurement == "production")
  |> aggregateWindow(every: 5m, fn: mean, createEmpty: false)
  |> set(key: "series", value: "Production (W)")

consumption = from(bucket: "loxone")
  |> range(start: -24h)
  |> filter(fn: (r) => r._measurement == "consumption")
  |> map(fn: (r) => ({r with _value: math.abs(x: r._value)}))
  |> aggregateWindow(every: 5m, fn: mean, createEmpty: false)
  |> set(key: "series", value: "Consumption (W)")

union(tables: [production, consumption])
```

### Energy Self-Sufficiency % (Stat)
Percentage of consumption covered by own solar:
```flux
import "math"

production = from(bucket: "loxone")
  |> range(start: today())
  |> filter(fn: (r) => r._measurement == "production")
  |> integral(unit: 1h)
  |> findRecord(fn: (key) => true, idx: 0)

consumption = from(bucket: "loxone")
  |> range(start: today())
  |> filter(fn: (r) => r._measurement == "consumption")
  |> map(fn: (r) => ({r with _value: math.abs(x: r._value)}))
  |> integral(unit: 1h)
  |> findRecord(fn: (key) => true, idx: 0)

array.from(rows: [{
  _time: now(),
  _value: if consumption._value > 0.0 then math.min(x: (production._value / consumption._value) * 100.0, y: 100.0) else 0.0
}])
```

### Solar + Water Correlation (2-axis Time Series)
```flux
solar = from(bucket: "loxone")
  |> range(start: -24h)
  |> filter(fn: (r) => r._measurement == "production")
  |> aggregateWindow(every: 15m, fn: mean, createEmpty: false)
  |> set(key: "series", value: "Solar (W)")

water = from(bucket: "loxone")
  |> range(start: -24h)
  |> filter(fn: (r) => r._measurement == "water" and r.type == "flow")
  |> aggregateWindow(every: 15m, fn: mean, createEmpty: false)
  |> set(key: "series", value: "Water (L/min)")

union(tables: [solar, water])
```

### Net Energy Balance (Time Series — positive = surplus, negative = importing)
```flux
from(bucket: "loxone")
  |> range(start: -24h)
  |> filter(fn: (r) => r._measurement == "grid")
  |> filter(fn: (r) => r._field == "export" or r._field == "import")
  |> pivot(rowKey: ["_time"], columnKey: ["_field"], valueColumn: "_value")
  |> map(fn: (r) => ({r with _value: r.export - r.import}))
  |> aggregateWindow(every: 5m, fn: mean, createEmpty: false)
```

### Water Cost Estimate — Monthly (Stat)
Calculates m³ used in the last 30 days, multiplied by €1.50/m³:
```flux
first_val = from(bucket: "loxone")
  |> range(start: -30d)
  |> filter(fn: (r) => r._measurement == "water" and r.type == "consumption")
  |> first()
  |> findRecord(fn: (key) => true, idx: 0)

last_val = from(bucket: "loxone")
  |> range(start: -30d)
  |> filter(fn: (r) => r._measurement == "water" and r.type == "consumption")
  |> last()
  |> findRecord(fn: (key) => true, idx: 0)

array.from(rows: [{_time: now(), _value: (last_val._value - first_val._value) * 1.50}])
  |> yield(name: "monthly_water_cost_eur")
```

---

## Dashboard Layout Suggestion

| Row | Panel | Query | Visualization |
|-----|-------|-------|---------------|
| 1 | Solar Now | Current Production | Gauge (0–5 kW) |
| 1 | Consumption Now | Current Consumption | Gauge (0–5 kW) |
| 1 | Grid Status | Net Grid Balance | Stat |
| 1 | Water Flow | Current Flow | Gauge (0–10 L/min) |
| 2 | Today Energy | Production + Consumption Today | Stat + Bar Gauge |
| 2 | Water Today | Water Usage Today | Stat (liters) |
| 3 | 24h Overview | Production + Consumption Trend | Time Series (2 series) |
| 3 | 24h Water | Water Flow Trend | Time Series |
| 4 | Forecast vs Actual | Today Forecast vs Actual | Time Series |
| 4 | 7-Day Forecast | Daily Forecast kWh | Bar Chart |
| 5 | Net Balance | Energy balance over time | Time Series (pos/neg fill) |
| 5 | 7-Day Water | Water usage per day | Bar Chart |

---

## Tips

- Set **Grafana → Panel → Field override → Unit** to match:
  - Power: `Watts (W)` or `kilowatts (kW)`
  - Energy: `kilowatt-hours (kWh)`
  - Water flow: `Liter per minute (L/min)`
  - Water volume: `Cubic metres (m³)` or `Liters (L)`
- Use **`aggregateWindow(every: 5m, fn: mean)`** for smooth time series
- For **Gauge** panels showing real-time: use `range(start: -15m)` and `last()`
- All measurements use `source` tag — add **`|> filter(fn: (r) => r.source == "HomeWizard")`** to isolate by source
