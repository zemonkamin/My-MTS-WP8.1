using System;
using System.Collections.Generic;
using System.IO.IsolatedStorage;
using System.Net;

namespace Мой_МТС.Services
{
    public sealed class CookieStore
    {
        private const string SettingsKey = "MtsCookieJarV2";
        private readonly List<StoredCookie> _cookies = new List<StoredCookie>();

        public CookieStore()
        {
            Load();
        }

        public bool HasCookies
        {
            get { return _cookies.Count > 0; }
        }

        public void Clear()
        {
            _cookies.Clear();
            IsolatedStorageSettings.ApplicationSettings.Remove(SettingsKey);
            IsolatedStorageSettings.ApplicationSettings.Remove("MtsCookieJarV1");
            SafeSaveSettings();
        }

        public CookieContainer CreateContainer(Uri uri)
        {
            CookieContainer container = new CookieContainer();
            DateTime now = DateTime.UtcNow;
            List<Uri> hosts = KnownCookieUris(uri);

            for (int i = 0; i < _cookies.Count; i++)
            {
                StoredCookie stored = _cookies[i];
                if (stored.ExpiresUtc.HasValue && stored.ExpiresUtc.Value <= now)
                    continue;
                if (!IsValidCookieName(stored.Name))
                    continue;

                // HttpWebRequest сохраняет один CookieContainer на всю цепочку redirect.
                // Поэтому заранее кладём в него куки не только стартового lk.mts.ru,
                // но и login/united-auth/federation, иначе silent SSO после перезапуска
                // теряет сохранённую auth-куку при переходе на другой MTS-хост.
                for (int h = 0; h < hosts.Count; h++)
                {
                    if (Matches(hosts[h].Host, stored.Domain))
                        TryAddToContainer(container, hosts[h], stored);
                }
            }

            return container;
        }

        public void Capture(Uri responseUri, WebHeaderCollection headers, CookieCollection responseCookies)
        {
            if (responseUri == null)
                return;

            bool changed = false;

            if (responseCookies != null)
            {
                foreach (Cookie sourceCookie in responseCookies)
                {
                    StoredCookie cookie = FromCookie(responseUri, sourceCookie);
                    if (cookie != null)
                        changed |= AddOrReplace(cookie);
                }
            }

            if (headers != null)
            {
                string raw = headers["Set-Cookie"];
                if (!String.IsNullOrEmpty(raw))
                {
                    foreach (string cookieText in SplitSetCookie(raw))
                    {
                        StoredCookie cookie = Parse(responseUri, cookieText);
                        if (cookie != null)
                            changed |= AddOrReplace(cookie);
                    }
                }
            }

            if (changed)
                Save();
        }

        public void CaptureFromContainer(CookieContainer container, Uri responseUri)
        {
            if (container == null)
                return;

            bool changed = false;
            List<Uri> hosts = KnownCookieUris(responseUri);
            for (int h = 0; h < hosts.Count; h++)
            {
                CookieCollection collection = null;
                try
                {
                    collection = container.GetCookies(hosts[h]);
                }
                catch
                {
                    collection = null;
                }

                if (collection == null)
                    continue;

                foreach (Cookie sourceCookie in collection)
                {
                    StoredCookie cookie = FromCookie(hosts[h], sourceCookie);
                    if (cookie != null)
                        changed |= AddOrReplace(cookie);
                }
            }

            if (changed)
                Save();
        }

        private static void TryAddToContainer(CookieContainer container, Uri uri, StoredCookie stored)
        {
            if (stored == null || !IsTrustedMtsDomain(stored.Domain) || !IsValidCookieName(stored.Name) || !IsSafeCookieValue(stored.Value))
                return;

            try
            {
                Cookie cookie = new Cookie(stored.Name, stored.Value ?? String.Empty, SafePath(stored.Path));
                container.Add(uri, cookie);
            }
            catch
            {
            }
        }

        private static StoredCookie FromCookie(Uri fallbackUri, Cookie source)
        {
            if (source == null || !IsValidCookieName(source.Name))
                return null;

            StoredCookie cookie = new StoredCookie();
            cookie.Name = source.Name;
            cookie.Value = source.Value ?? String.Empty;
            cookie.Domain = NormalizeDomain(String.IsNullOrEmpty(source.Domain) ? fallbackUri.Host : source.Domain);
            cookie.Path = SafePath(source.Path);

            try
            {
                if (source.Expires != DateTime.MinValue && source.Expires != DateTime.MaxValue)
                    cookie.ExpiresUtc = source.Expires.ToUniversalTime();
            }
            catch
            {
            }

            return cookie;
        }

