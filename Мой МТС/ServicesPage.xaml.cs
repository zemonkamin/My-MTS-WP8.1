using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Phone.Controls;
using Мой_МТС.Models;
using Мой_МТС.Services;

namespace Мой_МТС
{
    public partial class ServicesPage : PhoneApplicationPage
    {
        public ServicesPage()
        {
            InitializeComponent();
            Loaded += ServicesPage_Loaded;
        }

        private void ServicesPage_Loaded(object sender, RoutedEventArgs e)
        {
            AccountDashboard data = AppServices.LastDashboard;
            if (data == null)
            {
                StatusText.Text = "Вернитесь на главный экран и обновите данные.";
                return;
            }

            CountText.Text = "Подключено: " + data.Services.Count.ToString();
            ServicesStack.Children.Clear();
            for (int i = 0; i < data.Services.Count; i++)
                AddService(data.Services[i]);
            if (data.Services.Count == 0)
                StatusText.Text = "Активные услуги не найдены.";
        }

        private void AddService(ServiceItem item)
        {
            Border card = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 43, 43, 43)),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 12)
            };
            StackPanel stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = item.Name, FontSize = 26, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            stack.Children.Add(new TextBlock { Text = item.Group, FontSize = 22, Foreground = MutedBrush(), Margin = new Thickness(0, 4, 0, 0) });
            stack.Children.Add(new TextBlock { Text = "Плата: " + item.Fee, FontSize = 22, Margin = new Thickness(0, 10, 0, 0) });
            stack.Children.Add(new TextBlock { Text = "Следующая дата: " + item.NextDate, FontSize = 22, Foreground = MutedBrush() });
            card.Child = stack;
            ServicesStack.Children.Add(card);
        }

        private static Brush MutedBrush()
        {
            return new SolidColorBrush(Color.FromArgb(255, 154, 160, 166));
        }
    }
}
