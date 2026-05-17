using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Phone.Controls;
using Мой_МТС.Models;
using Мой_МТС.Services;

namespace Мой_МТС
{
    public partial class ExpensesPage : PhoneApplicationPage
    {
        public ExpensesPage()
        {
            InitializeComponent();
            Loaded += ExpensesPage_Loaded;
        }

        private void ExpensesPage_Loaded(object sender, RoutedEventArgs e)
        {
            AccountDashboard data = AppServices.LastDashboard;
            if (data == null)
            {
                StatusText.Text = "Вернитесь на главный экран и обновите данные.";
                return;
            }

            TotalText.Text = "Потрачено: " + data.ExpensesTotal + "   Пополнения: " + data.IncomeTotal;
            CategoriesStack.Children.Clear();
            TransactionsStack.Children.Clear();

            for (int i = 0; i < data.ExpenseCategories.Count; i++)
                AddCategory(data.ExpenseCategories[i]);

            if (data.ExpenseCategories.Count == 0)
                AddMuted(CategoriesStack, "Расходов по категориям за месяц нет.");

            for (int i = 0; i < data.Transactions.Count; i++)
                AddTransaction(data.Transactions[i]);

            if (data.Transactions.Count == 0)
                AddMuted(TransactionsStack, "Операций за месяц нет.");
        }

        private void AddCategory(ExpenseCategoryItem item)
        {
            Border card = Card();
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel left = new StackPanel();
            left.Children.Add(new TextBlock { Text = item.Name, FontSize = 26, TextWrapping = TextWrapping.Wrap });
            left.Children.Add(new TextBlock { Text = item.Percent, FontSize = 22, Foreground = MutedBrush(), Margin = new Thickness(0, 4, 0, 0) });
            grid.Children.Add(left);

            TextBlock amount = new TextBlock { Text = item.Amount, FontSize = 26, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(amount, 1);
            grid.Children.Add(amount);
            card.Child = grid;
            CategoriesStack.Children.Add(card);
        }

        private void AddTransaction(TransactionItem item)
        {
            Border card = Card();
            StackPanel stack = new StackPanel();
            Grid top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock name = new TextBlock { Text = item.Name, FontSize = 24, TextWrapping = TextWrapping.Wrap };
            top.Children.Add(name);
            TextBlock amount = new TextBlock { Text = item.Amount, FontSize = 24, FontWeight = FontWeights.SemiBold };
            Grid.SetColumn(amount, 1);
            top.Children.Add(amount);
            stack.Children.Add(top);
            stack.Children.Add(new TextBlock { Text = item.Date, FontSize = 20, Foreground = MutedBrush(), Margin = new Thickness(0, 4, 0, 0) });
            stack.Children.Add(new TextBlock { Text = item.Description, FontSize = 20, Foreground = MutedBrush(), TextWrapping = TextWrapping.Wrap });
            card.Child = stack;
            TransactionsStack.Children.Add(card);
        }

        private static Border Card()
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 43, 43, 43)),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 12)
            };
        }

        private static Brush MutedBrush()
        {
            return new SolidColorBrush(Color.FromArgb(255, 154, 160, 166));
        }

        private static void AddMuted(StackPanel panel, string text)
        {
            panel.Children.Add(new TextBlock { Text = text, FontSize = 22, Foreground = MutedBrush(), TextWrapping = TextWrapping.Wrap });
        }
    }
}
