# SEIT Hackathon Project (DRT)

WPF (.NET Framework) map application that visualizes Durham Region Transit
stops and live vehicle positions, with From/To stop selection, real-time
alerts, and schedule-aware trip suggestions.

## Demo Summary

- Map with DRT stops and live vehicles
- Stop clustering by zoom level to reduce clutter
- Vehicle markers colored by route with route/headsign tooltips
- From/To stop search with smart autocomplete
- Address search (auto-lookup and Enter-to-search) resolved to nearest stop
- Route dropdown filtered by chosen From/To stops
- Schedule-based trip suggestions with real-time delay adjustments
- Alert banner with realtime delay warnings

## Architecture Overview

The app is a single-window WPF UI (`MainWindow.xaml`) and code-behind
(`MainWindow.xaml.cs`) backed by local static GTFS data and live GTFS-RT feeds:

```
UI (WPF)
  -> GMap.NET (OpenStreetMap tiles)
  -> Stop/Route search + results panels
Data
  -> GTFS Static: stops, routes, trips, stop_times, calendar
  -> GTFS-RT: TripUpdates, VehiclePositions, Alerts
```

### Data Pipeline

1. **Static GTFS load** on startup
   - `stops.txt` => stop locations
   - `routes.txt` + `trips.txt` => route metadata + headsigns
   - `stop_times.txt` => stop-to-route indexes + trip timing
   - `calendar.txt` + `calendar_dates.txt` => service-day filtering
2. **Realtime refresh** every 30 seconds
   - Trip updates (delays)
   - Vehicle positions (live bus dots)
   - Alerts (banner)

### Core Components

- **Stop search and autocomplete**
  - Multi-token search across stop name, stop description, stop code, stop ID
  - Suggestions ranked by text match and route density
  - Geocoding fallback when no stop matches
- **Trip suggestion logic**
  - Finds trips that contain both origin and destination stops in order
  - Applies calendar filters (active service only)
  - Adjusts departure/arrival using realtime delay
  - Scores by soonest departure (with next-day wrap)
- **Map clustering**
  - Dynamic grid-based clustering driven by zoom
  - Single stops shown as red dots, clusters show count
- **Route/vehicle display**
  - Route name composed from short + long name when meaningful
  - Vehicle markers colored deterministically by route ID
  - Tooltips show route name and trip headsign when available

## Data Sources

GTFS-RT (live):
- Trip Updates: https://drtonline.durhamregiontransit.com/gtfsrealtime/TripUpdates
- Vehicle Positions: https://drtonline.durhamregiontransit.com/gtfsrealtime/VehiclePositions
- Alerts: https://maps.durham.ca/OpenDataGTFS/alerts.pb

Static GTFS (local):
- `SEITHackathonProject/Data/GTFS_DRT_Static/`

## Build and Run

1. Open `SEITHackathonProject.sln` in Visual Studio.
2. Restore NuGet packages if prompted.
3. Build the solution.
4. Run (F5).

## Technical Details

### Address Search (Geocoding)

- Uses Nominatim (OpenStreetMap), no API key required.
- Input is scoped to Durham Region, Ontario, Canada.
- If no stop matches are found, an address lookup is triggered automatically.
- Press Enter to force a lookup at any time.

### Real-Time Status

- Vehicle positions are only shown for trips that are currently publishing
  location data via the GTFS-RT feed.
- The UI shows last update time, live vehicle count, and trip update count.

### Trip Suggestions

- Single-route trip suggestions between From/To stops
- Active service filtering based on today's service calendar
- Delay adjustments applied using the current GTFS-RT trip updates

## Limitations

- **Live vehicles** only appear if DRT publishes a vehicle position for that bus.
- **Address search** depends on Nominatim accuracy and rate limits.
- **Routing** currently shows single-route trips (no transfers).
- **Static data** must be updated manually if the GTFS feed changes.

## Troubleshooting

- If stops are missing, verify `Data/GTFS_DRT_Static/` exists and rebuild.
- If live vehicles do not appear, verify internet access and GTFS-RT URLs.
- If search results feel incomplete, try adding a street name + city or press Enter.

## Roadmap Ideas

- Multi-leg routing (with transfers)
- Address autocomplete suggestions
- Automatic GTFS static refresh
- Vehicle direction arrows and route polylines
