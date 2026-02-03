# SEIT Hackathon Project — Durham Region Transit (DRT)

A WPF (.NET Framework) desktop app that visualizes Durham Region Transit in
real time, combines static GTFS with GTFS‑Realtime, and helps users discover
routes between two places or stops with delay awareness.

## Problem Statement

Durham Region riders often have to switch between schedule PDFs, static maps,
and generic trip planners to understand what is running **right now** and
whether delays affect their trip. This project aims to provide a single,
fast, map‑centric view that answers:

- Where are buses currently?
- Are there active alerts or delays?
- Which routes can take me from A to B today?

## Key Features

- **Live map view**: OpenStreetMap tiles with real‑time vehicle markers.
- **Stop clustering**: Dynamic clustering based on zoom to reduce clutter.
- **Route‑colored vehicles**: Each vehicle is colored by route; tooltips show
  route name and trip headsign when available.
- **Smart From/To search**: Autocomplete across stop name/description/code,
  ranked by match quality and route density.
- **Address search**: Enter an address and the app resolves it to the nearest
  DRT stop (Nominatim, no API key required).
- **Trip suggestions**: Schedule‑based options with real‑time delay adjustments.
- **Alerts**: Banner shows active transit alerts and delay severity.

## How It Works (Architecture)

```
UI (WPF) ──> GMap.NET (OpenStreetMap tiles)
          └─> Search + Trip Panels

Data
  ├─ GTFS Static: stops, routes, trips, stop_times, calendar
  └─ GTFS-RT: TripUpdates, VehiclePositions, Alerts
```

### Data Pipeline

1. **Static GTFS load (startup)**
   - `stops.txt` → stop locations
   - `routes.txt` + `trips.txt` → route metadata and headsigns
   - `stop_times.txt` → stop‑to‑route index and trip timing
   - `calendar.txt` + `calendar_dates.txt` → service‑day filtering
2. **Realtime refresh (every 30s)**
   - Trip updates (delays)
   - Vehicle positions (live buses)
   - Alerts (banner)

### Trip Suggestion Logic (High‑Level)

- Finds trips containing both origin and destination stops in order.
- Applies service calendar rules for **today**.
- Adjusts departure/arrival times with realtime delay.
- Ranks by soonest valid departure (with next‑day wrap).

## Data Sources

**GTFS‑Realtime (live):**
- Trip Updates: `https://drtonline.durhamregiontransit.com/gtfsrealtime/TripUpdates`
- Vehicle Positions: `https://drtonline.durhamregiontransit.com/gtfsrealtime/VehiclePositions`
- Alerts: `https://maps.durham.ca/OpenDataGTFS/alerts.pb`

**GTFS Static (local):**
- `SEITHackathonProject/Data/GTFS_DRT_Static/`

## Build & Run

1. Open `SEITHackathonProject.sln` in Visual Studio.
2. Restore NuGet packages if prompted.
3. Build the solution.
4. Run (F5).

## Technical Details

### Address Search (Geocoding)

- Uses Nominatim (OpenStreetMap), **no API key** required.
- Automatically scoped to Durham Region, Ontario, Canada.
- Auto‑lookup triggers when no stop matches are found.
- Press Enter to force a lookup at any time.

### Real‑Time Visibility

- Vehicle dots only appear for trips publishing GTFS‑RT vehicle positions.
- UI shows last update time + live counts for transparency.

### Known Limitations

- **Live vehicles** depend entirely on GTFS‑RT feed coverage.
- **Routing** is currently single‑route (no transfers).
- **Static GTFS** must be updated manually when the agency updates feeds.

## Troubleshooting

- Missing stops: verify `Data/GTFS_DRT_Static/` exists and rebuild.
- No live vehicles: confirm internet access and GTFS‑RT URLs.
- Address not found: add street + city (e.g., "Simcoe St N Oshawa") or press Enter.

## Roadmap (Suggested)

- Transfer‑aware routing
- Address autocomplete suggestions
- Auto‑refresh of static GTFS feeds
- Vehicle direction arrows and route polylines

## Tech Stack

- WPF (.NET Framework)
- GMap.NET
- Google.Protobuf + GTFS‑RT bindings
- OpenStreetMap / Nominatim
