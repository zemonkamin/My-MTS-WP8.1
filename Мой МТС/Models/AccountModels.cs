using System;
using System.Collections.Generic;

namespace Мой_МТС.Models
{
    public sealed class AccountDashboard
    {
        public AccountDashboard()
        {
            Packages = new List<PackageItem>();
            Services = new List<ServiceItem>();
            ExpenseCategories = new List<ExpenseCategoryItem>();
            Transactions = new List<TransactionItem>();
        }

        public string Name { get; set; }
        public string Phone { get; set; }
        public string Region { get; set; }
        public string TariffName { get; set; }
        public string TariffPrice { get; set; }
        public string TariffNextChargeDate { get; set; }
        public string Balance { get; set; }
        public string MonthName { get; set; }
        public string ExpensesTotal { get; set; }
        public string IncomeTotal { get; set; }
        public string NextChargeTitle { get; set; }
        public string NextChargeAmount { get; set; }
        public string NextChargeDate { get; set; }
        public string Warnings { get; set; }
        public List<PackageItem> Packages { get; private set; }
        public List<ServiceItem> Services { get; private set; }
        public List<ExpenseCategoryItem> ExpenseCategories { get; private set; }
        public List<TransactionItem> Transactions { get; private set; }
    }

    public sealed class PackageItem
    {
        public string Title { get; set; }
        public string Remaining { get; set; }
        public string Total { get; set; }
        public string Deadline { get; set; }
        public string DaysText { get; set; }
        public double Percent { get; set; }
    }

    public sealed class ServiceItem
    {
        public string Name { get; set; }
        public string Group { get; set; }
        public string Fee { get; set; }
        public string NextDate { get; set; }
    }

    public sealed class ExpenseCategoryItem
    {
        public string Name { get; set; }
        public string Amount { get; set; }
        public string Percent { get; set; }
    }

    public sealed class TransactionItem
    {
        public string Date { get; set; }
        public string Name { get; set; }
        public string Amount { get; set; }
        public string Description { get; set; }
    }
}