        private static bool Matches(string host, string domain)
        {
            if (String.IsNullOrEmpty(host) || String.IsNullOrEmpty(domain))
                return false;
            string h = host.ToLowerInvariant();
            string d = NormalizeDomain(domain).ToLowerInvariant();
            return h == d || h.EndsWith("." + d, StringComparison.Ordinal);
        }

        private bool AddOrReplace(StoredCookie cookie)
        {
            if (cookie == null || !IsValidCookieName(cookie.Name) || String.IsNullOrEmpty(cookie.Domain))
                return false;
            if (!IsTrustedMtsDomain(cookie.Domain) || !IsSafeCookieValue(cookie.Value))
                return false;

            for (int i = _cookies.Count - 1; i >= 0; i--)
            {
                StoredCookie current = _cookies[i];
                if (String.Equals(current.Name, cookie.Name, StringComparison.OrdinalIgnoreCase) &&
                    String.Equals(NormalizeDomain(current.Domain), NormalizeDomain(cookie.Domain), StringComparison.OrdinalIgnoreCase) &&
                    String.Equals(SafePath(current.Path), SafePath(cookie.Path), StringComparison.Ordinal))
                {
                    _cookies.RemoveAt(i);
                }
            }

            if (cookie.ExpiresUtc.HasValue && cookie.ExpiresUtc.Value <= DateTime.UtcNow)
                return true;

            _cookies.Add(cookie);
            return true;
        }

        private static StoredCookie Parse(Uri responseUri, string text)
        {
            if (String.IsNullOrWhiteSpace(text))
                return null;

            string[] parts = text.Split(';');
            if (parts.Length == 0)
                return null;

            int eq = parts[0].IndexOf('=');
            if (eq <= 0)
                return null;

            string name = parts[0].Substring(0, eq).Trim();
            if (!IsValidCookieName(name))
                return null;

            StoredCookie cookie = new StoredCookie();
            cookie.Name = name;
            cookie.Value = parts[0].Substring(eq + 1).Trim();
            cookie.Domain = responseUri.Host;
            cookie.Path = "/";

            for (int i = 1; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                int attrEq = part.IndexOf('=');
                string attrName = attrEq > 0 ? part.Substring(0, attrEq).Trim().ToLowerInvariant() : part.ToLowerInvariant();
                string value = attrEq > 0 ? part.Substring(attrEq + 1).Trim() : String.Empty;

                if (attrName == "domain" && !String.IsNullOrWhiteSpace(value))
                    cookie.Domain = NormalizeDomain(value);
                else if (attrName == "path" && !String.IsNullOrWhiteSpace(value))
                    cookie.Path = SafePath(value);
                else if (attrName == "max-age")
                {
                    int seconds;
                    if (Int32.TryParse(value, out seconds))
                        cookie.ExpiresUtc = DateTime.UtcNow.AddSeconds(seconds);
                }
                else if (attrName == "expires")
                {
                    DateTime expires;
                    if (DateTime.TryParse(value, out expires))
                        cookie.ExpiresUtc = expires.ToUniversalTime();
                }
            }

            return cookie;
        }

        private static IEnumerable<string> SplitSetCookie(string header)
        {
            if (String.IsNullOrEmpty(header))
                yield break;

            int start = 0;
            bool inExpires = false;
            for (int i = 0; i < header.Length; i++)
            {
                if (i + 8 <= header.Length && String.Compare(header, i, "expires=", 0, 8, StringComparison.OrdinalIgnoreCase) == 0)
                    inExpires = true;

                if (inExpires && header[i] == ';')
                    inExpires = false;

                if (header[i] != ',' || inExpires)
                    continue;

                int j = i + 1;
                while (j < header.Length && header[j] == ' ')
                    j++;

                int k = j;
                bool looksLikeCookie = false;
                while (k < header.Length && header[k] != ';' && header[k] != ',')
                {
                    if (header[k] == '=')
                    {
                        looksLikeCookie = true;
                        break;
                    }
                    k++;
                }

                if (looksLikeCookie)
                {
                    yield return header.Substring(start, i - start).Trim();
                    start = j;
                }
            }

            if (start < header.Length)
                yield return header.Substring(start).Trim();
        }

