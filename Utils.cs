using System;
using System.Windows.Media;

namespace XTS_Evolution_PB
{
    public static class Utils
    {
        public enum IconType
        {
            Export,
            Import,
            Search,
            Copy,
            Sync,
            ID,
            Filter,
            Database,
            Check,
            Reset,
            Empty,
            Duplicate,
            CloudSync,
            XTS,
            Pipe,
            Junction,
            Material
        }

        public static ImageSource GetVectorIcon(IconType type)
        {
            // Rutas minimalistas (Material Style)
            string outPathData = type switch
            {
                IconType.Export   => "M5,20H19V18H5M12,2L4,10H9V16H15V10H20L12,2Z", // Flecha arriba
                IconType.Import   => "M5,2H19V4H5M12,22L20,14H15V8H9V14H4L12,22Z", // Flecha abajo
                IconType.Sync     => "M12,4V1L8,5L12,9V6A6,6 0 0,1 18,12A6,6 0 0,1 12,18A6,6 0 0,1 6,12C6,10.94 6.28,9.95 6.75,9.1L5.31,7.67C4.5,8.89 4,10.39 4,12A8,8 0 0,0 12,20V23L16,19L12,15V18Z",
                IconType.CloudSync => "M19.35,10.04C18.67,6.59 15.64,4 12,4C9.11,4 6.6,5.64 5.35,8.04C2.34,8.36 0,10.91 0,14A6,6 0 0,0 6,20H19A5,5 0 0,0 24,15C24,12.36 21.95,10.22 19.35,10.04M19,18H6A4,4 0 0,1 6,10C6.7,10 7.3,10.1 7.9,10.3L9.2,10.7L9.9,9.4C10.6,8 11.2,7.4 12,7.4C14.2,7.4 16,9.2 16,11.4V13H19A3,3 0 0,1 22,16A3,3 0 0,1 19,18M12,11L8,15H11V19H13V15H16L12,11Z",
                IconType.XTS      => "M21,16.5C21,16.88 20.79,17.21 20.47,17.38L12.57,21.82C12.41,21.94 12.21,22 12,22C11.79,22 11.59,21.94 11.43,21.82L3.53,17.38C3.21,17.21 3,16.88 3,16.5V7.5C3,7.12 3.21,6.79 3.53,6.62L11.43,2.18C11.59,2.06 11.79,2 12,2C12.21,2 12.41,2.06 12.57,2.18L20.47,6.62C20.79,6.79 21,7.12 21,7.5V16.5M12,4.15L5,8.09V15.91L12,19.85L19,15.91V8.09L12,4.15M12,7L15,9V12L12,14L9,12V9L12,7Z", // Hexágono Tech con núcleo
                IconType.Search   => "M9.5,3A6.5,6.5 0 0,1 16,9.5C16,11.11 15.41,12.59 14.44,13.73L14.71,14H15.5L20.5,19L19,20.5L14,15.5V14.71L13.73,14.44C12.59,15.41 11.11,16 9.5,16A6.5,6.5 0 0,1 3,9.5A6.5,6.5 0 0,1 9.5,3M9.5,5A4.5,4.5 0 0,0 5,9.5A4.5,4.5 0 0,0 9.5,14A4.5,4.5 0 0,0 14,9.5A4.5,4.5 0 0,0 9.5,5Z",
                IconType.Copy     => "M16,1H4C2.89,1 2,1.89 2,3V17H4V3H16V1M19,5H8C6.89,5 6,5.89 6,7V21C6,22.11 6.89,23 8,23H19C20.11,23 21,22.11 21,21V7C21,5.89 20.11,5 19,5M19,21H8V7H19V21Z",
                IconType.ID       => "M12,12C14.21,12 16,10.21 16,8C16,5.79 14.21,4 12,4C9.79,4 8,5.79 8,8C8,10.21 9.79,12 12,12M12,14C9.33,14 4,15.34 4,18V20H20V18C20,15.34 14.67,14 12,14Z",
                IconType.Filter   => "M10,18H14V16H10V18M3,6V8H21V6H3M6,13H18V11H6V13Z",
                IconType.Database => "M12,3C7.58,3 4,4.79 4,7C4,9.21 7.58,11 12,11C16.42,11 20,9.21 20,7C20,4.79 16.42,3 12,3M4,9V12C4,14.21 7.58,16 12,16C16.42,16 20,14.21 20,12V9C20,11.21 16.42,13 12,13C7.58,13 4,11.21 4,9M4,14V17C4,19.21 7.58,21 12,21C16.42,21 20,19.21 20,17V14C20,16.21 16.42,18 12,18C7.58,18 4,16.21 4,14Z",
                IconType.Check    => "M12,2L4,5V11C4,16.55 7.84,21.74 12,23C16.16,21.74 20,16.55 20,11V5L12,2M10,17L6,13L7.41,11.59L10,14.17L16.59,7.58L18,9L10,17Z",
                IconType.Reset    => "M17.65,6.35C16.2,4.9 14.21,4 12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20C15.73,20 18.84,17.45 19.73,14H17.65C16.83,16.33 14.61,18 12,18A6,6 0 0,1 6,12A6,6 0 0,1 12,6C13.66,6 15.14,6.69 16.22,7.78L13,11H20V4L17.65,6.35Z",
                IconType.Empty    => "M19,6.41L17.59,5L12,10.59L6.41,5L5,6.41L10.59,12L5,17.59L6.41,19L12,13.41L17.59,19L19,17.59L13.41,12L19,6.41Z",
                IconType.Duplicate => "M16,3H4V17H2V3C2,1.9 2.9,1 4,1H16V3M19,7V21C19,22.1 18.1,23 17,23H8C6.9,23 6,22.1 6,21V7C6,5.9 6.9,5 8,5H17C18.1,5 19,5.9 19,7M17,7H8V21H17V7Z",
                // Tubería horizontal con sección circular en extremos
                IconType.Pipe     => "M4,8H20C21.1,8 22,8.9 22,10V14C22,15.1 21.1,16 20,16H4C2.9,16 2,15.1 2,14V10C2,8.9 2.9,8 4,8M4,10V14H20V10H4M6,11A1,1 0 0,0 5,12A1,1 0 0,0 6,13A1,1 0 0,0 7,12A1,1 0 0,0 6,11M18,11A1,1 0 0,0 17,12A1,1 0 0,0 18,13A1,1 0 0,0 19,12A1,1 0 0,0 18,11Z",
                // Pozo/sumidero visto desde arriba: círculo exterior con cuadrado interior
                IconType.Junction => "M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2M12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20A8,8 0 0,0 20,12A8,8 0 0,0 12,4M9,9H15V15H9V9M11,11V13H13V11H11Z",
                // Cubo de pintura / material
                IconType.Material => "M16.56,8.94L7.62,0L6.21,1.41L8.59,3.79L3.44,8.94C2.85,9.53 2.85,10.47 3.44,11.06L8.94,16.56C9.23,16.85 9.62,17 10,17C10.38,17 10.77,16.85 11.06,16.56L16.56,11.06C17.15,10.47 17.15,9.53 16.56,8.94M5.21,10L10,5.21L14.79,10H5.21M19,11.5C19,11.5 17,13.67 17,15A2,2 0 0,0 19,17A2,2 0 0,0 21,15C21,13.67 19,11.5 19,11.5Z",
                _                 => "M12,2L4.5,20.29L5.21,21L12,18L18.79,21L19.5,20.29L12,2Z"
            };

            var geometry = System.Windows.Media.Geometry.Parse(outPathData);
            var drawingGroup = new System.Windows.Media.DrawingGroup();

            // 1. CAPA PRINCIPAL (Blanco sutil)
            var brush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#ecf0f1"));
            drawingGroup.Children.Add(new System.Windows.Media.GeometryDrawing(brush, null, geometry));

            // 2. CAJA DE VISTA (Estándar 24x24)
            drawingGroup.Children.Add(new System.Windows.Media.GeometryDrawing(System.Windows.Media.Brushes.Transparent, null, new System.Windows.Media.RectangleGeometry(new System.Windows.Rect(0, 0, 24, 24))));

            return new System.Windows.Media.DrawingImage(drawingGroup);
        }
    }
}
