# SEIT Hackathon Project (DRT)

WPF app that visualizes Durham Region Transit (Ontario) stops and live vehicle
positions on a map, with basic route information and alerts.

## Features

- Map with DRT stops and live vehicles
- Stop clustering by zoom level to reduce clutter
- Vehicle markers colored by route
- Tooltips show route name and trip headsign (when available)
- Route dropdown filtered by From/To stops
- From/To stop search with autocomplete
- Address search (press Enter) resolves to nearest stop
- Schedule-based trip suggestions with realtime delay adjustments
- Alert banner and suggested route panel with delay warnings

## Data Sources

GTFS-RT (live):
- Trip Updates: https://drtonline.durhamregiontransit.com/gtfsrealtime/TripUpdates
- Vehicle Positions: https://drtonline.durhamregiontransit.com/gtfsrealtime/VehiclePositions
- Alerts: https://maps.durham.ca/OpenDataGTFS/alerts.pb

Static GTFS:
- Local files in `SEITHackathonProject/Data/GTFS_DRT_Static/`

## Build and Run

1. Open `SEITHackathonProject.sln` in Visual Studio.
2. Restore NuGet packages if prompted.
3. Build the solution.
4. Run (F5).

## Project Notes

- Map provider: OpenStreetMap via GMap.NET.
- Vehicle tooltips use route short/long name and trip headsign from `trips.txt`.
- From/To suggestions and filtering use `stop_times.txt` + `trips.txt`.
- Service calendar filtering uses `calendar.txt` + `calendar_dates.txt`.
- Address search uses Nominatim (OpenStreetMap), no API key required.
- Clustering is computed dynamically based on the map zoom level.

## Troubleshooting

- If stops are missing, confirm `Data/GTFS_DRT_Static/` exists and rebuild.
- If live vehicles do not appear, check internet access and GTFS-RT URLs.
- If you see GTFS-RT type conflict warnings, the build still succeeds; they
  come from the generated `GtfsRealtime.cs` file and the binding package.
