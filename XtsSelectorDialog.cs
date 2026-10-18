using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Collections.Generic;
using System.Windows.Input;

namespace XTS_Evolution_PB
{
    public class XtsSelectorDialog : Window
    {
        private System.Windows.Controls.ListBox _listBox;
        public string SelectedValue { get; private set; }

        public XtsSelectorDialog(string title, List<string> items)
        {
            Title = title;
            Width = 450;
            Height = 350;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;

            // Fuentes y Colores
            var bgColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1a1a1a"); // Un poco más oscuro
            var borderColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#333333");
            var accentColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#ffffff");

            // Main Border
            var mainBorder = new Border
            {
                Background = new SolidColorBrush(bgColor),
                BorderBrush = new SolidColorBrush(borderColor),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20)
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Header
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleBlock = new TextBlock
            {
                Text = title.ToUpper(),
                Foreground = new SolidColorBrush(accentColor),
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI Semibold"),
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 15)
            };
            Grid.SetColumn(titleBlock, 0);
            headerGrid.Children.Add(titleBlock);

            var closeBtn = new System.Windows.Controls.Button
            {
                Content = "×",
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = System.Windows.Media.Brushes.Gray,
                BorderBrush = System.Windows.Media.Brushes.Transparent,
                FontSize = 20,
                Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Top
            };
            closeBtn.Click += (s, e) => Close();
            Grid.SetColumn(closeBtn, 1);
            headerGrid.Children.Add(closeBtn);

            Grid.SetRow(headerGrid, 0);
            grid.Children.Add(headerGrid);

            // ListBox
            _listBox = new System.Windows.Controls.ListBox
            {
                ItemsSource = items,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = System.Windows.Media.Brushes.White,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 15)
            };

            var itemStyle = new Style(typeof(ListBoxItem));
            itemStyle.Setters.Add(new Setter(ListBoxItem.PaddingProperty, new Thickness(12, 10, 12, 10)));
            itemStyle.Setters.Add(new Setter(ListBoxItem.TemplateProperty, CreateItemTemplate()));
            _listBox.ItemContainerStyle = itemStyle;

            Grid.SetRow(_listBox, 1);
            grid.Children.Add(_listBox);

            // Footer
            var copyBtn = new System.Windows.Controls.Button
            {
                Content = "COPY TO CLIPBOARD",
                Height = 40,
                Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#252525")),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#444444")),
                BorderThickness = new Thickness(1),
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI Bold"),
                FontSize = 11,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            copyBtn.Click += (s, e) => {
                if (_listBox.SelectedItem != null) {
                    SelectedValue = _listBox.SelectedItem.ToString();
                    DialogResult = true;
                    Close();
                }
            };

            Grid.SetRow(copyBtn, 2);
            grid.Children.Add(copyBtn);

            mainBorder.Child = grid;
            Content = mainBorder;

            _listBox.MouseDoubleClick += (s, e) => {
                if (_listBox.SelectedItem != null) {
                    SelectedValue = _listBox.SelectedItem.ToString();
                    DialogResult = true;
                    Close();
                }
            };

            mainBorder.MouseDown += (s, e) => {
                if (e.ChangedButton == System.Windows.Input.MouseButton.Left) DragMove();
            };
        }

        private ControlTemplate CreateItemTemplate()
        {
            var template = new ControlTemplate(typeof(ListBoxItem));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Bd";
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(ListBoxItem.PaddingProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            border.SetValue(Border.MarginProperty, new Thickness(0, 1, 0, 1));

            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            border.AppendChild(contentPresenter);

            var trigger = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            trigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0078d7")), "Bd"));

            var mouseOverTrigger = new Trigger { Property = ListBoxItem.IsMouseOverProperty, Value = true };
            mouseOverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(System.Windows.Media.Color.FromArgb(30, 255, 255, 255)), "Bd"));

            template.VisualTree = border;
            template.Triggers.Add(trigger);
            template.Triggers.Add(mouseOverTrigger);
            return template;
        }

        public static string Show(string title, List<string> items)
        {
            var dialog = new XtsSelectorDialog(title, items);
            if (dialog.ShowDialog() == true)
            {
                return dialog.SelectedValue;
            }
            return null;
        }
    }
}
