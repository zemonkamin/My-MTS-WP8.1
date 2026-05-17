using System;
using System.Windows;
using Microsoft.Phone.Controls;
using Мой_МТС.Services;

namespace Мой_МТС
{
    public partial class LoginPage : PhoneApplicationPage
    {
        public LoginPage()
        {
            InitializeComponent();
        }

        private async void SmsButton_Click(object sender, RoutedEventArgs e)
        {
            SmsButton.IsEnabled = false;
            StatusText.Text = "Отправляем запрос в МТС ID...";
            try
            {
                string phone = BuildPhoneNumber();
                await AppServices.Auth.BeginLoginAsync(phone);
                StatusText.Text = "SMS-код отправлен.";
                NavigationService.Navigate(new Uri("/OtpPage.xaml", UriKind.Relative));
            }
            catch (Exception ex)
            {
                StatusText.Text = ex.Message;
                SmsButton.IsEnabled = true;
            }
        }

        private string BuildPhoneNumber()
        {
            string digits = OnlyDigits(PhoneTailBox.Text);

            if (digits.Length == 10)
                return "7" + digits;

            // На случай, если пользователь вставил полный номер в правое поле.
            if (digits.Length == 11 && digits.StartsWith("7", StringComparison.Ordinal))
                return digits;

            if (digits.Length == 11 && digits.StartsWith("8", StringComparison.Ordinal))
                return "7" + digits.Substring(1);

            throw new InvalidOperationException("Введите 10 цифр номера после +7.");
        }

        private static string OnlyDigits(string value)
        {
            if (String.IsNullOrEmpty(value))
                return String.Empty;

            char[] buffer = new char[value.Length];
            int count = 0;
            for (int i = 0; i < value.Length; i++)
            {
                if (Char.IsDigit(value[i]))
                    buffer[count++] = value[i];
            }
            return new String(buffer, 0, count);
        }
    }
}
