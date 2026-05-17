using System;
using System.Windows;
using Microsoft.Phone.Controls;
using Мой_МТС.Services;

namespace Мой_МТС
{
    public partial class OtpPage : PhoneApplicationPage
    {
        public OtpPage()
        {
            InitializeComponent();
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            LoginButton.IsEnabled = false;
            StatusText.Text = "Проверяем код и сохраняем вход...";
            try
            {
                await AppServices.Auth.CompleteOtpAsync(OtpBox.Text);
                NavigationService.Navigate(new Uri("/DashboardPage.xaml", UriKind.Relative));
            }
            catch (Exception ex)
            {
                StatusText.Text = ex.Message;
                LoginButton.IsEnabled = true;
            }
        }
    }
}
