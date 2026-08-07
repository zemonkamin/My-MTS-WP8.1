using System;
using System.Collections.Generic;
using System.Json;
using System.Threading.Tasks;
using Мой_МТС.Utilities;

namespace Мой_МТС.Services
{
    public sealed class MtsAuthService
    {
        private const string LoginStartUrl = "https://united-auth.ssl.mts.ru/account/login";
        private const string WssoAuthUrl = "https://login.mts.ru/amserver/wsso/authenticate";
        private const string DefaultGoto = "https://lk.mts.ru/";
        private const string DefaultScope = "profile account phone slaves:all slaves:profile sub email user_address identity_doc personal_data openid offline offline_access";
        private const string Cid = "mts-w-payment";
        private static readonly TimeSpan SessionRefreshInterval = TimeSpan.FromMinutes(25);

        private readonly MtsHttpClient _http;
        private string _authUrl;
        private string _refererUrl;
        private JsonValue _state;
        private string _phone;
        private DateTime? _lastSessionRefreshUtc;

        public MtsAuthService(MtsHttpClient http)
        {
            _http = http;
        }

        public bool HasSavedSession
        {
            get { return _http.Cookies.HasCookies; }
        }

        public void Logout()
        {
            _http.Cookies.Clear();
            _state = null;
            _authUrl = null;
            _refererUrl = null;
            _phone = null;
            _lastSessionRefreshUtc = null;
        }

        public async Task BeginLoginAsync(string phone)
        {
            _phone = NormalizePhone(phone);

            string startUrl = LoginStartUrl + "?xClientId=LK&goto=" + UrlEncode(DefaultGoto);
            HttpResult start = await _http.GetAsync(startUrl, DefaultHeaders());
            if (!start.IsSuccess)
                throw new InvalidOperationException("МТС ID вернул HTTP " + start.StatusCode.ToString());

            _refererUrl = start.ResponseUri.ToString();
            Dictionary<string, string> nui = ParseQuery(start.ResponseUri);
            string gotoValue = Get(nui, "goto", String.Empty);
            string redirectUri = Get(nui, "redirect_uri", "https://united-auth.ssl.mts.ru/account/callback/login");
            string statetrace = Get(nui, "statetrace", Get(nui, "state", String.Empty));

            Dictionary<string, string> authParams = new Dictionary<string, string>();
            authParams["client_id"] = Get(nui, "client_id", "mts.ru");
            authParams["scope"] = Get(nui, "scope", DefaultScope);
            authParams["redirect_uri"] = redirectUri;
            authParams["authIndexType"] = "service";
            authParams["authIndexValue"] = "login-spa";
            authParams["goto"] = gotoValue;
            authParams["response_type"] = Get(nui, "response_type", "code");
            authParams["statetrace"] = statetrace;

            _authUrl = WssoAuthUrl + "?" + BuildQuery(authParams);
            _state = await AuthPostAsync(null);
            SubmitDeviceValues(_state);
            _state = await AuthPostAsync(_state);
            SubmitPhoneValues(_state);
            _state = await AuthPostAsync(_state);
        }

        public async Task CompleteOtpAsync(string otp)
        {
            if (_state == null)
                throw new InvalidOperationException("Сначала запросите SMS-код.");

            SubmitOtpValues(_state, otp);
            _state = await AuthPostAsync(_state);
            _state = await FinishAsync(_state);

            string successUrl = JsonUtil.String(JsonUtil.Get(_state, "successUrl"));
            if (String.IsNullOrEmpty(successUrl))
                throw new InvalidOperationException("МТС ID не вернул successUrl. Возможно, изменилась цепочка входа.");

            HttpResult finish = await _http.GetAsync(successUrl, DefaultHeaders());
            if (!finish.IsSuccess)
                throw new InvalidOperationException("Финальный переход вернул HTTP " + finish.StatusCode.ToString());

            await WarmUpAsync();
        }

        public async Task WarmUpAsync()
        {
            bool restored = await RefreshSessionCoreAsync();
            if (!restored)
                throw new UnauthorizedAccessException("Не удалось закрепить сессию МТС после входа.");
        }

        public async Task<bool> RestoreSavedSessionAsync()
        {
            if (!HasSavedSession)
                return false;

            return await RefreshSessionCoreAsync();
        }

        public async Task TryRefreshSessionIfNeededAsync()
        {
            if (!HasSavedSession)
                return;

            if (_lastSessionRefreshUtc.HasValue &&
                DateTime.UtcNow - _lastSessionRefreshUtc.Value < SessionRefreshInterval)
                return;

            try
            {
                await RefreshSessionCoreAsync();
            }
            catch
            {
                // Не блокируем обычный запрос из-за временной ошибки keep-alive.
            }
        }

