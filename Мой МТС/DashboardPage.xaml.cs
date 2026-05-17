using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Shell;
using Мой_МТС.Models;
using Мой_МТС.Services;

namespace Мой_МТС
{
    public partial class DashboardPage : PhoneApplicationPage
    {
        private bool _pageReady;
        private bool _accountLoaded;
        private bool _packagesLoaded;
        private bool _expensesLoaded;
        private bool _servicesLoaded;
        private bool _accountLoading;
        private bool _packagesLoading;
        private bool _expensesLoading;
        private bool _servicesLoading;

        public DashboardPage()
        {
            InitializeComponent();
            Loaded += DashboardPage_Loaded;
        }

        private async void DashboardPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (_pageReady)
                return;
            _pageReady = true;
            await LoadCurrentPivotAsync(false);
        }

        private async void MainPivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_pageReady)
                return;
            await LoadCurrentPivotAsync(false);
        }

        private Task LoadCurrentPivotAsync(bool force)
        {
            int index = MainPivot.SelectedIndex;
            if (index == 0)
                return LoadAccountAsync(force);
            if (index == 1)
                return LoadPackagesAsync(force);
            if (index == 2)
                return LoadExpensesAsync(force);
            if (index == 3)
                return LoadServicesAsync(force);
            return TaskEx.CompletedTask;
        }

        private async Task LoadAccountAsync(bool force)
        {
            if (_accountLoading || (_accountLoaded && !force))
                return;
            _accountLoading = true;
            SetPageState(AccountContent, true);
            AccountStatusText.Text = String.Empty;

            try
            {
                AccountDashboard data = await AppServices.Lk.LoadAccountAsync();
                RenderAccountPage(data);
                _accountLoaded = true;
                AccountStatusText.Text = Safe(data.Warnings) == "—" ? String.Empty : data.Warnings;
            }
            catch (UnauthorizedAccessException)
            {
                HandleUnauthorized();
            }
            catch (Exception ex)
            {
                AccountStatusText.Text = "Не удалось загрузить аккаунт: " + ex.Message;
            }
            finally
            {
                _accountLoading = false;
                SetPageState(AccountContent, false);
            }
        }

        private async Task LoadPackagesAsync(bool force)
        {
            if (_packagesLoading || (_packagesLoaded && !force))
                return;
            _packagesLoading = true;
            SetPageState(PackagesContent, true);
            PackagesStatusText.Text = String.Empty;

            try
            {
                AccountDashboard data = await AppServices.Lk.LoadPackagesAsync();
                RenderPackages(data);
                _packagesLoaded = true;
                PackagesStatusText.Text = Safe(data.Warnings) == "—" ? String.Empty : data.Warnings;
            }
            catch (UnauthorizedAccessException)
            {
                HandleUnauthorized();
            }
            catch (Exception ex)
            {
                PackagesStatusText.Text = "Не удалось загрузить пакеты: " + ex.Message;
            }
            finally
            {
                _packagesLoading = false;
                SetPageState(PackagesContent, false);
            }
        }

        private async Task LoadExpensesAsync(bool force)
        {
            if (_expensesLoading || (_expensesLoaded && !force))
                return;
            _expensesLoading = true;
            SetPageState(ExpensesContent, true);
            ExpensesStatusText.Text = String.Empty;

            try
            {
                AccountDashboard data = await AppServices.Lk.LoadExpensesAsync();
                RenderExpenses(data);
                _expensesLoaded = true;
                ExpensesStatusText.Text = Safe(data.Warnings) == "—" ? String.Empty : data.Warnings;
            }
            catch (UnauthorizedAccessException)
            {
                HandleUnauthorized();
            }
            catch (Exception ex)
            {
                ExpensesStatusText.Text = "Не удалось загрузить расходы: " + ex.Message;
            }
            finally
            {
                _expensesLoading = false;
                SetPageState(ExpensesContent, false);
            }
        }

        private async Task LoadServicesAsync(bool force)
        {
            if (_servicesLoading || (_servicesLoaded && !force))
                return;
            _servicesLoading = true;
            SetPageState(ServicesContent, true);
            ServicesStatusText.Text = String.Empty;

            try
            {
                AccountDashboard data = await AppServices.Lk.LoadServicesAsync();
                RenderServices(data);
                _servicesLoaded = true;
                ServicesStatusText.Text = Safe(data.Warnings) == "—" ? String.Empty : data.Warnings;
            }
            catch (UnauthorizedAccessException)
            {
                HandleUnauthorized();
            }
            catch (Exception ex)
            {
                ServicesStatusText.Text = "Не удалось загрузить услуги: " + ex.Message;
            }
            finally
            {
                _servicesLoading = false;
                SetPageState(ServicesContent, false);
            }
        }

        private void SetPageState(FrameworkElement content, bool isLoading)
        {
            ShowTopLoading(isLoading);
            if (content != null)
                content.Visibility = isLoading ? Visibility.Collapsed : Visibility.Visible;
        }

        private static void ShowTopLoading(bool isLoading)
        {
            ProgressIndicator indicator = SystemTray.ProgressIndicator;
            if (indicator == null)
            {
                indicator = new ProgressIndicator();
                SystemTray.ProgressIndicator = indicator;
            }

            indicator.IsIndeterminate = isLoading;
            indicator.IsVisible = isLoading;
            indicator.Text = isLoading ? "загрузка" : String.Empty;
        }

        private void HandleUnauthorized()
        {
            AppServices.Auth.Logout();
            AppServices.LastDashboard = null;
            NavigationService.Navigate(new Uri("/LoginPage.xaml", UriKind.Relative));
        }

        private void RenderAccountPage(AccountDashboard data)
        {
            BalanceText.Text = Safe(data.Balance);
            TariffNameText.Text = Safe(data.TariffName);
            TariffPriceText.Text = BuildTariffPriceText(data);

            AccountStack.Children.Clear();
            AddAccountRow("ФИО", data.Name);
            AddAccountRow("Номер", data.Phone);
            AddAccountRow("Регион", data.Region);
            AddAccountRow("Тариф", data.TariffName);
            AddAccountRow("Баланс", data.Balance);
        }

        private string BuildTariffPriceText(AccountDashboard data)
        {
            string price = Safe(data.TariffPrice);
            string date = Safe(data.TariffNextChargeDate);
            if (price == "—" && date == "—")
                return "—";
            if (date == "—")
                return price;
            return price + " • спишется " + date;
        }

        private void RenderPackages(AccountDashboard data)
        {
            PackageStack.Children.Clear();
            PackagesEmptyText.Text = String.Empty;

            for (int i = 0; i < data.Packages.Count; i++)
                AddPackage(data.Packages[i]);

            if (data.Packages.Count == 0)
                PackagesEmptyText.Text = "Остатки пакетов не загрузились или пакетов нет.";
        }

        private void AddPackage(PackageItem item)
        {
            Border card = Card();
            StackPanel stack = new StackPanel();

            stack.Children.Add(new TextBlock
            {
                Text = Safe(item.Title),
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });

            Grid grid = new Grid { Margin = new Thickness(0, 9, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            AddPackageCell(grid, 0, 0, "Осталось", item.Remaining);
            AddPackageCell(grid, 0, 1, "Всего", item.Total);
            AddPackageCell(grid, 1, 0, "До", item.Deadline);
            AddPackageCell(grid, 1, 1, "Дней", item.DaysText);

            stack.Children.Add(grid);

            if (item.Percent > 0)
                stack.Children.Add(new ProgressBar { Height = 6, Maximum = 100, Value = item.Percent, Margin = new Thickness(0, 8, 0, 0) });

            AddDivider(stack);
            card.Child = stack;
            PackageStack.Children.Add(card);
        }

        private static void AddPackageCell(Grid grid, int row, int column, string title, string value)
        {
            while (grid.RowDefinitions.Count <= row)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            StackPanel panel = new StackPanel { Margin = new Thickness(column == 0 ? 0 : 8, row == 0 ? 0 : 8, 0, 0) };
            panel.Children.Add(new TextBlock { Text = title, FontSize = 16, Foreground = MutedBrush() });
            panel.Children.Add(new TextBlock { Text = Safe(value), FontSize = 20, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });

            Grid.SetRow(panel, row);
            Grid.SetColumn(panel, column);
            grid.Children.Add(panel);
        }

        private void RenderExpenses(AccountDashboard data)
        {
            ExpenseCategoriesStack.Children.Clear();
            TransactionsStack.Children.Clear();
            ExpensesEmptyText.Text = String.Empty;
            ExpensesSummaryText.Text = "Потрачено: " + Safe(data.ExpensesTotal) + "   Пополнения: " + Safe(data.IncomeTotal);

            for (int i = 0; i < data.ExpenseCategories.Count; i++)
                AddCategory(data.ExpenseCategories[i]);

            if (data.ExpenseCategories.Count == 0)
                AddMuted(ExpenseCategoriesStack, "Расходов по категориям за месяц нет.");

            int maxTransactions = data.Transactions.Count > 12 ? 12 : data.Transactions.Count;
            for (int i = 0; i < maxTransactions; i++)
                AddTransaction(data.Transactions[i]);

            if (data.Transactions.Count == 0)
                ExpensesEmptyText.Text = "Операций за месяц нет.";
        }

        private void AddCategory(ExpenseCategoryItem item)
        {
            Border card = Card();
            StackPanel stack = new StackPanel();
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel left = new StackPanel();
            left.Children.Add(new TextBlock { Text = Safe(item.Name), FontSize = 21, TextWrapping = TextWrapping.Wrap });
            left.Children.Add(new TextBlock { Text = Safe(item.Percent), FontSize = 18, Foreground = MutedBrush(), Margin = new Thickness(0, 4, 0, 0) });
            grid.Children.Add(left);

            TextBlock amount = new TextBlock { Text = Safe(item.Amount), FontSize = 21, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(amount, 1);
            grid.Children.Add(amount);

            stack.Children.Add(grid);
            AddDivider(stack);
            card.Child = stack;
            ExpenseCategoriesStack.Children.Add(card);
        }

        private void AddTransaction(TransactionItem item)
        {
            Border card = Card();
            StackPanel stack = new StackPanel();
            Grid top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock name = new TextBlock { Text = Safe(item.Name), FontSize = 20, TextWrapping = TextWrapping.Wrap };
            top.Children.Add(name);

            TextBlock amount = new TextBlock { Text = Safe(item.Amount), FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 0) };
            Grid.SetColumn(amount, 1);
            top.Children.Add(amount);

            stack.Children.Add(top);
            stack.Children.Add(new TextBlock { Text = Safe(item.Date), FontSize = 17, Foreground = MutedBrush(), Margin = new Thickness(0, 4, 0, 0) });
            if (!String.IsNullOrWhiteSpace(item.Description) && item.Description != "—")
                stack.Children.Add(new TextBlock { Text = item.Description, FontSize = 17, Foreground = MutedBrush(), TextWrapping = TextWrapping.Wrap });

            AddDivider(stack);
            card.Child = stack;
            TransactionsStack.Children.Add(card);
        }

        private void RenderServices(AccountDashboard data)
        {
            ServicesStack.Children.Clear();
            ServicesEmptyText.Text = String.Empty;

            if (!String.IsNullOrWhiteSpace(data.NextChargeTitle) && data.NextChargeTitle != "—")
                NextChargeText.Text = Safe(data.NextChargeAmount) + " — " + Safe(data.NextChargeTitle) + ", " + Safe(data.NextChargeDate);
            else
                NextChargeText.Text = "—";

            for (int i = 0; i < data.Services.Count; i++)
                AddService(data.Services[i]);

            if (data.Services.Count == 0)
                ServicesEmptyText.Text = "Активные услуги не загрузились или услуг нет.";
        }

        private void AddService(ServiceItem item)
        {
            Border card = Card();
            StackPanel stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = Safe(item.Name), FontSize = 21, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            stack.Children.Add(new TextBlock { Text = Safe(item.Group), FontSize = 18, Foreground = MutedBrush(), Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
            stack.Children.Add(new TextBlock { Text = "Плата: " + Safe(item.Fee), FontSize = 19, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap });
            stack.Children.Add(new TextBlock { Text = "Следующая дата: " + Safe(item.NextDate), FontSize = 18, Foreground = MutedBrush(), TextWrapping = TextWrapping.Wrap });
            AddDivider(stack);
            card.Child = stack;
            ServicesStack.Children.Add(card);
        }

        private void AddAccountRow(string title, string value)
        {
            Border card = Card();
            StackPanel stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = title, FontSize = 18, Foreground = MutedBrush() });
            stack.Children.Add(new TextBlock { Text = Safe(value), FontSize = 23, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            AddDivider(stack);
            card.Child = stack;
            AccountStack.Children.Add(card);
        }

        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            AppServices.Auth.Logout();
            AppServices.LastDashboard = null;
            NavigationService.Navigate(new Uri("/LoginPage.xaml", UriKind.Relative));
        }

        private static Border Card()
        {
            return new Border
            {
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0, 9, 0, 0),
                Margin = new Thickness(0, 0, 0, 0)
            };
        }

        private static void AddDivider(StackPanel panel)
        {
            panel.Children.Add(new Border
            {
                Height = 1,
                Background = DividerBrush(),
                Margin = new Thickness(0, 12, 0, 0)
            });
        }

        private static Brush DividerBrush()
        {
            return new SolidColorBrush(Color.FromArgb(255, 42, 42, 42));
        }

        private static Brush MutedBrush()
        {
            return new SolidColorBrush(Color.FromArgb(255, 154, 160, 166));
        }

        private static string Safe(string value)
        {
            if (String.IsNullOrWhiteSpace(value))
                return "—";
            return value.Replace("₽", "руб.");
        }

        private static void AddMuted(StackPanel panel, string text)
        {
            panel.Children.Add(new TextBlock { Text = text, FontSize = 20, Foreground = MutedBrush(), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        }
    }

    internal static class TaskEx
    {
        private static readonly Task Completed = CreateCompletedTask();

        public static Task CompletedTask
        {
            get { return Completed; }
        }

        private static Task CreateCompletedTask()
        {
            TaskCompletionSource<object> source = new TaskCompletionSource<object>();
            source.SetResult(null);
            return source.Task;
        }
    }
}