        private static List<Uri> KnownCookieUris(Uri responseUri)
        {
            List<Uri> result = new List<Uri>();
            AddUri(result, responseUri);
            AddUri(result, "https://lk.mts.ru/");
            AddUri(result, "https://mts.ru/");
            AddUri(result, "https://moskva.mts.ru/");
            AddUri(result, "https://login.mts.ru/");
            AddUri(result, "https://united-auth.ssl.mts.ru/");
            AddUri(result, "https://federation.mts.ru/");
            return result;
        }

        private static void AddUri(List<Uri> list, string value)
        {
            Uri uri;
            if (Uri.TryCreate(value, UriKind.Absolute, out uri))
                AddUri(list, uri);
        }

        private static void AddUri(List<Uri> list, Uri uri)
        {
            if (uri == null)
                return;
            for (int i = 0; i < list.Count; i++)
            {
                if (String.Equals(list[i].Host, uri.Host, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            list.Add(uri);
        }

        private static bool IsTrustedMtsDomain(string domain)
        {
            string d = NormalizeDomain(domain);
            if (String.IsNullOrEmpty(d))
                return false;

            return d == "mts.ru" || d.EndsWith(".mts.ru", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSafeCookieValue(string value)
        {
            if (value == null)
                return true;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c <= 32 || c >= 127 || c == ';' || c == ',' || c == '"' || c == '\\' || c == '\r' || c == '\n')
                    return false;
            }
            return true;
        }

        private static bool IsValidCookieName(string name)
        {
            if (String.IsNullOrWhiteSpace(name))
                return false;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c <= 32 || c >= 127 || c == '(' || c == ')' || c == '<' || c == '>' ||
                    c == '@' || c == ',' || c == ';' || c == ':' || c == '\\' || c == '"' ||
                    c == '/' || c == '[' || c == ']' || c == '?' || c == '=' || c == '{' || c == '}')
                    return false;
            }
            return true;
        }

        private static string NormalizeDomain(string domain)
        {
            if (String.IsNullOrWhiteSpace(domain))
                return String.Empty;
            return domain.Trim().TrimStart('.').ToLowerInvariant();
        }

        private static string SafePath(string path)
        {
            if (String.IsNullOrEmpty(path))
                return "/";
            return path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path;
        }

        private void Load()
        {
            _cookies.Clear();
            object value;
            if (!IsolatedStorageSettings.ApplicationSettings.TryGetValue(SettingsKey, out value))
                return;

            string text = value as string;
            if (String.IsNullOrEmpty(text))
                return;

            string[] lines = text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string[] p = lines[i].Split('\t');
                if (p.Length < 5)
                    continue;

                StoredCookie cookie = new StoredCookie();
                cookie.Domain = NormalizeDomain(Uri.UnescapeDataString(p[0]));
                cookie.Path = SafePath(Uri.UnescapeDataString(p[1]));
                cookie.Name = Uri.UnescapeDataString(p[2]);
                cookie.Value = Uri.UnescapeDataString(p[3]);
                long ticks;
                if (Int64.TryParse(p[4], out ticks) && ticks > 0)
                    cookie.ExpiresUtc = new DateTime(ticks, DateTimeKind.Utc);

                if ((!cookie.ExpiresUtc.HasValue || cookie.ExpiresUtc.Value > DateTime.UtcNow) &&
                    IsTrustedMtsDomain(cookie.Domain) && IsValidCookieName(cookie.Name) && IsSafeCookieValue(cookie.Value))
                    _cookies.Add(cookie);
            }
        }

        private void Save()
        {
            List<string> lines = new List<string>();
            for (int i = 0; i < _cookies.Count; i++)
            {
                StoredCookie c = _cookies[i];
                string line = Uri.EscapeDataString(NormalizeDomain(c.Domain)) + "\t" +
                              Uri.EscapeDataString(SafePath(c.Path)) + "\t" +
                              Uri.EscapeDataString(c.Name ?? String.Empty) + "\t" +
                              Uri.EscapeDataString(c.Value ?? String.Empty) + "\t" +
                              (c.ExpiresUtc.HasValue ? c.ExpiresUtc.Value.Ticks.ToString() : "0");
                lines.Add(line);
            }

            IsolatedStorageSettings.ApplicationSettings[SettingsKey] = String.Join("\n", lines.ToArray());
            SafeSaveSettings();
        }

        private static void SafeSaveSettings()
        {
            try
            {
                IsolatedStorageSettings.ApplicationSettings.Save();
            }
            catch
            {
            }
        }

        private sealed class StoredCookie
        {
            public string Name { get; set; }
            public string Value { get; set; }
            public string Domain { get; set; }
            public string Path { get; set; }
            public DateTime? ExpiresUtc { get; set; }
        }
    }
}