        public async Task<bool> TryRefreshSessionAsync()
        {
            if (!HasSavedSession)
                return false;

            try
            {
                return await RefreshSessionCoreAsync();
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> RefreshSessionCoreAsync()
        {
            HttpResult home = await _http.GetAsync("https://lk.mts.ru/", LkHeaders());
            if (IsSessionRejected(home))
                return false;
            if (!home.IsSuccess)
                throw new InvalidOperationException("LK МТС вернул HTTP " + home.StatusCode.ToString());

            HttpResult refresh = await _http.GetAsync("https://lk.mts.ru/api/login/refreshCsrfToken", LkHeaders());
            if (IsSessionRejected(refresh))
                return false;
            if (!refresh.IsSuccess)
                throw new InvalidOperationException("Обновление сессии МТС вернуло HTTP " + refresh.StatusCode.ToString());

            HttpResult verify = await _http.GetAsync("https://lk.mts.ru/api/login/user-info", LkHeaders());
            if (IsSessionRejected(verify))
                return false;
            if (!verify.IsSuccess)
                throw new InvalidOperationException("Проверка сессии МТС вернула HTTP " + verify.StatusCode.ToString());

            if (String.IsNullOrWhiteSpace(verify.Body) || JsonUtil.ParseOrNull(verify.Body) == null)
                return false;

            _lastSessionRefreshUtc = DateTime.UtcNow;
            return true;
        }

        internal static bool IsSessionRejected(HttpResult result)
        {
            if (result == null)
                return true;
            if (result.StatusCode == 401 || result.StatusCode == 403)
                return true;

            Uri uri = result.ResponseUri;
            if (uri == null)
                return false;

            string host = uri.Host == null ? String.Empty : uri.Host.ToLowerInvariant();
            return host == "login.mts.ru" ||
                   host == "united-auth.ssl.mts.ru" ||
                   host.EndsWith(".login.mts.ru", StringComparison.Ordinal);
        }

        private async Task<JsonValue> FinishAsync(JsonValue data)
        {
            for (int i = 0; i < 4; i++)
            {
                if (JsonUtil.Get(data, "successUrl") != null || JsonUtil.Get(data, "tokenId") != null)
                    return data;

                JsonArray callbacks = JsonUtil.Array(JsonUtil.Get(data, "callbacks"));
                if (callbacks.Count == 0)
                    return data;

                string header = JsonUtil.String(JsonUtil.Get(data, "header"));
                bool passkeyStage = !String.IsNullOrEmpty(header) && header.ToLowerInvariant().IndexOf("passkey") >= 0;
                bool onlyIgnoreOk = true;
                bool sawConfirmation = false;

                for (int c = 0; c < callbacks.Count; c++)
                {
                    JsonObject callback = callbacks[c] as JsonObject;
                    if (callback == null)
                        continue;

                    if (GetCallbackType(callback) == "ConfirmationCallback")
                    {
                        sawConfirmation = true;
                        List<string> options = GetOptions(callback);
                        if (options.Count != 2 || options[0] != "IGNORE" || options[1] != "OK")
                            onlyIgnoreOk = false;
                    }

                    if (HasPrompt(callback, "passkey") || HasPrompt(callback, "sessionkey"))
                        passkeyStage = true;
                }

                bool preferIgnore = passkeyStage || (sawConfirmation && onlyIgnoreOk);
                for (int c = 0; c < callbacks.Count; c++)
                {
                    JsonObject callback = callbacks[c] as JsonObject;
                    if (callback == null)
                        continue;

                    string type = GetCallbackType(callback);
                    if (type == "TextInputCallback" && passkeyStage)
                        SetInput(callback, String.Empty, null);
                    else if (type == "ConfirmationCallback")
                        SetInput(callback, OptionIndex(callback, preferIgnore ? "IGNORE" : "OK"), null);
                    else if (type == "MetadataCallback")
                        SetInput(callback, Cid, "cid");
                    else if (type == "LocaleCallback")
                        SetInput(callback, "ru", "Language");
                }

                data = await AuthPostAsync(data);
            }
            return data;
        }

        private async Task<JsonValue> AuthPostAsync(JsonValue payload)
        {
            Dictionary<string, string> headers = DefaultHeaders();
            headers["Accept-API-Version"] = "resource=4.0, protocol=1.0";
            headers["Content-Type"] = "application/json;charset=UTF-8";
            headers["Origin"] = "https://login.mts.ru";
            if (!String.IsNullOrEmpty(_refererUrl))
                headers["Referer"] = _refererUrl;

            string body = payload == null ? null : payload.ToString();
            HttpResult result = await _http.SendAsync("POST", _authUrl, body, headers, "application/json;charset=UTF-8");
            if (!result.IsSuccess)
                throw new InvalidOperationException("MTS ID API вернул HTTP " + result.StatusCode.ToString());

            return JsonValue.Parse(result.Body);
        }

        private void SubmitDeviceValues(JsonValue data)
        {
            string devicePrint = "{\"screen\":{\"screenWidth\":480,\"screenHeight\":800,\"screenColourDepth\":32}," +
                                 "\"userAgent\":\"Windows Phone 8.1\",\"platform\":\"Win32\",\"language\":\"ru\"," +
                                 "\"timezone\":{\"timezone\":-180},\"plugins\":{\"installedPlugins\":\"\"}," +
                                 "\"fonts\":{\"installedFonts\":\"Segoe UI;Arial;Tahoma;\"},\"appName\":\"Netscape\"," +
                                 "\"appCodeName\":\"Mozilla\",\"product\":\"Gecko\",\"productSub\":\"20030107\",\"vendor\":\"Microsoft\"}";

            JsonArray callbacks = JsonUtil.Array(JsonUtil.Get(data, "callbacks"));
            for (int i = 0; i < callbacks.Count; i++)
            {
                JsonObject cb = callbacks[i] as JsonObject;
                if (cb == null)
                    continue;

                string type = GetCallbackType(cb);
                string firstName = FirstInputName(cb);
                if (type == "HiddenValueCallback" && firstName == "IDToken1")
                    SetInput(cb, devicePrint, null);
                else if (type == "TextInputCallback")
                {
                    if (firstName == "IDToken2") SetInput(cb, String.Empty, null);
                    else if (firstName == "IDToken3") SetInput(cb, "true", null);
                    else if (firstName == "IDToken4") SetInput(cb, "windows", null);
                    else if (firstName == "IDToken5") SetInput(cb, "Windows Phone", null);
                    else if (firstName == "IDToken7") SetInput(cb, Guid.NewGuid().ToString(), null);
                    else if (firstName == "IDToken8") SetInput(cb, String.Empty, null);
                    else if (firstName == "IDToken9") SetInput(cb, "Windows Phone", null);
                    else if (firstName == "IDToken10") SetInput(cb, "false", null);
                }
                else if (type == "ReferrerCallback")
                    SetInput(cb, DefaultGoto, "referrer");
                else if (type == "ConfirmationCallback")
                    SetInput(cb, OptionIndex(cb, "OK"), null);
                else if (type == "MetadataCallback")
                    SetInput(cb, Cid, "cid");
                else if (type == "LocaleCallback")
                    SetInput(cb, "ru", "Language");
            }
        }

        private void SubmitPhoneValues(JsonValue data)
        {
            JsonArray callbacks = JsonUtil.Array(JsonUtil.Get(data, "callbacks"));
            for (int i = 0; i < callbacks.Count; i++)
            {
                JsonObject cb = callbacks[i] as JsonObject;
                if (cb == null)
                    continue;

                string type = GetCallbackType(cb);
                if (type == "NameCallback")
                    SetInput(cb, _phone, null);
                else if (type == "ConfirmationCallback")
                    SetInput(cb, OptionIndex(cb, "OK"), null);
                else if (type == "MetadataCallback")
                    SetInput(cb, Cid, "cid");
                else if (type == "LocaleCallback")
                    SetInput(cb, "ru", "Language");
            }
        }

        private void SubmitOtpValues(JsonValue data, string otp)
        {
            string digits = OnlyDigits(otp);
            if (digits.Length < 4)
                throw new InvalidOperationException("SMS-код слишком короткий.");

            JsonArray callbacks = JsonUtil.Array(JsonUtil.Get(data, "callbacks"));
            for (int i = 0; i < callbacks.Count; i++)
            {
                JsonObject cb = callbacks[i] as JsonObject;
                if (cb == null)
                    continue;

                string type = GetCallbackType(cb);
                string firstName = FirstInputName(cb);
                if (type == "PasswordCallback")
                    SetInput(cb, digits, null);
                else if (type == "TextInputCallback" && firstName == "IDToken4")
                    SetInput(cb, "true", null);
                else if (type == "ConfirmationCallback")
                    SetInput(cb, OptionIndex(cb, "OK"), null);
                else if (type == "MetadataCallback")
                    SetInput(cb, Cid, "cid");
                else if (type == "LocaleCallback")
                    SetInput(cb, "ru", "Language");
            }
        }

        private static string GetCallbackType(JsonObject callback)
        {
            if (callback == null || !callback.ContainsKey("type"))
                return String.Empty;
            return JsonUtil.String(callback["type"]);
        }

        private static string FirstInputName(JsonObject callback)
        {
            JsonArray input = JsonUtil.Array(JsonUtil.Get(callback, "input"));
            if (input.Count == 0)
                return String.Empty;
            return JsonUtil.String(JsonUtil.Get(input[0], "name"));
        }

        private static void SetInput(JsonObject callback, object value, string name)
        {
            JsonArray input = JsonUtil.Array(JsonUtil.Get(callback, "input"));
            for (int i = 0; i < input.Count; i++)
            {
                JsonObject item = input[i] as JsonObject;
                if (item == null)
                    continue;
                if (name == null || JsonUtil.String(JsonUtil.Get(item, "name")) == name)
                {
                    if (value is int)
                        item["value"] = JsonUtil.Primitive((int)value);
                    else if (value is bool)
                        item["value"] = JsonUtil.Primitive((bool)value);
                    else
                        item["value"] = JsonUtil.Primitive(value == null ? String.Empty : value.ToString());
                    return;
                }
            }
        }

        private static int OptionIndex(JsonObject callback, string option)
        {
            List<string> options = GetOptions(callback);
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i] == option)
                    return i;
            }
            return option == "OK" ? 1 : 0;
        }

