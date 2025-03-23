using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SEITHackathonProject
{

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // constants
        private SolidColorBrush TabSelectColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB8B4B4"));
        private const int InfoUpYPos = 80;
        private const int InfoDownYPos = 470;

        // variables
        private bool routeInfoShown = true;


        public MainWindow()
        {
            InitializeComponent();
        }

        private void mapView_Loaded(object sender, RoutedEventArgs e)
        {
            GMap.NET.GMaps.Instance.Mode = GMap.NET.AccessMode.ServerAndCache;
            // choose your provider here
            mapView.MapProvider = GMap.NET.MapProviders.OpenStreetMapProvider.Instance;
            mapView.MinZoom = 2;
            mapView.MaxZoom = 17;
            // whole world zoom
            mapView.Zoom = 2;
            // lets the map use the mousewheel to zoom
            mapView.MouseWheelZoomType = GMap.NET.MouseWheelZoomType.MousePositionAndCenter;
            // lets the user drag the map
            mapView.CanDragMap = true;
            // lets the user drag the map with the left mouse button
            mapView.DragButton = MouseButton.Left;
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
                    Duration = new Duration(TimeSpan.FromSeconds(0.3))
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
        }

        private void SuggstRouteBtn_Click(object sender, RoutedEventArgs e)
        {
            SuggstRouteBtn.Background = TabSelectColor;
            CrrntRouteBtn.Background = null;
        }
    }
}
