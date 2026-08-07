using System;
using System.Collections.Generic;
using System.Globalization;
using System.Json;
using System.Threading.Tasks;
using Мой_МТС.Models;
using Мой_МТС.Utilities;

namespace Мой_МТС.Services
{
    public sealed class MtsLkService
    {
        private const string LkBase = "https://lk.mts.ru";
        private const string GraphQlUrl = "https://federation.mts.ru/graphql";

        private const string BalanceQuery = @"query GetBalanceBaseQueryInput {
  balances {
    nodes {
      ... on BalanceInfo {
        name
        code
        status
        remainingValue { amount currency }
      }
      ... on BalanceError {
        error { message code }
      }
    }
  }
}";

        private const string TariffInfoQuery = @"query GetProductsTariffInfo($screenName: String!) {
  tariffInfo(input: {screenName: $screenName}) {
    name
    alias
    tariffPricesInfo { mainPrice { price basePrice date descriptionPrice } }
    tariffContent { recommendedAmount { amount text title } }
  }
}";

        private const string ExpensesBaseQuery = @"query GetExpensesBaseAndCategoryInfoQueryInput($pageIndex: Int!, $filter: TransactionsFilterInput!) {
  transactionsByFilter(input: {pageIndex: $pageIndex, filter: $filter}) {
    totalInfo { currency incomeAmount outcomeAmount period { dateFrom dateTo } }
    categories { alias value name color amount percentage }
  }
}";

        private const string ExpensesTransactionsQuery = @"query GetExpensesInfoQueryInput($pageIndex: Int!, $filter: TransactionsFilterInput!) {
  transactionsByFilter(input: {pageIndex: $pageIndex, filter: $filter}) {
    transactions { name type subtitle dateTime unitValue value direction amount description productType }
    totalPages
  }
}";

        private readonly MtsHttpClient _http;
        private readonly MtsAuthService _auth;

        public MtsLkService(MtsHttpClient http, MtsAuthService auth)
        {
            _http = http;
            _auth = auth;
        }

        public async Task<AccountDashboard> LoadAccountAsync()
        {
            List<string> warnings = new List<string>();
            JsonValue userInfo = await TryJsonAsync("профиль", delegate { return LkGetJsonAsync("/api/login/user-info"); }, warnings);
            JsonValue profile = await TryJsonAsync("аккаунт", delegate { return LkGetJsonAsync("/api/login/profile"); }, warnings);
            JsonValue balances = await TryJsonAsync("баланс", delegate { return GraphQlAsync("GetBalanceBaseQueryInput", BalanceQuery, new JsonObject()); }, warnings);
            JsonValue tariffInfo = await TryJsonAsync("тариф", delegate { return GraphQlAsync("GetProductsTariffInfo", TariffInfoQuery, TariffVariables()); }, warnings);

            AccountDashboard dashboard = new AccountDashboard();
            FillAccount(dashboard, userInfo, profile, balances, tariffInfo, null);
            dashboard.Warnings = BuildWarningText(warnings);
            return dashboard;
        }

        public async Task<AccountDashboard> LoadPackagesAsync()
        {
            List<string> warnings = new List<string>();
            JsonValue tariffSummary = await TryJsonAsync("тарифные пакеты", delegate { return LongTaskAsync("/api/tariff/summary", "api/tariff/summary", 5); }, warnings);
            JsonValue counters = await TryJsonAsync("остатки пакетов", delegate { return LongTaskAsync("/api/sharing/counters", "api/sharing/counters", 5); }, warnings);

            AccountDashboard dashboard = new AccountDashboard();
            FillCounters(dashboard, counters, tariffSummary);
            if (dashboard.Packages.Count > 0)
                warnings.Clear();

            dashboard.Warnings = BuildWarningText(warnings);
            return dashboard;
        }

        public async Task<AccountDashboard> LoadExpensesAsync()
        {
            List<string> warnings = new List<string>();
            JsonValue expenses = await TryJsonAsync("расходы", delegate { return LoadExpensesDataAsync(1); }, warnings);

            AccountDashboard dashboard = new AccountDashboard();
            dashboard.MonthName = TextUtil.CurrentMonthName();
            FillExpensesFromParts(dashboard, expenses);
            dashboard.Warnings = BuildWarningText(warnings);
            return dashboard;
        }

        public async Task<AccountDashboard> LoadServicesAsync()
        {
            List<string> warnings = new List<string>();
            JsonValue services = await TryJsonAsync("услуги и списания", delegate { return LongTaskAsync("/api/services/list/active", "api/services/list/active", 4); }, warnings);
            JsonValue tariffInfo = await TryJsonAsync("тариф", delegate { return GraphQlAsync("GetProductsTariffInfo", TariffInfoQuery, TariffVariables()); }, warnings);

            AccountDashboard dashboard = new AccountDashboard();
            FillServicesAndCharges(dashboard, services, tariffInfo);
            dashboard.Warnings = BuildWarningText(warnings);
            return dashboard;
        }
        public async Task<AccountDashboard> LoadDashboardAsync()
        {
            AccountDashboard result = new AccountDashboard();
            AccountDashboard account = await LoadAccountAsync();
            AccountDashboard packages = await LoadPackagesAsync();
            AccountDashboard expenses = await LoadExpensesAsync();
            AccountDashboard services = await LoadServicesAsync();

            result.Name = account.Name;
            result.Phone = account.Phone;
            result.Region = account.Region;
            result.TariffName = account.TariffName;
            result.TariffPrice = account.TariffPrice;
            result.TariffNextChargeDate = account.TariffNextChargeDate;
            result.Balance = account.Balance;
            result.MonthName = TextUtil.CurrentMonthName();
            result.ExpensesTotal = expenses.ExpensesTotal;
            result.IncomeTotal = expenses.IncomeTotal;
            result.NextChargeTitle = services.NextChargeTitle;
            result.NextChargeAmount = services.NextChargeAmount;
            result.NextChargeDate = services.NextChargeDate;
            CopyPackages(packages, result);
            CopyExpenses(expenses, result);
            CopyServices(services, result);
            result.Warnings = JoinWarnings(account.Warnings, packages.Warnings, expenses.Warnings, services.Warnings);
            return result;
        }

        public async Task<List<TransactionItem>> LoadMoreExpensesAsync(int pageIndex)
        {
            try
            {
                JsonValue expenses = await LoadExpensesPageAsync(pageIndex < 1 ? 1 : pageIndex);
                string currency = FirstNotEmpty(JsonUtil.String(JsonUtil.Get(expenses, "transactionsByFilter", "totalInfo", "currency")), "руб.");
                List<TransactionItem> result = new List<TransactionItem>();
                FillTransactions(result, JsonUtil.Array(JsonUtil.Get(expenses, "transactionsByFilter", "transactions")), currency);
                return result;
            }
            catch (UnauthorizedAccessException)
            {
                throw;
            }
            catch
            {
                return new List<TransactionItem>();
            }
        }

        private static void CopyPackages(AccountDashboard source, AccountDashboard target)
        {
            if (source == null)
                return;
            for (int i = 0; i < source.Packages.Count; i++)
                target.Packages.Add(source.Packages[i]);
        }

        private static void CopyExpenses(AccountDashboard source, AccountDashboard target)
        {
            if (source == null)
                return;
            target.ExpensesTotal = source.ExpensesTotal;
            target.IncomeTotal = source.IncomeTotal;
            for (int i = 0; i < source.ExpenseCategories.Count; i++)
                target.ExpenseCategories.Add(source.ExpenseCategories[i]);
            for (int i = 0; i < source.Transactions.Count; i++)
                target.Transactions.Add(source.Transactions[i]);
        }

        private static void CopyServices(AccountDashboard source, AccountDashboard target)
        {
            if (source == null)
                return;
            target.NextChargeTitle = source.NextChargeTitle;
            target.NextChargeAmount = source.NextChargeAmount;
            target.NextChargeDate = source.NextChargeDate;
            for (int i = 0; i < source.Services.Count; i++)
                target.Services.Add(source.Services[i]);
        }

        private static async Task<JsonValue> TryJsonAsync(string title, Func<Task<JsonValue>> action, List<string> warnings)
        {
            try
            {
                return await action();
            }
            catch (UnauthorizedAccessException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (warnings != null)
                    warnings.Add(title + " — " + ShortError(ex));
                return null;
            }
        }

        private static string BuildWarningText(List<string> warnings)
        {
            if (warnings == null || warnings.Count == 0)
                return String.Empty;
            return "Часть данных МТС не загрузилась: " + String.Join("; ", warnings.ToArray());
        }

        private static string JoinWarnings(params string[] values)
        {
            List<string> parts = new List<string>();
            for (int i = 0; i < values.Length; i++)
            {
                if (!String.IsNullOrWhiteSpace(values[i]))
                    parts.Add(values[i]);
            }
            return parts.Count == 0 ? String.Empty : String.Join("; ", parts.ToArray());
        }

        private static string ShortError(Exception ex)
        {
            if (ex == null || String.IsNullOrWhiteSpace(ex.Message))
                return "ошибка";
            string text = ex.Message.Replace("\r", " ").Replace("\n", " ");
            return text.Length > 80 ? text.Substring(0, 80) + "..." : text;
        }

        private async Task<HttpResult> GetWithSessionAsync(string url, IDictionary<string, string> headers)
        {
            await _auth.TryRefreshSessionIfNeededAsync();

            HttpResult result = await _http.GetAsync(url, headers);
            if (MtsAuthService.IsSessionRejected(result) && await _auth.TryRefreshSessionAsync())
                result = await _http.GetAsync(url, headers);

            return result;
        }

        private async Task<HttpResult> PostJsonWithSessionAsync(string url, string json, IDictionary<string, string> headers)
        {
            await _auth.TryRefreshSessionIfNeededAsync();

            HttpResult result = await _http.PostJsonAsync(url, json, headers);
            if (MtsAuthService.IsSessionRejected(result) && await _auth.TryRefreshSessionAsync())
                result = await _http.PostJsonAsync(url, json, headers);

            return result;
        }

        private async Task<JsonValue> LkGetJsonAsync(string path)
        {
            HttpResult result = await GetWithSessionAsync(BuildLkUrl(path, null), LkHeaders());
            EnsureAuthorized(result);
            if (String.IsNullOrWhiteSpace(result.Body))
            {
                EnsureSuccess(result);
                return null;
            }

            JsonValue parsed = JsonUtil.ParseOrNull(result.Body);
            if (parsed != null)
                return parsed;

            EnsureSuccess(result);
            throw new InvalidOperationException("сервер вернул не JSON");
        }

        private async Task<JsonValue> LongTaskAsync(string startPath, string forPath, int attempts)
        {
            Dictionary<string, string> startParams = new Dictionary<string, string>();
            startParams["overwriteCache"] = "false";
            HttpResult start = await GetWithSessionAsync(BuildLkUrl(startPath, startParams), LkHeaders());
            EnsureAuthorized(start);

            if (String.IsNullOrWhiteSpace(start.Body))
            {
                EnsureSuccess(start);
                return null;
            }

            JsonValue parsed = JsonUtil.ParseOrNull(start.Body);
            if (parsed != null && (parsed.JsonType == JsonType.Object || parsed.JsonType == JsonType.Array))
            {
                JsonValue data = JsonUtil.Get(parsed, "data");
                return data ?? parsed;
            }

            EnsureSuccess(start);

            string taskId = parsed == null ? start.Body.Trim().Trim('"') : JsonUtil.String(parsed);
            if (String.IsNullOrWhiteSpace(taskId) || taskId.StartsWith("<", StringComparison.Ordinal))
                return null;

            if (attempts < 1)
                attempts = 1;
            if (attempts > 6)
                attempts = 6;
            for (int i = 0; i < attempts; i++)
            {
                await Task.Delay(650);
                Dictionary<string, string> p = new Dictionary<string, string>();
                p["for"] = forPath;
                HttpResult check = await GetWithSessionAsync(BuildLkUrl("/api/longtask/check/" + Uri.EscapeDataString(taskId), p), LkHeaders());
                EnsureAuthorized(check);

                if (check.StatusCode == 204 || String.IsNullOrWhiteSpace(check.Body))
                    continue;

                JsonValue data = JsonUtil.ParseOrNull(check.Body);
                if (data == null)
                    continue;

                JsonValue inner = JsonUtil.Get(data, "data");
                return inner ?? data;
            }

            return null;
        }

        private async Task<JsonValue> GraphQlAsync(string operationName, string query, JsonObject variables)
        {
            JsonObject payload = new JsonObject();
            payload["operationName"] = JsonUtil.Primitive(operationName);
            payload["variables"] = variables ?? new JsonObject();
            payload["query"] = JsonUtil.Primitive(query);

            HttpResult result = await PostJsonWithSessionAsync(GraphQlUrl, payload.ToString(), GraphQlHeaders());
            EnsureAuthorized(result);

            JsonValue root = JsonUtil.ParseOrNull(result.Body);
            JsonValue data = JsonUtil.Get(root, "data");
            if (data != null)
                return data;

            JsonValue errors = JsonUtil.Get(root, "errors");
            if (errors != null)
                throw new InvalidOperationException("GraphQL вернул ошибку: " + errors.ToString());

            EnsureSuccess(result);

            if (root == null)
                throw new InvalidOperationException("сервер вернул не JSON");
            return data;
        }

        private async Task<JsonValue> LoadExpensesDataAsync(int pageIndex)
        {
            JsonValue baseData = await GraphQlAsync("GetExpensesBaseAndCategoryInfoQueryInput", ExpensesBaseQuery, ExpensesVariables(1));
            JsonValue transactionsData = await GraphQlAsync("GetExpensesInfoQueryInput", ExpensesTransactionsQuery, ExpensesVariables(pageIndex < 1 ? 1 : pageIndex));

            JsonObject result = new JsonObject();
            result["base"] = JsonUtil.Get(baseData, "transactionsByFilter");
            result["transactions"] = JsonUtil.Get(transactionsData, "transactionsByFilter");
            return result;
        }

        private async Task<JsonValue> LoadExpensesPageAsync(int pageIndex)
        {
            return await GraphQlAsync("GetExpensesInfoQueryInput", ExpensesTransactionsQuery, ExpensesVariables(pageIndex));
        }

        private static void EnsureAuthorized(HttpResult result)
        {
            if (MtsAuthService.IsSessionRejected(result))
                throw new UnauthorizedAccessException("Сессия МТС истекла. Нужно войти заново.");
        }

        private static void EnsureSuccess(HttpResult result)
        {
            if (!result.IsSuccess)
                throw new InvalidOperationException("HTTP " + result.StatusCode.ToString());
        }

        private static void FillAccount(AccountDashboard d, JsonValue userInfo, JsonValue profile, JsonValue balances, JsonValue tariffInfo, JsonValue tariffSummary)
        {
            JsonValue userProfile = JsonUtil.Get(userInfo, "userProfile");
            string name = FirstNotEmpty(
                JsonUtil.String(JsonUtil.Get(profile, "name")),
                JsonUtil.String(JsonUtil.Get(userProfile, "displayName")),
                JsonUtil.String(JsonUtil.Get(userProfile, "name")));
            string phone = FirstNotEmpty(
                JsonUtil.String(JsonUtil.Get(profile, "phone")),
                JsonUtil.String(JsonUtil.Get(userProfile, "login")),
                JsonUtil.String(JsonUtil.Get(userProfile, "accountNumber")));
            string region = FirstNotEmpty(
                JsonUtil.String(JsonUtil.Get(userProfile, "regionTitle")),
                JsonUtil.String(JsonUtil.Get(profile, "regionTitle")));

            JsonValue tariff = JsonUtil.Get(tariffInfo, "tariffInfo");
            JsonValue currentTariff = JsonUtil.Get(tariffSummary, "current");
            string tariffName = FirstNotEmpty(
                JsonUtil.String(JsonUtil.Get(tariff, "name")),
                JsonUtil.String(JsonUtil.Get(currentTariff, "name")),
                JsonUtil.String(JsonUtil.Get(userProfile, "tariff")));

            JsonValue balanceNode = FindPrimaryBalance(JsonUtil.Array(JsonUtil.Get(balances, "balances", "nodes")));
            double? balance = JsonUtil.Double(JsonUtil.Get(balanceNode, "remainingValue", "amount"));
            string currency = JsonUtil.String(JsonUtil.Get(balanceNode, "remainingValue", "currency"));

            JsonValue mainPrice = JsonUtil.Get(tariff, "tariffPricesInfo", "mainPrice");
            string price = FirstNotEmpty(JsonUtil.String(JsonUtil.Get(mainPrice, "price")), TextUtil.Money(JsonUtil.Double(JsonUtil.Get(mainPrice, "basePrice")), "руб."));

            d.Name = TextUtil.Safe(TextUtil.StripHtml(name));
            d.Phone = TextUtil.Phone(phone);
            d.Region = TextUtil.Safe(region);
            d.TariffName = TextUtil.Safe(TextUtil.StripHtml(tariffName));
            d.TariffPrice = TextUtil.Safe(TextUtil.StripHtml(price));
            d.TariffNextChargeDate = TextUtil.DateRu(JsonUtil.String(JsonUtil.Get(mainPrice, "date")), false);
            d.Balance = TextUtil.Money(balance, currency);
            d.MonthName = TextUtil.CurrentMonthName();
        }

        private static void FillCounters(AccountDashboard d, JsonValue counters, JsonValue tariffSummary)
        {
            JsonValue currentTariff = JsonUtil.Get(tariffSummary, "current");
            JsonValue internetPackage = JsonUtil.Get(currentTariff, "internetPackage");
            string internetText = TextUtil.StripHtml(JsonUtil.String(JsonUtil.Get(internetPackage, "textValue")));
            if (!String.IsNullOrWhiteSpace(internetText) || JsonUtil.Bool(JsonUtil.Get(internetPackage, "isUnlimited")))
            {
                d.Packages.Add(new PackageItem
                {
                    Title = "Интернет по тарифу",
                    Remaining = !String.IsNullOrWhiteSpace(internetText) ? internetText : "безлимит",
                    Total = "—",
                    Deadline = "—",
                    DaysText = "—",
                    Percent = 100
                });
            }

            JsonValue minutesPackage = JsonUtil.Get(currentTariff, "minutesPackage");
            double? minutes = JsonUtil.Double(JsonUtil.Get(minutesPackage, "value"));
            string minutesUnit = TextUtil.StripHtml(JsonUtil.String(JsonUtil.Get(minutesPackage, "unit")));
            if (minutes.HasValue || JsonUtil.Bool(JsonUtil.Get(minutesPackage, "isUnlimited")))
            {
                d.Packages.Add(new PackageItem
                {
                    Title = "Минуты по тарифу",
                    Remaining = JsonUtil.Bool(JsonUtil.Get(minutesPackage, "isUnlimited")) ? "безлимит" : FormatUnit(minutes.HasValue ? minutes.Value * 60.0 : (double?)null, "Second"),
                    Total = String.IsNullOrWhiteSpace(minutesUnit) ? "—" : minutesUnit,
                    Deadline = "—",
                    DaysText = "—",
                    Percent = 100
                });
            }

            JsonArray list = JsonUtil.Array(JsonUtil.Get(counters, "counters"));
            for (int i = 0; i < list.Count; i++)
            {
                JsonValue c = list[i];
                string title = FirstNotEmpty(
                    TextUtil.StripHtml(JsonUtil.String(JsonUtil.Get(c, "name"))),
                    JsonUtil.String(JsonUtil.Get(c, "packageType")),
                    "Пакет");
                bool unlimited = JsonUtil.Bool(JsonUtil.Get(c, "isUnlimited"));
                double? total = JsonUtil.Double(JsonUtil.Get(c, "totalAmount"));
                double? current = JsonUtil.Double(JsonUtil.Get(c, "currentAmount"));
                double? used = JsonUtil.Double(JsonUtil.Get(c, "usedAmount"));
                if (!current.HasValue && total.HasValue && used.HasValue)
                    current = total.Value - used.Value;

                string unit = JsonUtil.String(JsonUtil.Get(c, "unitType"));
                double? remainingDays = JsonUtil.Double(JsonUtil.Get(c, "remainingDays"));
                string daysText = remainingDays.HasValue ? remainingDays.Value.ToString("0", CultureInfo.CurrentCulture) : "—";
                string deadline = TextUtil.DateRu(JsonUtil.String(JsonUtil.Get(c, "deadlineDate")), false);

                double percent = 0;
                double? actualPercent = JsonUtil.Double(JsonUtil.Get(c, "actualRemainsPercents"));
                if (actualPercent.HasValue)
                    percent = actualPercent.Value;
                else if (unlimited)
                    percent = 100;
                else if (total.HasValue && total.Value > 0 && current.HasValue)
                    percent = current.Value / total.Value * 100.0;

                d.Packages.Add(new PackageItem
                {
                    Title = title,
                    Remaining = unlimited ? "безлимит" : FormatUnit(current, unit),
                    Total = unlimited ? "безлимит" : FormatUnit(total, unit),
                    Deadline = deadline,
                    DaysText = daysText,
                    Percent = TextUtil.ClampPercent(percent)
                });
            }
        }

        private static void FillServicesAndCharges(AccountDashboard d, JsonValue services, JsonValue tariffInfo)
        {
            JsonArray list = JsonUtil.Array(JsonUtil.Get(services, "services"));
            ServiceItem bestCharge = null;
            double bestAmount = 0;

            for (int i = 0; i < list.Count; i++)
            {
                JsonValue s = list[i];
                JsonValue fee = JsonUtil.Get(s, "subscriptionFee") ?? JsonUtil.Get(s, "primarySubscriptionFee");
                double? value = JsonUtil.Double(JsonUtil.Get(fee, "value"));
                string unit = FirstNotEmpty(JsonUtil.String(JsonUtil.Get(fee, "unit")), JsonUtil.String(JsonUtil.Get(fee, "quotaPeriodicity")));
                string feeText = value.HasValue ? TextUtil.Money(value, "руб.") : "—";
                if (!String.IsNullOrWhiteSpace(unit) && feeText != "—")
                    feeText = feeText + " / " + unit;

                ServiceItem item = new ServiceItem
                {
                    Name = TextUtil.Safe(TextUtil.StripHtml(JsonUtil.String(JsonUtil.Get(s, "name")))),
                    Group = TextUtil.Safe(TextUtil.StripHtml(JsonUtil.String(JsonUtil.Get(s, "group", "title")))),
                    Fee = feeText,
                    NextDate = TextUtil.DateRu(JsonUtil.String(JsonUtil.Get(fee, "recurringChargePeriod", "startDateTime")), false)
                };
                d.Services.Add(item);

                if (value.HasValue && value.Value > bestAmount && item.NextDate != "—")
                {
                    bestAmount = value.Value;
                    bestCharge = item;
                }
            }

            if (bestCharge != null)
            {
                d.NextChargeTitle = bestCharge.Name;
                d.NextChargeAmount = bestCharge.Fee;
                d.NextChargeDate = bestCharge.NextDate;
            }
            else
            {
                JsonValue mainPrice = JsonUtil.Get(tariffInfo, "tariffInfo", "tariffPricesInfo", "mainPrice");
                d.NextChargeTitle = "Тариф";
                d.NextChargeAmount = FirstNotEmpty(JsonUtil.String(JsonUtil.Get(mainPrice, "price")), TextUtil.Money(JsonUtil.Double(JsonUtil.Get(mainPrice, "basePrice")), "руб."));
                d.NextChargeDate = TextUtil.DateRu(JsonUtil.String(JsonUtil.Get(mainPrice, "date")), false);
            }
        }

        private static void FillExpensesFromParts(AccountDashboard d, JsonValue data)
        {
            JsonValue basePart = JsonUtil.Get(data, "base");
            JsonValue txPart = JsonUtil.Get(data, "transactions");
            JsonValue total = JsonUtil.Get(basePart, "totalInfo");
            string currency = FirstNotEmpty(JsonUtil.String(JsonUtil.Get(total, "currency")), "руб.");
            d.ExpensesTotal = TextUtil.Money(JsonUtil.Double(JsonUtil.Get(total, "outcomeAmount")), currency);
            d.IncomeTotal = TextUtil.Money(JsonUtil.Double(JsonUtil.Get(total, "incomeAmount")), currency);

            JsonArray categories = JsonUtil.Array(JsonUtil.Get(basePart, "categories"));
            for (int i = 0; i < categories.Count; i++)
            {
                JsonValue c = categories[i];
                double? amount = JsonUtil.Double(JsonUtil.Get(c, "amount"));
                double? percent = JsonUtil.Double(JsonUtil.Get(c, "percentage"));
                if ((!amount.HasValue || Math.Abs(amount.Value) < 0.001) && (!percent.HasValue || Math.Abs(percent.Value) < 0.001))
                    continue;

                d.ExpenseCategories.Add(new ExpenseCategoryItem
                {
                    Name = TextUtil.Safe(TextUtil.StripHtml(JsonUtil.String(JsonUtil.Get(c, "name")))),
                    Amount = TextUtil.Money(amount, currency),
                    Percent = percent.HasValue ? percent.Value.ToString("0.#", CultureInfo.CurrentCulture) + "%" : "—"
                });
            }

            FillTransactions(d.Transactions, JsonUtil.Array(JsonUtil.Get(txPart, "transactions")), currency);
        }

        private static void FillTransactions(List<TransactionItem> target, JsonArray transactions, string currency)
        {
            for (int i = 0; i < transactions.Count; i++)
            {
                JsonValue tx = transactions[i];
                double? amount = JsonUtil.Double(JsonUtil.Get(tx, "amount"));
                string type = JsonUtil.String(JsonUtil.Get(tx, "type"));
                string sign = String.Equals(type, "income", StringComparison.OrdinalIgnoreCase) ? "+" : (amount.HasValue && Math.Abs(amount.Value) > 0.001 ? "-" : String.Empty);
                target.Add(new TransactionItem
                {
                    Date = TextUtil.DateRu(JsonUtil.String(JsonUtil.Get(tx, "dateTime")), true),
                    Name = TextUtil.Safe(TextUtil.StripHtml(FirstNotEmpty(JsonUtil.String(JsonUtil.Get(tx, "name")), JsonUtil.String(JsonUtil.Get(tx, "subtitle")), "Операция"))),
                    Amount = sign + TextUtil.Money(amount, currency),
                    Description = TextUtil.Safe(TextUtil.StripHtml(JsonUtil.String(JsonUtil.Get(tx, "description"))))
                });
            }
        }

        private static JsonValue FindPrimaryBalance(JsonArray nodes)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                string code = JsonUtil.String(JsonUtil.Get(nodes[i], "code"));
                if (code == "MMA" || code == "PA-OF")
                    return nodes[i];
            }
            for (int i = 0; i < nodes.Count; i++)
            {
                if (JsonUtil.Get(nodes[i], "remainingValue", "amount") != null)
                    return nodes[i];
            }
            return nodes.Count > 0 ? nodes[0] : null;
        }

        private static string FormatUnit(double? value, string unit)
        {
            if (!value.HasValue)
                return "—";
            double v = value.Value;
            string u = (unit ?? String.Empty).ToLowerInvariant();
            if (u == "second")
                return (v / 60.0).ToString("0", CultureInfo.CurrentCulture) + " мин";
            if (u == "kbyte")
            {
                double gb = v / 1024.0 / 1024.0;
                if (gb >= 1)
                    return gb.ToString("0.#", CultureInfo.CurrentCulture) + " ГБ";
                return (v / 1024.0).ToString("0", CultureInfo.CurrentCulture) + " МБ";
            }
            if (u == "item")
                return v.ToString("0", CultureInfo.CurrentCulture) + " шт";
            return v.ToString("0.#", CultureInfo.CurrentCulture);
        }

        private static string FirstNotEmpty(params string[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (!String.IsNullOrWhiteSpace(values[i]) && values[i] != "—")
                    return values[i];
            }
            return "—";
        }

        private static JsonObject TariffVariables()
        {
            JsonObject variables = new JsonObject();
            variables["screenName"] = JsonUtil.Primitive("default");
            return variables;
        }

        private static JsonObject ExpensesVariables(int pageIndex)
        {
            JsonObject variables = new JsonObject();
            variables["pageIndex"] = JsonUtil.Primitive(pageIndex < 1 ? 1 : pageIndex);
            variables["filter"] = BuildExpensesFilter();
            return variables;
        }

        private static JsonObject BuildExpensesFilter()
        {
            DateTime now = DateTime.Now;
            DateTime start = new DateTime(now.Year, now.Month, 1, 0, 0, 0);
            DateTime end = new DateTime(now.Year, now.Month, now.Day, 23, 59, 59);

            JsonObject filter = new JsonObject();
            filter["dateFrom"] = JsonUtil.Primitive(start.ToString("yyyy-MM-ddTHH:mm:ss+03:00", CultureInfo.InvariantCulture));
            filter["dateTo"] = JsonUtil.Primitive(end.ToString("yyyy-MM-ddTHH:mm:ss+03:00", CultureInfo.InvariantCulture));
            filter["showFree"] = JsonUtil.Primitive(false);
            filter["tabType"] = null;
            filter["categoryId"] = null;
            filter["textSearch"] = JsonUtil.Primitive(String.Empty);
            filter["transactionsDirection"] = null;
            filter["cashbackType"] = JsonUtil.Primitive("ALL");
            return filter;
        }

        private static string BuildLkUrl(string path, Dictionary<string, string> query)
        {
            string url = path.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? path : LkBase + "/" + path.TrimStart('/');
            if (query == null || query.Count == 0)
                return url;
            List<string> parts = new List<string>();
            foreach (KeyValuePair<string, string> item in query)
                parts.Add(Uri.EscapeDataString(item.Key) + "=" + Uri.EscapeDataString(item.Value));
            return url + "?" + String.Join("&", parts.ToArray());
        }

        private static Dictionary<string, string> LkHeaders()
        {
            Dictionary<string, string> h = new Dictionary<string, string>();
            h["Accept"] = "application/json, text/plain, */*";
            h["Accept-Language"] = "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7";
            h["Referer"] = "https://lk.mts.ru/";
            h["X-Requested-With"] = "XMLHttpRequest";
            h["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/147.0.0.0 Safari/537.36";
            return h;
        }

        private static Dictionary<string, string> GraphQlHeaders()
        {
            Dictionary<string, string> h = LkHeaders();
            h["Content-Type"] = "application/json";
            h["Origin"] = "https://lk.mts.ru";
            h["X-Client-Id"] = "LK";
            return h;
        }
    }
}