        private static List<string> GetOptions(JsonObject callback)
        {
            List<string> result = new List<string>();
            JsonArray output = JsonUtil.Array(JsonUtil.Get(callback, "output"));
            for (int i = 0; i < output.Count; i++)
            {
                JsonObject item = output[i] as JsonObject;
                if (item == null)
                    continue;
                if (JsonUtil.String(JsonUtil.Get(item, "name")) == "options")
                {
                    JsonArray arr = JsonUtil.Array(JsonUtil.Get(item, "value"));
                    for (int j = 0; j < arr.Count; j++)
                        result.Add(JsonUtil.String(arr[j]));
                }
            }
            return result;
        }

        private static bool HasPrompt(JsonObject callback, string text)
        {
            JsonArray output = JsonUtil.Array(JsonUtil.Get(callback, "output"));
            for (int i = 0; i < output.Count; i++)
            {
                JsonObject item = output[i] as JsonObject;
                if (item == null)
                    continue;
                if (JsonUtil.String(JsonUtil.Get(item, "name")) == "prompt")
                {
                    string value = JsonUtil.String(JsonUtil.Get(item, "value"));
                    if (!String.IsNullOrEmpty(value) && value.ToLowerInvariant().IndexOf(text) >= 0)
                        return true;
                }
            }
            return false;
        }

