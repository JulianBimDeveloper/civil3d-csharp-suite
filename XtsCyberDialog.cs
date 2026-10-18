using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace XTS_Evolution_PB
{
    /// <summary>
    /// Motor de diálogos personalizado estilo MirrorFiles (WPF Borderless).
    /// Diseñado para evitar el look genérico de Windows y proporcionar una experiencia Cyberpunk
    /// con soporte para auto-cierre y modo confirmación.
    /// </summary>
    public class XtsCyberDialog : System.Windows.Window
    {
        private bool _isConfirmMode;
        public bool Result { get; private set; }

        public XtsCyberDialog(string message, string title, bool confirmMode = false, int autoCloseSeconds = 0)
        {
            _isConfirmMode = confirmMode;
            
            // Window setup
            this.Title = title;
            this.Width = 400;
            this.SizeToContent = System.Windows.SizeToContent.Height;
            this.WindowStyle = System.Windows.WindowStyle.None;
            this.AllowsTransparency = true;
            this.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2c3e50"));
            this.Topmost = true;
            this.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;
            this.BorderThickness = new System.Windows.Thickness(1);
            this.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#34495e"));
            this.Padding = new System.Windows.Thickness(20);

            // Container
            var stack = new System.Windows.Controls.StackPanel();
            
            // Header with Close Button
            var header = new System.Windows.Controls.DockPanel { LastChildFill = false, Margin = new System.Windows.Thickness(0, 0, 0, 10) };
            var titleLabel = new System.Windows.Controls.TextBlock
            {
                Text = title.ToUpper(),
                Foreground = System.Windows.Media.Brushes.Cyan,
                FontSize = 10,
                FontWeight = System.Windows.FontWeights.Bold,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI")
            };
            var closeBtn = new System.Windows.Controls.Button
            {
                Content = "✕",
                Width = 20,
                Height = 20,
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new System.Windows.Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            closeBtn.Click += (s, e) => { Result = false; this.Close(); };
            
            System.Windows.Controls.DockPanel.SetDock(titleLabel, System.Windows.Controls.Dock.Left);
            System.Windows.Controls.DockPanel.SetDock(closeBtn, System.Windows.Controls.Dock.Right);
            header.Children.Add(titleLabel);
            header.Children.Add(closeBtn);
            stack.Children.Add(header);

            // Message
            var msgText = new System.Windows.Controls.TextBlock
            {
                Text = message,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 13,
                TextWrapping = System.Windows.TextWrapping.Wrap,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                LineHeight = 18,
                Margin = new System.Windows.Thickness(0, 10, 0, 20)
            };
            stack.Children.Add(msgText);

            // Buttons Area
            var btnPanel = new System.Windows.Controls.StackPanel 
            { 
                Orientation = System.Windows.Controls.Orientation.Horizontal, 
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right 
            };
            
            if (confirmMode)
            {
                var btnYes = CreateStyledButton("FORZAR PROTOCOLO (SÍ)", true);
                var btnNo = CreateStyledButton("CANCELAR (NO)", false);
                btnYes.Click += (s, e) => { Result = true; this.Close(); };
                btnNo.Click += (s, e) => { Result = false; this.Close(); };
                btnPanel.Children.Add(btnYes);
                btnPanel.Children.Add(btnNo);
            }
            else
            {
                var btnOk = CreateStyledButton("ENTENDIDO", true);
                btnOk.Click += (s, e) => { Result = true; this.Close(); };
                btnPanel.Children.Add(btnOk);
            }
            
            stack.Children.Add(btnPanel);
            this.Content = new System.Windows.Controls.Border { Child = stack, Padding = new System.Windows.Thickness(20) };

            // Auto-close handling
            if (autoCloseSeconds > 0 && !confirmMode)
            {
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(autoCloseSeconds) };
                timer.Tick += (s, e) => { timer.Stop(); this.Close(); };
                timer.Start();
            }

            // Drag support
            this.MouseDown += (s, e) => { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) this.DragMove(); };
        }

        private System.Windows.Controls.Button CreateStyledButton(string text, bool isPrimary)
        {
            var btn = new System.Windows.Controls.Button
            {
                Content = text,
                Padding = new System.Windows.Thickness(15, 6, 15, 6),
                Margin = new System.Windows.Thickness(5, 0, 0, 0),
                Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(isPrimary ? "#1abc9c" : "#34495e")),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new System.Windows.Thickness(0),
                FontSize = 11,
                FontWeight = System.Windows.FontWeights.Bold,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            return btn;
        }

        public static bool Show(string message, string title, bool confirmMode = false, int autoCloseSeconds = 0)
        {
            var dialog = new XtsCyberDialog(message, title, confirmMode, autoCloseSeconds);
            dialog.ShowDialog();
            return dialog.Result;
        }
    }
}
