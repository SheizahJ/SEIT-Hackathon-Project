using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsPresentation;
using Google.Protobuf;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using TransitRealtime;
using Newtonsoft.Json.Linq;



namespace SEITHackathonProject
{
    public partial class MainWindow : Window
    {
        // constants
        private SolidColorBrush TabSelectColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB8B4B4"));
        private const int InfoUpYPos = 80;
        private const int InfoDownYPos = 470;
        private string projectDirectory = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\"));
        private static readonly Uri TripUpdatesUrl = new Uri("https://drtonline.durhamregiontransit.com/gtfsrealtime/TripUpdates");
        private static readonly Uri VehiclePositionsUrl = new Uri("https://drtonline.durhamregiontransit.com/gtfsrealtime/VehiclePositions");
        private static readonly Uri AlertsUrl = new Uri("https://maps.durham.ca/OpenDataGTFS/alerts.pb");
        private static readonly TimeSpan RealtimeRefreshInterval = TimeSpan.FromSeconds(30);

        // variables
        private bool routeInfoShown = true;
        private string dataPath, rtDataPath, stDataPath = "";
        private readonly List<Stop> stops = new List<Stop>();
        private readonly List<Route> routes = new List<Route>();
        private readonly HttpClient httpClient = new HttpClient();
        private readonly DispatcherTimer realtimeTimer = new DispatcherTimer();
        private readonly List<GMapMarker> vehicleMarkers = new List<GMapMarker>();
        private readonly List<GMapMarker> stopMarkers = new List<GMapMarker>();
        private readonly List<GMapMarker> stopClusterMarkers = new List<GMapMarker>();
        private List<TripUpdate> lastTripUpdates = new List<TripUpdate>();
        private List<VehiclePosition> lastVehiclePositions = new List<VehiclePosition>();
        private List<Alert> lastAlerts = new List<Alert>();
        private bool realtimeRefreshing = false;
        private readonly Dictionary<string, Route> routesById = new Dictionary<string, Route>();
        private readonly Dictionary<string, string> tripHeadsignById = new Dictionary<string, string>();
        private readonly Dictionary<string, Brush> routeBrushCache = new Dictionary<string, Brush>();
        private readonly List<StopOption> allStopOptions = new List<StopOption>();
        private const int MaxStopSuggestions = 200;
        private readonly Dictionary<string, HashSet<string>> stopToRouteIds = new Dictionary<string, HashSet<string>>();
        private readonly Dictionary<string, string> tripIdToRouteId = new Dictionary<string, string>();
        private readonly Dictionary<string, List<StopTimeEntry>> tripStopTimes = new Dictionary<string, List<StopTimeEntry>>();
        private readonly Dictionary<string, string> tripIdToServiceId = new Dictionary<string, string>();
        private readonly HashSet<string> activeServiceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, GeoPoint> geocodeCache = new Dictionary<string, GeoPoint>(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim geocodeLock = new SemaphoreSlim(1, 1);
        private DateTime lastGeocodeUtc = DateTime.MinValue;
        private CancellationTokenSource originGeocodeCts;
        private CancellationTokenSource destinationGeocodeCts;
        private DateTime lastRealtimeUtc = DateTime.MinValue;


        public MainWindow()
        {
            InitializeComponent();

            dataPath = System.IO.Path.Combine(projectDirectory, "Data");
            rtDataPath = System.IO.Path.Combine(dataPath, "GTFS_DRT_RT");
            stDataPath = System.IO.Path.Combine(dataPath, "GTFS_DRT_Static");
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            if (!httpClient.DefaultRequestHeaders.UserAgent.Any())
            {
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SEITHackathonProject/1.0 (local)");
            }
        }

        // Load stops from the file
        private static string[] SplitCsvLine(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return Array.Empty<string>();
            }

            var fields = new List<string>();
            var current = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                        continue;
                    }

                    inQuotes = !inQuotes;
                    continue;
                }

                if (c == ',' && !inQuotes)
                {
                    fields.Add(current.ToString());
                    current.Clear();
                    continue;
                }

