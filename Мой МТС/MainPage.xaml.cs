using System;
using System.Windows.Navigation;
using Microsoft.Phone.Controls;
using Мой_МТС.Services;

namespace Мой_МТС
{
    public partial class MainPage : PhoneApplicationPage
    {
        private bool _routing;

        public MainPage()
        {
            InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (_routing)
                return;

            _routing = true;
            string target = "/LoginPage.xaml";

            if (AppServices.Auth.HasSavedSession)
            {
                try
                {
                    bool restored = await AppServices.Auth.RestoreSavedSessionAsync();
                    if (restored)
                        target = "/DashboardPage.xaml";
                    else
                        AppServices.Auth.Logout();
                }
                catch
                {
                    // При временной сетевой ошибке не уничтожаем сохранённую сессию.
                    // Dashboard сам покажет ошибку загрузки и повторит refresh при следующем запросе.
                    target = "/DashboardPage.xaml";
                }
            }

            NavigationService.Navigate(new Uri(target, UriKind.Relative));
        }
    }
}
