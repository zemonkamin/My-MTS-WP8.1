using Мой_МТС.Models;

namespace Мой_МТС.Services
{
    public static class AppServices
    {
        private static readonly CookieStore CookieStore = new CookieStore();
        private static readonly MtsHttpClient HttpClient = new MtsHttpClient(CookieStore);

        public static readonly MtsAuthService Auth = new MtsAuthService(HttpClient);
        public static readonly MtsLkService Lk = new MtsLkService(HttpClient);

        public static AccountDashboard LastDashboard { get; set; }
    }
}