                current.Append(c);
            }

            fields.Add(current.ToString());
            return fields.ToArray();
        }

        public List<Stop> LoadStops(string filePathStops)
        {
            var loadedStops = new List<Stop>();

            if (!File.Exists(filePathStops))
            {
                MessageBox.Show($"Stops file not found:\n{filePathStops}");
                return loadedStops;
            }

            var errorCount = 0;
            try
            {
                using (var reader = new StreamReader(filePathStops))
                {
                    reader.ReadLine(); // Skip header
                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        var values = SplitCsvLine(line);

                        if (values.Length >= 6)
                        {
                            var stop = new Stop
                            {
                                StopId = values[0],
                                StopCode = values.Length > 1 ? values[1] : string.Empty,
                                StopName = values[2],
                                StopDesc = values.Length > 3 ? values[3] : string.Empty
                            };

                            if (double.TryParse(values[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
                                && double.TryParse(values[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
                            {
                                stop.Latitude = latitude;
                                stop.Longitude = longitude;
                                loadedStops.Add(stop);
                            }
                            else
                            {
                                errorCount++;
                            }
                        }
                        else
                        {
                            errorCount++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading stops: " + ex.Message);
            }

            if (errorCount > 0)
            {
                MessageBox.Show($"Loaded stops with {errorCount} invalid row(s).");
            }

            return loadedStops;
        }

        public List<Route> LoadRoutes(string filePathRoute)
        {
            var loadedRoutes = new List<Route>();

            if (!File.Exists(filePathRoute))
            {
                MessageBox.Show($"Routes file not found:\n{filePathRoute}");
                return loadedRoutes;
            }

            var errorCount = 0;
            try
            {
                using (var reader = new StreamReader(filePathRoute))
                {
                    reader.ReadLine(); // Skip header
                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        var values = SplitCsvLine(line);

                        if (values.Length >= 7)
                        {
                            var route = new Route
                            {
                                RouteId = values[0],
                                AgencyId = values[1],
                                RouteShortName = values[2],
                                RouteLongName = values[3],
                                RouteDesc = values[4],
                                RouteType = values[5],
                                RouteUrl = values[6]
                            };
                            loadedRoutes.Add(route);
                        }
                        else
                        {
                            errorCount++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading routes: " + ex.Message);
            }

            if (errorCount > 0)
            {
                MessageBox.Show($"Loaded routes with {errorCount} invalid row(s).");
            }

            return loadedRoutes;
        }

        private static List<TripUpdate> ExtractTripUpdates(FeedMessage feedMessage)
        {
            var tripUpdates = new List<TripUpdate>();
            if (feedMessage == null)
            {
                return tripUpdates;
            }

            foreach (var entity in feedMessage.Entity)
            {
                if (entity.TripUpdate != null)
                {
                    tripUpdates.Add(entity.TripUpdate);
                }
            }

            return tripUpdates;
        }

        private static List<VehiclePosition> ExtractVehiclePositions(FeedMessage feedMessage)
        {
            var vehiclePositions = new List<VehiclePosition>();
            if (feedMessage == null)
            {
                return vehiclePositions;
            }

            foreach (var entity in feedMessage.Entity)
            {
                if (entity.Vehicle != null)
                {
                    vehiclePositions.Add(entity.Vehicle);
                }
            }

            return vehiclePositions;
        }

        private static List<Alert> ExtractAlerts(FeedMessage feedMessage)
        {
            var alerts = new List<Alert>();
            if (feedMessage == null)
            {
                return alerts;
            }

            foreach (var entity in feedMessage.Entity)
            {
                if (entity.Alert != null)
                {
                    alerts.Add(entity.Alert);
                }
            }

            return alerts;
        }

        private async Task<FeedMessage> FetchFeedMessageAsync(Uri feedUrl, string fallbackPath)
        {
            try
            {
                byte[] fileContent = await httpClient.GetByteArrayAsync(feedUrl);
                return FeedMessage.Parser.ParseFrom(fileContent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Realtime fetch failed: {ex.Message}");
            }

            if (!string.IsNullOrWhiteSpace(fallbackPath) && File.Exists(fallbackPath))
            {
                try
                {
                    byte[] fallbackContent = File.ReadAllBytes(fallbackPath);
                    return FeedMessage.Parser.ParseFrom(fallbackContent);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Fallback parse failed: {ex.Message}");
                }
            }

            return null;
        }

        private async Task RefreshRealtimeAsync()
        {
            if (realtimeRefreshing)
            {
                return;
            }

            realtimeRefreshing = true;
            try
            {
                var tripFeed = await FetchFeedMessageAsync(TripUpdatesUrl, $@"{rtDataPath}\TripUpdates");
                var vehicleFeed = await FetchFeedMessageAsync(VehiclePositionsUrl, $@"{rtDataPath}\VehiclePositions");
                var alertFeed = await FetchFeedMessageAsync(AlertsUrl, $@"{rtDataPath}\alerts.pb");

                lastTripUpdates = ExtractTripUpdates(tripFeed);
                lastVehiclePositions = ExtractVehiclePositions(vehicleFeed);
                lastAlerts = ExtractAlerts(alertFeed);
                lastRealtimeUtc = DateTime.UtcNow;

                UpdateVehicleMarkers(lastVehiclePositions);
                UpdateAlertNotice(lastAlerts);
                UpdateRealtimeStatus();
                UpdateCurrentRouteUi();
            }
            finally
            {
                realtimeRefreshing = false;
            }
        }

        private void StartRealtimeRefresh()
        {
            realtimeTimer.Interval = RealtimeRefreshInterval;
            realtimeTimer.Tick += async (sender, args) => await RefreshRealtimeAsync();
            realtimeTimer.Start();
            _ = RefreshRealtimeAsync();
        }

        private void UpdateVehicleMarkers(List<VehiclePosition> vehiclePositions)
        {
            foreach (var marker in vehicleMarkers)
            {
                mapView.Markers.Remove(marker);
            }
            vehicleMarkers.Clear();

            foreach (var vehicle in vehiclePositions)
            {
                if (vehicle?.Position == null)
                {
                    continue;
                }

                var routeId = vehicle.Trip?.RouteId;
                var tripId = vehicle.Trip?.TripId;
                var routeName = GetRouteDisplayName(routeId);
                var headsign = string.Empty;
                if (!string.IsNullOrWhiteSpace(tripId) && tripHeadsignById.TryGetValue(tripId, out var tripHeadsign))
                {
                    headsign = tripHeadsign;
                }

                var tooltipText = routeName;
                if (!string.IsNullOrWhiteSpace(headsign))
                {
                    tooltipText = $"{routeName}\n{headsign}";
                }

                var marker = new GMapMarker(new PointLatLng(vehicle.Position.Latitude, vehicle.Position.Longitude))
                {
                    Shape = new Ellipse
                    {
                        Width = 8,
                        Height = 8,
                        Fill = GetRouteBrush(routeId),
                        Stroke = Brushes.White,
                        StrokeThickness = 1,
                        ToolTip = tooltipText
                    }
                };

                vehicleMarkers.Add(marker);
                mapView.Markers.Add(marker);
            }
        }

        private void UpdateAlertNotice(List<Alert> alerts)
        {
            if (alerts.Count > 0)
            {
                AlertNotice.Visibility = Visibility.Visible;
                AlertNoticeText.Text = "Transit alert active. See SUGGESTED ROUTES for alternatives.";
                return;
            }

            var delaySeconds = GetMaxRealtimeDelaySeconds(lastTripUpdates);
            if (delaySeconds <= 0)
            {
                AlertNotice.Visibility = Visibility.Hidden;
                return;
            }

            AlertNotice.Visibility = Visibility.Visible;
            if (delaySeconds >= 60)
            {
                var minutes = delaySeconds / 60;
                AlertNoticeText.Text = $"Delay detected: {minutes} min. See SUGGESTED ROUTES for alternatives.";
            }
            else
            {
                AlertNoticeText.Text = "Minor delay detected.";
            }
        }

        private static int GetMaxRealtimeDelaySeconds(List<TripUpdate> tripUpdates)
        {
            if (tripUpdates == null || tripUpdates.Count == 0)
            {
                return 0;
            }

            var maxDelay = 0;
            foreach (var update in tripUpdates)
            {
                if (update?.StopTimeUpdate == null)
                {
                    continue;
                }

                foreach (var stopUpdate in update.StopTimeUpdate)
                {
                    var delay = (int)(stopUpdate?.Departure?.Delay ?? stopUpdate?.Arrival?.Delay ?? 0);
                    if (delay > maxDelay)
                    {
                        maxDelay = delay;
                    }
                }
            }

            return maxDelay;
        }

        private void UpdateRealtimeStatus()
        {
            if (RealtimeStatusText == null)
            {
                return;
            }

            var vehicleCount = lastVehiclePositions?.Count ?? 0;
            var tripCount = lastTripUpdates?.Count ?? 0;
            var updatedLocal = lastRealtimeUtc == DateTime.MinValue
                ? "unknown"
                : lastRealtimeUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);

            RealtimeStatusText.Text = $"Live vehicles: {vehicleCount} • Trip updates: {tripCount} • Updated {updatedLocal}";
        }

        private void UpdateCurrentRouteUi()
        {
            if (RoutesDropDown.SelectedItem == null)
            {
                return;
            }

            RoutesDropDown_SelectionChanged(RoutesDropDown, null);
        }


        private void mapView_Loaded(object sender, RoutedEventArgs e)
        {
            string stopsPath = $@"{stDataPath}\stops.txt";
            string routesPath = $@"{stDataPath}\routes.txt";
            string tripsPath = $@"{stDataPath}\trips.txt";
            string stopTimesPath = $@"{stDataPath}\stop_times.txt";
            string calendarPath = $@"{stDataPath}\calendar.txt";
            string calendarDatesPath = $@"{stDataPath}\calendar_dates.txt";

            stops.Clear();
            routes.Clear();
            stops.AddRange(LoadStops(stopsPath));
            routes.AddRange(LoadRoutes(routesPath));
            BuildRouteIndex();
            LoadTripHeadsigns(tripsPath);
            LoadTripsForRoutes(tripsPath);
            LoadStopTimesForRoutes(stopTimesPath);
            LoadActiveServices(calendarPath, calendarDatesPath);
            BuildStopOptions();
            ApplyStopOptions(OriginStopBox, allStopOptions);
            ApplyStopOptions(DestinationStopBox, allStopOptions);
            UpdateRoutesForStopSelection();
            UpdateTripSuggestions();

            GMaps.Instance.Mode = AccessMode.ServerAndCache;
            mapView.MapProvider = OpenStreetMapProvider.Instance;
            mapView.MinZoom = 2;
            mapView.MaxZoom = 17;
            mapView.Zoom = 10;
            mapView.MouseWheelZoomType = MouseWheelZoomType.MousePositionAndCenter;
            mapView.CanDragMap = true;
            mapView.DragButton = System.Windows.Input.MouseButton.Left;
            mapView.ShowCenter = false;
            mapView.SetPositionByKeywords("Oshawa, Canada");
            mapView.OnMapZoomChanged += MapView_OnMapZoomChanged;
            mapView.OnPositionChanged += MapView_OnPositionChanged;

            mapView.Markers.Clear();

            // Create overlay for routes
            //GMapOverlay routesOverlay = new GMapOverlay("routes");

            RefreshStopClusters();

            RoutesDropDown.IsEnabled = RoutesDropDown.Items.Count > 0;

            // Add overlay to the map
            // mapView.Overlays.Add(routesOverlay);


            // Focus on the first stop
            if (stops.Count > 0)
            {
                mapView.Position = new PointLatLng(stops[0].Latitude, stops[0].Longitude);
            }

            StartRealtimeRefresh();
        }

        // Method to get route points for a specific RouteId
        public List<PointLatLng> GetRoutePointsForRoute(string routeId, List<Stop> stops)
        {
            List<PointLatLng> routePoints = new List<PointLatLng>();

            foreach (var stop in stops)
            {
                if (IsStopPartOfRoute(routeId, stop))
                {
                    routePoints.Add(new PointLatLng(stop.Latitude, stop.Longitude));
                }
            }

            return routePoints;
        }

        public bool IsStopPartOfRoute(string routeId, Stop stop)
        {
            return true; // Logic to check if the stop belongs to the route
        }

        public class Stop
        {
            public string StopId { get; set; }
            public string StopCode { get; set; }
            public string StopName { get; set; }
            public string StopDesc { get; set; }
            public double Latitude { get; set; }
            public double Longitude { get; set; } // Ensure it's writable
        }

        public class StopOption
        {
            public string StopId { get; set; }
            public string StopName { get; set; }
            public string StopCode { get; set; }
            public string StopDesc { get; set; }
            public string DisplayName { get; set; }
            public string SearchText { get; set; }
        }

        public struct GeoPoint
        {
            public GeoPoint(double latitude, double longitude)
            {
                Latitude = latitude;
                Longitude = longitude;
            }

            public double Latitude { get; }
            public double Longitude { get; }
        }

        public class StopTimeEntry
        {
            public string StopId { get; set; }
            public int ArrivalSeconds { get; set; }
            public int DepartureSeconds { get; set; }
            public int StopSequence { get; set; }
        }

        public class TripOption
        {
            public string RouteId { get; set; }
            public int DepartureSeconds { get; set; }
            public int ArrivalSeconds { get; set; }
            public int SortScore { get; set; }
            public string StatusText { get; set; }
        }

        public class Route
        {
            public string RouteId { get; set; }
            public string AgencyId { get; set; }
            public string RouteShortName { get; set; }
            public string RouteLongName { get; set; }
            public string RouteDesc { get; set; }
            public string RouteType { get; set; }
            public string RouteUrl { get; set; }
            // public string RouteColor { get; set; }
            // public string RouteTextColor { get; set; }
        }



        // UI Events - Purely for visual aspects
        private Grid CreateRouteItem(Route route, string status)
        {
            // grid setup
            var routeInfo = new Grid();

            RowDefinition row1 = new RowDefinition();
            row1.Height = new GridLength(20);
            routeInfo.RowDefinitions.Add(row1);

            RowDefinition row2 = new RowDefinition();
            row2.Height = new GridLength(40);
            routeInfo.RowDefinitions.Add(row2);

            // textblock
            var routeName = new TextBlock
            {
                Text = GetRouteDisplayName(route.RouteId)
            };
            Grid.SetRow(routeName, 0);
            routeInfo.Children.Add(routeName);

            var routeStatus = new TextBlock
            {
                Text = status
            };
            Grid.SetRow(routeStatus, 1);
            routeInfo.Children.Add(routeStatus);

            return routeInfo;
        }

        private void InfoShowBar_Click(object sender, RoutedEventArgs e)
        {
            void TransformAnimation(int toPosition)
            {
                DoubleAnimation animY = new DoubleAnimation
                {
                    From = RouteInfoTranslation.Y,
                    To = toPosition, // Translate to 100 on the Y axis
                    Duration = new System.Windows.Duration(TimeSpan.FromSeconds(0.3))
                };

                // Apply the animations to the TranslateTransform
                RouteInfoTranslation.BeginAnimation(TranslateTransform.YProperty, animY);
            }


            routeInfoShown = !(routeInfoShown);

            if (routeInfoShown)
                TransformAnimation(InfoUpYPos);
            else
                TransformAnimation(InfoDownYPos);
        }

        private void RoutesDropDown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            CurrentRouteInfo.Items.Clear();
            SuggestRouteInfo.Items.Clear();
            AlertNotice.Visibility = Visibility.Hidden;

            var originStopId = ResolveStopId(OriginStopBox);
            var destinationStopId = ResolveStopId(DestinationStopBox);
            if (!string.IsNullOrWhiteSpace(originStopId) && !string.IsNullOrWhiteSpace(destinationStopId))
            {
                UpdateTripSuggestions();
                return;
            }

            // get route info
            var selectedItem = RoutesDropDown.SelectedItem as ComboBoxItem;
            if (selectedItem?.Content == null)
            {
                return;
            }

            Route route = null;
            if (selectedItem.Tag is string routeId && !string.IsNullOrWhiteSpace(routeId))
            {
                routesById.TryGetValue(routeId, out route);
            }
            if (route == null)
            {
                route = routes.FirstOrDefault(p => p.RouteLongName.Equals(selectedItem.Content.ToString(), StringComparison.OrdinalIgnoreCase));
            }
            if (route == null)
            {
                CurrentRouteInfo.Items.Add(new TextBlock { Text = "Route details unavailable." });
                return;
            }

            var tripUpdates = lastTripUpdates ?? new List<TripUpdate>();
            string routeStatusTxt = "Route has no delay.";
            if (tripUpdates.Count == 0)
            {
                routeStatusTxt = "Realtime updates unavailable.";
                CurrentRouteInfo.Items.Add(CreateRouteItem(route, routeStatusTxt));
                return;
            }

            // Suggested Route UI ----------------
            int count = 0;
            foreach (var tripUpdate in tripUpdates)
            {
                if (tripUpdate?.Trip == null)
                {
                    continue;
                }

                if (tripUpdate.Trip.RouteId != route.RouteId && !tripUpdate.HasDelay && count < 10)
                {
                    Route tripRoute = routes.FirstOrDefault(p => p.RouteId.Equals(tripUpdate.Trip.RouteId, StringComparison.OrdinalIgnoreCase));
                    if (tripRoute != null)
                    {
                        SuggestRouteInfo.Items.Add(CreateRouteItem(tripRoute, "Route has no delay."));
                    }
                }
                count++;


                // for current route ui
                if (tripUpdate.Trip.RouteId == route.RouteId && tripUpdate.HasDelay)
                {
                    AlertNotice.Visibility = Visibility.Visible;
                    routeStatusTxt = "There is a delay on this route.";
                }
            }

            // Current Route UI ----------------
            CurrentRouteInfo.Items.Add(CreateRouteItem(route, routeStatusTxt));

            
        }

        private void CrrntRouteBtn_Click(object sender, RoutedEventArgs e)
        {
            CrrntRouteBtn.Background = TabSelectColor;
            SuggstRouteBtn.Background = null;

            CurrentRouteMenu.Visibility = Visibility.Visible;
            SuggestRouteMenu.Visibility = Visibility.Hidden;
        }

        private void SuggstRouteBtn_Click(object sender, RoutedEventArgs e)
        {
            SuggstRouteBtn.Background = TabSelectColor;
            CrrntRouteBtn.Background = null;

            SuggestRouteMenu.Visibility = Visibility.Visible;
            CurrentRouteMenu.Visibility = Visibility.Hidden;
        }

        private async void StopBox_KeyUp(object sender, KeyEventArgs e)
        {
            if (!(sender is ComboBox comboBox))
            {
                return;
            }

            if (e.Key == Key.Tab || e.Key == Key.Escape)
            {
                return;
            }

            if (e.Key == Key.Enter)
            {
                var resolved = await TryResolveStopFromAddressAsync(comboBox);
                if (resolved)
                {
                    UpdateRoutesForStopSelection();
                    UpdateTripSuggestions();
                }
                return;
            }

            var filterText = (comboBox.Text ?? string.Empty).Trim();
            var suggestions = FilterStopOptions(filterText).ToList();

            var caretIndex = comboBox.Text?.Length ?? 0;
            ApplyStopOptions(comboBox, suggestions);
            comboBox.Text = filterText;
            var editableTextBox = comboBox.Template?.FindName("PART_EditableTextBox", comboBox) as TextBox;
            if (editableTextBox != null)
            {
                editableTextBox.CaretIndex = Math.Min(caretIndex, editableTextBox.Text.Length);
            }
            comboBox.IsDropDownOpen = true;
            UpdateGeocodeHint(filterText, suggestions.Count);
            ScheduleGeocodeLookup(comboBox, filterText, suggestions.Count);
            UpdateRoutesForStopSelection();
            UpdateTripSuggestions();
        }

        private void StopBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(sender is ComboBox comboBox))
            {
                return;
            }

            if (comboBox.SelectedItem is StopOption selected)
            {
                comboBox.Text = string.IsNullOrWhiteSpace(selected.DisplayName) ? selected.StopName : selected.DisplayName;
                comboBox.IsDropDownOpen = false;
            }
            UpdateGeocodeHint(comboBox.Text ?? string.Empty, 1);
            CancelPendingGeocode(comboBox);
            UpdateRoutesForStopSelection();
            UpdateTripSuggestions();
        }

        private void BuildStopOptions()
        {
            allStopOptions.Clear();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var stop in stops)
            {
                if (string.IsNullOrWhiteSpace(stop.StopId) || seen.Contains(stop.StopId))
                {
                    continue;
                }

                seen.Add(stop.StopId);
                var displayName = BuildStopDisplayName(stop);
                allStopOptions.Add(new StopOption
                {
                    StopId = stop.StopId,
                    StopName = stop.StopName,
                    StopCode = stop.StopCode,
                    StopDesc = stop.StopDesc,
                    DisplayName = displayName,
                    SearchText = NormalizeForSearch($"{stop.StopName} {stop.StopDesc} {stop.StopCode} {stop.StopId} {displayName}")
                });
            }

            allStopOptions.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
        }

        private IEnumerable<StopOption> FilterStopOptions(string filterText)
        {
            if (string.IsNullOrWhiteSpace(filterText))
            {
                return allStopOptions.Take(MaxStopSuggestions).ToList();
            }

            var normalizedFilter = NormalizeForSearch(filterText);
            var tokens = SplitTokens(filterText);

            var matches = allStopOptions
                .Where(option => tokens.Length == 0 || tokens.All(token => option.SearchText.Contains(token)));

            return matches
                .OrderBy(option => GetStopMatchScore(option, normalizedFilter))
                .ThenByDescending(option => GetRouteCountForStop(option.StopId))
                .ThenBy(option => option.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Take(MaxStopSuggestions)
                .ToList();
        }

        private static void ApplyStopOptions(ComboBox comboBox, IEnumerable<StopOption> options)
        {
            comboBox.ItemsSource = options;
        }

        private void LoadActiveServices(string calendarPath, string calendarDatesPath)
        {
            activeServiceIds.Clear();
            var today = DateTime.Today;
            var todayYmd = today.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

            if (File.Exists(calendarPath))
            {
                try
                {
                    using (var reader = new StreamReader(calendarPath))
                    {
                        reader.ReadLine();
                        while (!reader.EndOfStream)
                        {
                            var line = reader.ReadLine();
                            var values = SplitCsvLine(line);
                            if (values.Length < 10)
                            {
                                continue;
                            }

                            var serviceId = values[0];
                            if (!IsServiceActiveOnDate(values, today))
                            {
                                continue;
                            }

                            if (!string.IsNullOrWhiteSpace(serviceId))
                            {
                                activeServiceIds.Add(serviceId);
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore calendar parsing failures.
                }
            }

            if (File.Exists(calendarDatesPath))
            {
                try
                {
                    using (var reader = new StreamReader(calendarDatesPath))
                    {
                        reader.ReadLine();
                        while (!reader.EndOfStream)
                        {
                            var line = reader.ReadLine();
                            var values = SplitCsvLine(line);
                            if (values.Length < 3)
                            {
                                continue;
                            }

                            var serviceId = values[0];
                            var date = values[1];
                            var exceptionType = values[2];

                            if (!date.Equals(todayYmd, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            if (exceptionType == "1")
                            {
                                activeServiceIds.Add(serviceId);
                            }
                            else if (exceptionType == "2")
                            {
                                activeServiceIds.Remove(serviceId);
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore calendar date parsing failures.
                }
            }
        }

        private static bool IsServiceActiveOnDate(string[] values, DateTime date)
        {
            var dayOfWeek = date.DayOfWeek;
            var dayIndex = dayOfWeek == DayOfWeek.Sunday ? 1 :
                dayOfWeek == DayOfWeek.Monday ? 2 :
                dayOfWeek == DayOfWeek.Tuesday ? 3 :
                dayOfWeek == DayOfWeek.Wednesday ? 4 :
                dayOfWeek == DayOfWeek.Thursday ? 5 :
                dayOfWeek == DayOfWeek.Friday ? 6 : 7;

            var dayFlag = values[dayIndex];
            if (!dayFlag.Equals("1", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!TryParseYmd(values[8], out var startDate))
            {
                return false;
            }

            if (!TryParseYmd(values[9], out var endDate))
            {
                return false;
            }

            return date >= startDate && date <= endDate;
        }

        private static bool TryParseYmd(string value, out DateTime date)
        {
            date = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(value) || value.Length != 8)
            {
                return false;
            }

            if (int.TryParse(value.Substring(0, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
                && int.TryParse(value.Substring(4, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var month)
                && int.TryParse(value.Substring(6, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var day))
            {
                try
                {
                    date = new DateTime(year, month, day);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        private void LoadTripsForRoutes(string tripsPath)
        {
            tripIdToRouteId.Clear();
            tripIdToServiceId.Clear();
            if (!File.Exists(tripsPath))
            {
                return;
            }

            try
            {
                using (var reader = new StreamReader(tripsPath))
                {
                    reader.ReadLine();
                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        var values = SplitCsvLine(line);
                        if (values.Length < 3)
                        {
                            continue;
                        }

                        var routeId = values[0];
                        var serviceId = values.Length > 1 ? values[1] : string.Empty;
                        var tripId = values[2];
                        if (!string.IsNullOrWhiteSpace(tripId) && !tripIdToRouteId.ContainsKey(tripId))
                        {
                            tripIdToRouteId[tripId] = routeId;
                        }
                        if (!string.IsNullOrWhiteSpace(tripId) && !tripIdToServiceId.ContainsKey(tripId))
                        {
                            tripIdToServiceId[tripId] = serviceId;
                        }
                    }
                }
            }
            catch
            {
                // Ignore failures; route filtering will be limited.
            }
        }

        private void LoadStopTimesForRoutes(string stopTimesPath)
        {
            stopToRouteIds.Clear();
            tripStopTimes.Clear();
            if (!File.Exists(stopTimesPath))
            {
                return;
            }

            try
            {
                using (var reader = new StreamReader(stopTimesPath))
                {
                    reader.ReadLine();
                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        var values = SplitCsvLine(line);
                        if (values.Length < 4)
                        {
                            continue;
                        }

                        var tripId = values[0];
                        var arrivalTime = values[1];
                        var departureTime = values[2];
                        var stopId = values[3];
                        var stopSequence = values.Length > 4 ? values[4] : string.Empty;
                        if (string.IsNullOrWhiteSpace(tripId) || string.IsNullOrWhiteSpace(stopId))
                        {
                            continue;
                        }

                        if (!tripIdToRouteId.TryGetValue(tripId, out var routeId))
                        {
                            continue;
                        }

                        if (!tripStopTimes.TryGetValue(tripId, out var list))
                        {
                            list = new List<StopTimeEntry>();
                            tripStopTimes[tripId] = list;
                        }

                        if (TryParseGtfsTime(arrivalTime, out var arrivalSeconds)
                            && TryParseGtfsTime(departureTime, out var departureSeconds)
                            && int.TryParse(stopSequence, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sequence))
                        {
                            list.Add(new StopTimeEntry
                            {
                                StopId = stopId,
                                ArrivalSeconds = arrivalSeconds,
                                DepartureSeconds = departureSeconds,
                                StopSequence = sequence
                            });
                        }

                        if (!stopToRouteIds.TryGetValue(stopId, out var routesForStop))
                        {
                            routesForStop = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            stopToRouteIds[stopId] = routesForStop;
                        }
                        routesForStop.Add(routeId);
                    }
                }

                foreach (var entry in tripStopTimes.Values)
                {
                    entry.Sort((a, b) => a.StopSequence.CompareTo(b.StopSequence));
                }
            }
            catch
            {
                // Ignore failures; route filtering will be limited.
            }
        }

        private void UpdateRoutesForStopSelection()
        {
            if (routes.Count == 0 || stopToRouteIds.Count == 0)
            {
                return;
            }

            var originStopId = ResolveStopId(OriginStopBox);
            var destinationStopId = ResolveStopId(DestinationStopBox);

            HashSet<string> filteredRouteIds = null;
            if (!string.IsNullOrWhiteSpace(originStopId) && stopToRouteIds.TryGetValue(originStopId, out var originRoutes))
            {
                filteredRouteIds = new HashSet<string>(originRoutes, StringComparer.OrdinalIgnoreCase);
            }

            if (!string.IsNullOrWhiteSpace(destinationStopId) && stopToRouteIds.TryGetValue(destinationStopId, out var destinationRoutes))
            {
                if (filteredRouteIds == null)
                {
                    filteredRouteIds = new HashSet<string>(destinationRoutes, StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    filteredRouteIds.IntersectWith(destinationRoutes);
                }
            }

            RoutesDropDown.Items.Clear();

            IEnumerable<Route> routeList = routes;
            if (filteredRouteIds != null)
            {
                routeList = routes.Where(route => filteredRouteIds.Contains(route.RouteId));
            }

            foreach (var route in routeList)
            {
                var displayName = GetRouteDisplayName(route.RouteId);
                var routeItem = new ComboBoxItem
                {
                    Content = displayName,
                    Tag = route.RouteId
                };
                RoutesDropDown.Items.Add(routeItem);
            }

            RoutesDropDown.IsEnabled = RoutesDropDown.Items.Count > 0;
            if (RoutesDropDown.Items.Count == 0)
            {
                CurrentRouteInfo.Items.Clear();
                SuggestRouteInfo.Items.Clear();
                CurrentRouteInfo.Items.Add(new TextBlock { Text = "No routes found for selected stops." });
            }
        }

        private string ResolveStopId(ComboBox comboBox)
        {
            if (comboBox == null)
            {
                return null;
            }

            if (comboBox.SelectedItem is StopOption selected)
            {
                return selected.StopId;
            }

            var text = (comboBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var exactMatches = allStopOptions
                .Where(option =>
                    option.DisplayName.Equals(text, StringComparison.OrdinalIgnoreCase) ||
                    option.StopName.Equals(text, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(option.StopCode) && option.StopCode.Equals(text, StringComparison.OrdinalIgnoreCase)) ||
                    option.StopId.Equals(text, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(option => GetRouteCountForStop(option.StopId))
                .ToList();

            if (exactMatches.Count > 0)
            {
                return exactMatches[0].StopId;
            }

            return null;
        }

        private async Task<bool> TryResolveStopFromAddressAsync(ComboBox comboBox)
        {
            var text = (comboBox?.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var exactStop = allStopOptions.FirstOrDefault(option =>
                option.DisplayName.Equals(text, StringComparison.OrdinalIgnoreCase) ||
                option.StopName.Equals(text, StringComparison.OrdinalIgnoreCase));
            if (exactStop != null)
            {
                comboBox.SelectedItem = exactStop;
                return true;
            }

            var point = await GeocodeAsync(text);
            if (point == null)
            {
                GeocodeStatusText.Text = "Address not found in Durham Region.";
                return false;
            }

            var nearestStopId = FindNearestStopId(point.Value);
            if (string.IsNullOrWhiteSpace(nearestStopId))
            {
                GeocodeStatusText.Text = "No nearby stop found.";
                return false;
            }

            var nearest = allStopOptions.FirstOrDefault(option =>
                option.StopId.Equals(nearestStopId, StringComparison.OrdinalIgnoreCase));
            if (nearest != null)
            {
                comboBox.SelectedItem = nearest;
                comboBox.Text = string.IsNullOrWhiteSpace(nearest.DisplayName) ? nearest.StopName : nearest.DisplayName;
                comboBox.IsDropDownOpen = false;
                GeocodeStatusText.Text = $"Nearest stop: {nearest.StopName}";
                return true;
            }

            return false;
        }

        private async Task<GeoPoint?> GeocodeAsync(string query)
        {
            if (geocodeCache.TryGetValue(query, out var cached))
            {
                return cached;
            }

            await geocodeLock.WaitAsync();
            try
            {
                var elapsed = DateTime.UtcNow - lastGeocodeUtc;
                if (elapsed < TimeSpan.FromSeconds(1))
                {
                    await Task.Delay(TimeSpan.FromSeconds(1) - elapsed);
                }

                var fullQuery = BuildGeocodeQuery(query);
                var url = $"https://nominatim.openstreetmap.org/search?format=json&limit=1&q={Uri.EscapeDataString(fullQuery)}";
                var response = await httpClient.GetStringAsync(url);
                lastGeocodeUtc = DateTime.UtcNow;

                var json = JArray.Parse(response);
                if (json.Count == 0)
                {
                    return null;
                }

                var first = json[0];
                if (first == null)
                {
                    return null;
                }

                if (!double.TryParse(first.Value<string>("lat"), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ||
                    !double.TryParse(first.Value<string>("lon"), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
                {
                    return null;
                }

                var point = new GeoPoint(lat, lon);
                geocodeCache[query] = point;
                return point;
            }
            catch
            {
                return null;
            }
            finally
            {
                geocodeLock.Release();
            }
        }

        private static string BuildGeocodeQuery(string input)
        {
            var normalized = input.Trim();
            if (normalized.IndexOf("durham", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("oshawa", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("ontario", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("canada", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return normalized;
            }

            return $"{normalized}, Durham Region, Ontario, Canada";
        }

        private string FindNearestStopId(GeoPoint point)
        {
            if (stops.Count == 0)
            {
                return null;
            }

            var bestDistance = double.MaxValue;
            string bestStopId = null;

            foreach (var stop in stops)
            {
                var distance = HaversineKm(point.Latitude, point.Longitude, stop.Latitude, stop.Longitude);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestStopId = stop.StopId;
                }
            }

            return bestStopId;
        }

        private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371.0;
            var dLat = DegreesToRadians(lat2 - lat1);
            var dLon = DegreesToRadians(lon2 - lon1);
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        private static double DegreesToRadians(double degrees)
        {
            return degrees * (Math.PI / 180.0);
        }

        private static string BuildStopDisplayName(Stop stop)
        {
            var name = stop?.StopName?.Trim() ?? string.Empty;
            var desc = stop?.StopDesc?.Trim() ?? string.Empty;
            var code = stop?.StopCode?.Trim() ?? string.Empty;

            var display = name;
            if (!string.IsNullOrWhiteSpace(desc) && !desc.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                display = $"{display} - {desc}";
            }

            if (!string.IsNullOrWhiteSpace(code))
            {
                display = $"{display} (Code {code})";
            }

            return display.Trim();
        }

        private static string NormalizeForSearch(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(text.Length);
            foreach (var ch in text.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch))
                {
                    builder.Append(ch);
                }
                else if (char.IsWhiteSpace(ch))
                {
                    builder.Append(' ');
                }
                else
                {
                    builder.Append(' ');
                }
            }

            return builder.ToString();
        }

        private static string[] SplitTokens(string text)
        {
            var normalized = NormalizeForSearch(text);
            return normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private int GetRouteCountForStop(string stopId)
        {
            if (string.IsNullOrWhiteSpace(stopId))
            {
                return 0;
            }

            return stopToRouteIds.TryGetValue(stopId, out var routesForStop) ? routesForStop.Count : 0;
        }

        private int GetStopMatchScore(StopOption option, string normalizedFilter)
        {
            if (option == null)
            {
                return 3;
            }

            var display = NormalizeForSearch(option.DisplayName);
            var name = NormalizeForSearch(option.StopName);

            if (!string.IsNullOrWhiteSpace(normalizedFilter))
            {
                if (display.StartsWith(normalizedFilter))
                {
                    return 0;
                }

                if (name.StartsWith(normalizedFilter))
                {
                    return 1;
                }

                if (option.SearchText.Contains(normalizedFilter))
                {
                    return 2;
                }
            }

            return 3;
        }

        private void UpdateGeocodeHint(string filterText, int suggestionCount)
        {
            if (GeocodeStatusText == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(filterText) && filterText.Length >= 3 && suggestionCount == 0)
            {
                GeocodeStatusText.Text = "No stop matches. Press Enter to search address.";
                return;
            }

            if (GeocodeStatusText.Text == "No stop matches. Press Enter to search address.")
            {
                GeocodeStatusText.Text = string.Empty;
            }
        }

        private void ScheduleGeocodeLookup(ComboBox comboBox, string filterText, int suggestionCount)
        {
            if (comboBox == null)
            {
                return;
            }

            var trimmed = (filterText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length < 5 || suggestionCount > 0)
            {
                CancelPendingGeocode(comboBox);
                return;
            }

            var cts = CreateGeocodeCts(comboBox);
            var token = cts.Token;
            _ = RunGeocodeLookupAsync(comboBox, trimmed, token);
        }

        private async Task RunGeocodeLookupAsync(ComboBox comboBox, string filterText, CancellationToken token)
        {
            try
            {
                await Task.Delay(600, token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (token.IsCancellationRequested)
            {
                return;
            }

            var currentText = (comboBox.Text ?? string.Empty).Trim();
            if (!currentText.Equals(filterText, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            GeocodeStatusText.Text = "Searching address...";
            var resolved = await TryResolveStopFromAddressAsync(comboBox);
            if (resolved)
            {
                UpdateRoutesForStopSelection();
                UpdateTripSuggestions();
                return;
            }

            if (GeocodeStatusText.Text == "Searching address...")
            {
                GeocodeStatusText.Text = "Address not found in Durham Region.";
            }
        }

        private void CancelPendingGeocode(ComboBox comboBox)
        {
            var cts = GetGeocodeCts(comboBox);
            if (cts == null)
            {
                return;
            }

            cts.Cancel();
        }

        private CancellationTokenSource CreateGeocodeCts(ComboBox comboBox)
        {
            CancelPendingGeocode(comboBox);
            var cts = new CancellationTokenSource();
            if (comboBox == OriginStopBox)
            {
                originGeocodeCts = cts;
            }
            else if (comboBox == DestinationStopBox)
            {
                destinationGeocodeCts = cts;
            }
            return cts;
        }

        private CancellationTokenSource GetGeocodeCts(ComboBox comboBox)
        {
            if (comboBox == OriginStopBox)
            {
                return originGeocodeCts;
            }

            if (comboBox == DestinationStopBox)
            {
                return destinationGeocodeCts;
            }

            return null;
        }

        private void UpdateTripSuggestions()
        {
            CurrentRouteInfo.Items.Clear();
            SuggestRouteInfo.Items.Clear();

            var originStopId = ResolveStopId(OriginStopBox);
            var destinationStopId = ResolveStopId(DestinationStopBox);

            if (string.IsNullOrWhiteSpace(originStopId) || string.IsNullOrWhiteSpace(destinationStopId))
            {
                CurrentRouteInfo.Items.Add(new TextBlock { Text = "Select both From and To stops." });
                return;
            }

            if (tripStopTimes.Count == 0)
            {
                CurrentRouteInfo.Items.Add(new TextBlock { Text = "Trip data not loaded." });
                return;
            }

            var options = BuildTripOptions(originStopId, destinationStopId);
            if (options.Count == 0)
            {
                CurrentRouteInfo.Items.Add(new TextBlock { Text = "No trips found for selected stops." });
                return;
            }

            var bestOptions = options.Take(3).ToList();
            foreach (var option in bestOptions)
            {
                if (routesById.TryGetValue(option.RouteId, out var route))
                {
                    CurrentRouteInfo.Items.Add(CreateRouteItem(route, option.StatusText));
                }
                else
                {
                    CurrentRouteInfo.Items.Add(new TextBlock { Text = option.StatusText });
                }
            }

            var suggested = options
                .GroupBy(option => option.RouteId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Skip(1)
                .Take(5)
                .ToList();

            foreach (var option in suggested)
            {
                if (routesById.TryGetValue(option.RouteId, out var route))
                {
                    SuggestRouteInfo.Items.Add(CreateRouteItem(route, option.StatusText));
                }
            }

            var bestRouteId = bestOptions.First().RouteId;
            if (!string.IsNullOrWhiteSpace(bestRouteId))
            {
                if (RoutesDropDown.SelectedItem is ComboBoxItem selectedItem
                    && selectedItem.Tag is string selectedRouteId
                    && selectedRouteId.Equals(bestRouteId, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                foreach (ComboBoxItem item in RoutesDropDown.Items)
                {
                    if (item.Tag is string routeId && routeId.Equals(bestRouteId, StringComparison.OrdinalIgnoreCase))
                    {
                        RoutesDropDown.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        private List<TripOption> BuildTripOptions(string originStopId, string destinationStopId)
        {
            var results = new List<TripOption>();
            var nowSeconds = (int)DateTime.Now.TimeOfDay.TotalSeconds;
            var delayMap = BuildTripDelayMap(lastTripUpdates);

            foreach (var trip in tripStopTimes)
            {
                var tripId = trip.Key;
                var entries = trip.Value;
                if (activeServiceIds.Count > 0 && tripIdToServiceId.TryGetValue(tripId, out var serviceId))
                {
                    if (!activeServiceIds.Contains(serviceId))
                    {
                        continue;
                    }
                }
                var origin = entries.FirstOrDefault(entry => entry.StopId == originStopId);
                var destination = entries.FirstOrDefault(entry => entry.StopId == destinationStopId);
                if (origin == null || destination == null || origin.StopSequence >= destination.StopSequence)
                {
                    continue;
                }

                var delaySeconds = delayMap.TryGetValue(tripId, out var delay) ? delay : 0;
                var departure = origin.DepartureSeconds + delaySeconds;
                var arrival = destination.ArrivalSeconds + delaySeconds;
                var duration = Math.Max(0, arrival - departure);

                if (!tripIdToRouteId.TryGetValue(tripId, out var routeId))
                {
                    continue;
                }

                var headsign = tripHeadsignById.TryGetValue(tripId, out var tripHeadsign) ? tripHeadsign : string.Empty;
                var routeName = GetRouteDisplayName(routeId);

                var status = $"{routeName} • Departs {FormatTime(departure)} • Arrives {FormatTime(arrival)} • {FormatDuration(duration)}";
                if (!string.IsNullOrWhiteSpace(headsign))
                {
                    status = $"{status} • {headsign}";
                }

                var timeScore = departure >= nowSeconds ? departure : departure + 24 * 3600;
                results.Add(new TripOption
                {
                    RouteId = routeId,
                    DepartureSeconds = departure,
                    ArrivalSeconds = arrival,
                    SortScore = timeScore,
                    StatusText = status
                });
            }

            return results
                .OrderBy(option => option.SortScore)
                .ToList();
        }

        private static bool TryParseGtfsTime(string value, out int seconds)
        {
            seconds = 0;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var parts = value.Split(':');
            if (parts.Length < 3)
            {
                return false;
            }

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours) ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) ||
                !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var secs))
            {
                return false;
            }

            seconds = hours * 3600 + minutes * 60 + secs;
            return true;
        }

        private static string FormatTime(int seconds)
        {
            var normalized = seconds % (24 * 3600);
            var time = TimeSpan.FromSeconds(normalized);
            return $"{(int)time.TotalHours:00}:{time.Minutes:00}";
        }

        private static string FormatDuration(int seconds)
        {
            var duration = TimeSpan.FromSeconds(seconds);
            if (duration.TotalHours >= 1)
            {
                return $"{(int)duration.TotalHours}h {duration.Minutes}m";
            }
            return $"{duration.Minutes}m";
        }

        private static Dictionary<string, int> BuildTripDelayMap(List<TripUpdate> tripUpdates)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (tripUpdates == null || tripUpdates.Count == 0)
            {
                return result;
            }

            foreach (var update in tripUpdates)
            {
                var tripId = update?.Trip?.TripId;
                if (string.IsNullOrWhiteSpace(tripId))
                {
                    continue;
                }

                if (update.StopTimeUpdate != null && update.StopTimeUpdate.Count > 0)
                {
                    var delay = update.StopTimeUpdate
                        .Select(stop => (int)(stop?.Departure?.Delay ?? stop?.Arrival?.Delay ?? 0))
                        .FirstOrDefault();
                    result[tripId] = delay;
                    continue;
                }

                // TripDescriptor does not expose delay in this GTFS-RT binding.
            }

            return result;
        }

        private void MapView_OnMapZoomChanged()
        {
            RefreshStopClusters();
        }

        private void MapView_OnPositionChanged(PointLatLng point)
        {
            RefreshStopClusters();
        }

        private void RefreshStopClusters()
        {
            foreach (var marker in stopMarkers)
            {
                mapView.Markers.Remove(marker);
            }
            foreach (var marker in stopClusterMarkers)
            {
                mapView.Markers.Remove(marker);
            }
            stopMarkers.Clear();
            stopClusterMarkers.Clear();

            if (stops.Count == 0)
            {
                return;
            }

            var zoom = mapView.Zoom;
            var cellSize = Math.Max(0.005, Math.Min(1.0, 0.04 * Math.Pow(2, 12 - zoom)));
            var clusters = new Dictionary<string, List<Stop>>();

            foreach (var stop in stops)
            {
                var keyLat = (int)Math.Floor(stop.Latitude / cellSize);
                var keyLon = (int)Math.Floor(stop.Longitude / cellSize);
                var key = $"{keyLat}:{keyLon}";

                if (!clusters.TryGetValue(key, out var list))
                {
                    list = new List<Stop>();
                    clusters[key] = list;
                }
                list.Add(stop);
            }

            foreach (var cluster in clusters.Values)
            {
                if (cluster.Count == 1)
                {
                    var stop = cluster[0];
                    var stopMarker = new GMapMarker(new PointLatLng(stop.Latitude, stop.Longitude))
                    {
                        Shape = new System.Windows.Shapes.Ellipse
                        {
                            Width = 5,
                            Height = 5,
                            Fill = Brushes.Red,
                            ToolTip = stop.StopName
                        }
                    };
                    stopMarkers.Add(stopMarker);
                    mapView.Markers.Add(stopMarker);
                    continue;
                }

                var avgLat = cluster.Average(item => item.Latitude);
                var avgLon = cluster.Average(item => item.Longitude);
                var size = Math.Min(28, 14 + (int)Math.Log(cluster.Count) * 6);

                var container = new Grid
                {
                    Width = size,
                    Height = size,
                    ToolTip = $"{cluster.Count} stops"
                };
                container.Children.Add(new System.Windows.Shapes.Ellipse
                {
                    Fill = new SolidColorBrush(Color.FromArgb(200, 220, 0, 0)),
                    Stroke = Brushes.White,
                    StrokeThickness = 1
                });
                container.Children.Add(new TextBlock
                {
                    Text = cluster.Count.ToString(CultureInfo.InvariantCulture),
                    Foreground = Brushes.White,
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });

                var clusterMarker = new GMapMarker(new PointLatLng(avgLat, avgLon))
                {
                    Shape = container
                };
                stopClusterMarkers.Add(clusterMarker);
                mapView.Markers.Add(clusterMarker);
            }
        }

        private void BuildRouteIndex()
        {
            routesById.Clear();
            routeBrushCache.Clear();
            foreach (var route in routes)
            {
                if (!string.IsNullOrWhiteSpace(route.RouteId) && !routesById.ContainsKey(route.RouteId))
                {
                    routesById.Add(route.RouteId, route);
                }
            }
        }

        private void LoadTripHeadsigns(string tripsPath)
        {
            tripHeadsignById.Clear();
            if (!File.Exists(tripsPath))
            {
                return;
            }

            try
            {
                using (var reader = new StreamReader(tripsPath))
                {
                    reader.ReadLine();
                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        var values = SplitCsvLine(line);
                        if (values.Length < 4)
                        {
                            continue;
                        }

                        var tripId = values[2];
                        var headsign = values[3];
                        if (!string.IsNullOrWhiteSpace(tripId) && !tripHeadsignById.ContainsKey(tripId))
                        {
                            tripHeadsignById[tripId] = headsign;
                        }
                    }
                }
            }
            catch
            {
                // Ignore headsign failures; tooltips will just omit headsigns.
            }
        }

        private string GetRouteDisplayName(string routeId)
        {
            if (string.IsNullOrWhiteSpace(routeId))
            {
                return "Route";
            }

            if (routesById.TryGetValue(routeId, out var route))
            {
                var display = ComposeRouteName(route.RouteShortName, route.RouteLongName);
                return string.IsNullOrWhiteSpace(display) ? route.RouteId : display;
            }

            return routeId;
        }

        private static string ComposeRouteName(string routeShortName, string routeLongName)
        {
            var shortName = routeShortName?.Trim() ?? string.Empty;
            var longName = routeLongName?.Trim() ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(shortName)
                && !string.IsNullOrWhiteSpace(longName)
                && !IsGenericRouteName(shortName, longName))
            {
                return $"{shortName} {longName}".Trim();
            }

            if (!string.IsNullOrWhiteSpace(longName))
            {
                return IsGenericRouteName(shortName, longName) ? shortName : longName;
            }

            if (!string.IsNullOrWhiteSpace(shortName))
            {
                return shortName;
            }

            return shortName;
        }

        private static bool IsGenericRouteName(string shortName, string longName)
        {
            if (string.IsNullOrWhiteSpace(longName))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(shortName)
                && longName.Equals($"Route {shortName}", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }


        private Brush GetRouteBrush(string routeId)
        {
            if (string.IsNullOrWhiteSpace(routeId))
            {
                return Brushes.DeepSkyBlue;
            }

            if (routeBrushCache.TryGetValue(routeId, out var brush))
            {
                return brush;
            }

            var hash = 17;
            foreach (var ch in routeId)
            {
                hash = hash * 31 + ch;
            }

            var r = (byte)(60 + (hash & 0x7F));
            var g = (byte)(60 + ((hash >> 7) & 0x7F));
            var b = (byte)(60 + ((hash >> 14) & 0x7F));
            brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            routeBrushCache[routeId] = brush;
            return brush;
        }
    }
}
