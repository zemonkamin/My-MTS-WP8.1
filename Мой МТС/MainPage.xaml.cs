using System;
using System.Windows.Navigation;
using Microsoft.Phone.Controls;
using Мой_МТС.Services;

namespace Мой_МТС
{
    public partial class MainPage : PhoneApplicationPage
    {
        public MainPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            string target = AppServices.Auth.HasSavedSession ? "/DashboardPage.xaml" : "/LoginPage.xaml";
            NavigationService.Navigate(new Uri(target, UriKind.Relative));
        }
    }
}
