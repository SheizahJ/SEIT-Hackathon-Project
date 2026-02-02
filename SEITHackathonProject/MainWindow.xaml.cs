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
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using TransitRealtime;



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
        private List<TripUpdate> lastTripUpdates = new List<TripUpdate>();
        private List<VehiclePosition> lastVehiclePositions = new List<VehiclePosition>();
        private List<Alert> lastAlerts = new List<Alert>();
        private bool realtimeRefreshing = false;


        public MainWindow()
        {
            InitializeComponent();

            dataPath = System.IO.Path.Combine(projectDirectory, "Data");
            rtDataPath = System.IO.Path.Combine(dataPath, "GTFS_DRT_RT");
            stDataPath = System.IO.Path.Combine(dataPath, "GTFS_DRT_Static");
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
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
                                StopName = values[2]
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

                UpdateVehicleMarkers(lastVehiclePositions);
                UpdateAlertNotice(lastAlerts);
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

                var marker = new GMapMarker(new PointLatLng(vehicle.Position.Latitude, vehicle.Position.Longitude))
                {
                    Shape = new Ellipse
                    {
                        Width = 8,
                        Height = 8,
                        Fill = Brushes.DeepSkyBlue,
                        Stroke = Brushes.White,
                        StrokeThickness = 1,
                        ToolTip = $"{vehicle.Vehicle?.Id ?? "Vehicle"} {vehicle.Trip?.RouteId ?? string.Empty}".Trim()
                    }
                };

                vehicleMarkers.Add(marker);
                mapView.Markers.Add(marker);
            }
        }

        private void UpdateAlertNotice(List<Alert> alerts)
        {
            AlertNotice.Visibility = alerts.Count > 0 ? Visibility.Visible : Visibility.Hidden;
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

            stops.Clear();
            routes.Clear();
            stops.AddRange(LoadStops(stopsPath));
            routes.AddRange(LoadRoutes(routesPath));

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

            mapView.Markers.Clear();

            // Create overlay for routes
            //GMapOverlay routesOverlay = new GMapOverlay("routes");

            // Add markers for each stop
            foreach (var stop in stops)
            {
                var marker = new GMapMarker(new PointLatLng(stop.Latitude, stop.Longitude))
                {
                    Shape = new System.Windows.Shapes.Ellipse
                    {
                        Width = 5,
                        Height = 5,
                        Fill = Brushes.Red
                    }
                };

                mapView.Markers.Add(marker);
            }

            // Add routes as GMapRoute objects to the overlay
            // THIS PART WILL NOT WORK AND IDK WHY SO I GIVE UP
            foreach (var route in routes)
            {
                // add routes to dropdown
                var routeItem = new ComboBoxItem { Content = route.RouteLongName };
                RoutesDropDown.Items.Add(routeItem);
            }

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
            public string StopName { get; set; }
            public double Latitude { get; set; }
            public double Longitude { get; set; } // Ensure it's writable
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
                Text = route.RouteLongName
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

            // get route info
            var selectedItem = RoutesDropDown.SelectedItem as ComboBoxItem;
            if (selectedItem?.Content == null)
            {
                return;
            }

            Route route = routes.FirstOrDefault(p => p.RouteLongName.Equals(selectedItem.Content.ToString(), StringComparison.OrdinalIgnoreCase));
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
    }
}
