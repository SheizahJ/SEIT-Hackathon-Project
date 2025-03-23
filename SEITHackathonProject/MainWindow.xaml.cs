using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsPresentation;
using Google.Protobuf;
using GTFS.IO.CSV;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
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

        // variables
        private bool routeInfoShown = true;
        private string dataPath, rtDataPath, stDataPath = "";


        public MainWindow()
        {
            InitializeComponent();

            dataPath = System.IO.Path.Combine(projectDirectory, "Data");
            rtDataPath = System.IO.Path.Combine(dataPath, "GTFS_DRT_RT");
            stDataPath = System.IO.Path.Combine(dataPath, "GTFS_DRT_Static");
        }

        // Load stops from the file
        public List<Stop> LoadStops(string filePathStops)
        {
            var stops = new List<Stop>();
            try
            {
                using (var reader = new StreamReader(filePathStops))
                {
                    reader.ReadLine(); // Skip header
                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        var values = line.Split(',');

                        if (values.Length >= 6)
                        {
                            try
                            {
                                var stop = new Stop
                                {
                                    StopId = values[0],
                                    StopName = values[2],
                                    Latitude = double.Parse(values[4]),
                                    Longitude = double.Parse(values[5])
                                };
                                stops.Add(stop);
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"Error parsing line: {line}\n{ex.Message}");
                            }
                        }
                        else
                        {
                            MessageBox.Show($"Skipping invalid line: {line}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading stops: " + ex.Message);
            }
            return stops;
        }

        public List<Route> LoadRoutes(string filePathRoute)
        {
            var routes = new List<Route>();
            try
            {
                using (var reader = new StreamReader(filePathRoute))
                {
                    reader.ReadLine(); // Skip header
                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        var values = line.Split(',');

                        if (values.Length >= 9)
                        {
                            try
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
                                routes.Add(route);
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"Error parsing line: {line}\n{ex.Message}");
                            }
                        }
                        else
                        {
                            MessageBox.Show($"Skipping invalid line: {line}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading routes: " + ex.Message);
            }
            return routes;
        }

        private TripUpdate GetRTTripUpdate()
        {
            string filePath = $@"{rtDataPath}\TripUpdates";
            try
            {
                // Read the GTFS-RT feed into a byte array
                byte[] fileContent = File.ReadAllBytes(filePath);

                // Parse the GTFS-RT FeedMessage from the byte array
                var feedMessage = FeedMessage.Parser.ParseFrom(fileContent);

                // Iterate through each entity in the feed
                foreach (var entity in feedMessage.Entity)
                {
                    // Check if the entity contains a TripUpdate
                    if (entity.TripUpdate.HasTimestamp)
                    {
                        return entity.TripUpdate;
                    }
                }
            }
            catch (InvalidProtocolBufferException ex)
            {
                Console.WriteLine($"Error parsing GTFS-RT feed: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }

            return null;
        }

        private VehiclePosition GetRTVehiclePosition()
        {
            string filePath = $@"{rtDataPath}\VehiclePositions";
            try
            {
                // Read the GTFS-RT feed into a byte array
                byte[] fileContent = File.ReadAllBytes(filePath);

                // Parse the GTFS-RT FeedMessage from the byte array
                var feedMessage = FeedMessage.Parser.ParseFrom(fileContent);

                // Iterate through each entity in the feed
                foreach (var entity in feedMessage.Entity)
                {
                    // Check if the entity contains a TripUpdate
                    if (entity.Vehicle.HasStopId)
                    {
                        return entity.Vehicle;
                    }
                }
            }
            catch (InvalidProtocolBufferException ex)
            {
                Console.WriteLine($"Error parsing GTFS-RT feed: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }

            return null;
        }

        private Alert GetRTAlert()
        {
            // Path to your GTFS-RT file (e.g., .pb file)
            string filePath = $@"{rtDataPath}\alerts.pb";

            //// Read the GTFS-RT file into a byte array
            byte[] fileContent = File.ReadAllBytes(filePath);

            // Parse the GTFS-RT feed from the byte array
            var feedMessage = FeedMessage.Parser.ParseFrom(fileContent);

            // Iterate through each entity in the feed
            foreach (var entity in feedMessage.Entity)
            {
                // Check if the entity contains a service alert
                if (entity.HasId)
                {
                    return entity.Alert;
                }
            }

            return null;
        }


        private void mapView_Loaded(object sender, RoutedEventArgs e)
        {
            string stopsPath = $@"{stDataPath}\stops.txt";
            string routesPath = $@"{stDataPath}\routes.txt";

            List<Stop> stops = LoadStops(stopsPath);
            List<Route> routes = LoadRoutes(routesPath);

            List<PointLatLng> points = new List<PointLatLng>();
            GMapPolygon polygon = new GMapPolygon(points);

            

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
                List<PointLatLng> routePoints = GetRoutePointsForRoute(route.RouteId, stops);

                List<Point> allPoints = new List<Point>();
                int newPoint = 0;
                foreach (var pointLatLng in routePoints)
                {
                    Point point = new Point(newPoint, newPoint);
                    allPoints.Add(point);
                    newPoint++;
                }

                if (routePoints.Count > 1)
                {
                    var routeLine = new GMapRoute(routePoints);
                    var path = routeLine.CreatePath(allPoints, false);
                    Canvas.SetZIndex(path, 2);
                }

                // add routes to dropdown
            }

            // Add overlay to the map
            // mapView.Overlays.Add(routesOverlay);


            // Focus on the first stop
            if (stops.Count > 0)
            {
                mapView.Position = new PointLatLng(stops[0].Latitude, stops[0].Longitude);
            }
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
