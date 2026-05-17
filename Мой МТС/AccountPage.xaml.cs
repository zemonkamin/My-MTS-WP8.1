using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Phone.Controls;
using Мой_МТС.Models;
using Мой_МТС.Services;

namespace Мой_МТС
{
    public partial class AccountPage : PhoneApplicationPage
    {
        public AccountPage()
        {
            InitializeComponent();
            Loaded += AccountPage_Loaded;
        }

        private void AccountPage_Loaded(object sender, RoutedEventArgs e)
        {
            AccountDashboard data = AppServices.LastDashboard;
            AccountStack.Children.Clear();
            if (data == null)
            {
                AddRow("Статус", "Нет загруженных данных");
                return;
            }

            AddRow("ФИО", data.Name);
            AddRow("Номер", data.Phone);
            AddRow("Регион", data.Region);
            AddRow("Тариф", data.TariffName);
            AddRow("Баланс", data.Balance);
            AddRow("Следующее списание", data.NextChargeAmount + " — " + data.NextChargeDate);
        }

        private void AddRow(string title, string value)
        {
            Border card = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 43, 43, 43)),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 12)
            };
            StackPanel stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = title, FontSize = 22, Foreground = new SolidColorBrush(Color.FromArgb(255, 154, 160, 166)) });
            stack.Children.Add(new TextBlock { Text = value, FontSize = 28, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            card.Child = stack;
            AccountStack.Children.Add(card);
        }

        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            AppServices.Auth.Logout();
            AppServices.LastDashboard = null;
            NavigationService.Navigate(new Uri("/LoginPage.xaml", UriKind.Relative));
        }
    }
}