        private static string NormalizePhone(string phone)
        {
            string digits = OnlyDigits(phone);

            if (digits.Length == 10)
                digits = "7" + digits;
            else if (digits.Length == 11 && digits.StartsWith("8", StringComparison.Ordinal))
                digits = "7" + digits.Substring(1);

            if (digits.Length != 11 || !digits.StartsWith("7", StringComparison.Ordinal))
                throw new InvalidOperationException("Введите 10 цифр номера после +7.");
            return digits;
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

        private static Dictionary<string, string> DefaultHeaders()
        {
            Dictionary<string, string> h = new Dictionary<string, string>();
            h["Accept"] = "application/json, text/plain, */*";
            h["Accept-Language"] = "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7";
            h["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/147.0.0.0 Safari/537.36";
            return h;
        }

        private static Dictionary<string, string> LkHeaders()
        {
            Dictionary<string, string> h = DefaultHeaders();
            h["Referer"] = "https://lk.mts.ru/";
            h["X-Requested-With"] = "XMLHttpRequest";
            return h;
        }

        private static Dictionary<string, string> ParseQuery(Uri uri)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            string query = uri.Query;
            if (String.IsNullOrEmpty(query))
                return result;
            if (query.StartsWith("?", StringComparison.Ordinal))
                query = query.Substring(1);
            string[] parts = query.Split('&');
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                int eq = part.IndexOf('=');
                string key = eq >= 0 ? part.Substring(0, eq) : part;
                string value = eq >= 0 ? part.Substring(eq + 1) : String.Empty;
                key = Uri.UnescapeDataString(key.Replace("+", " "));
                value = Uri.UnescapeDataString(value.Replace("+", " "));
                result[key] = value;
            }
            return result;
        }

        private static string Get(Dictionary<string, string> data, string key, string defaultValue)
        {
            string value;
            if (data.TryGetValue(key, out value))
                return value;
            return defaultValue;
        }

        private static string BuildQuery(Dictionary<string, string> values)
        {
            List<string> parts = new List<string>();
            foreach (KeyValuePair<string, string> item in values)
                parts.Add(UrlEncode(item.Key) + "=" + UrlEncode(item.Value));
            return String.Join("&", parts.ToArray());
        }

        private static string UrlEncode(string value)
        {
            return Uri.EscapeDataString(value ?? String.Empty).Replace("%20", "+");
        }
    }
}
