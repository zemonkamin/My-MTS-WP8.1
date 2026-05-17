using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Мой_МТС.Services
{
    public sealed class MtsHttpClient
    {
        private readonly CookieStore _cookies;

        public MtsHttpClient(CookieStore cookies)
        {
            _cookies = cookies;
        }

        public CookieStore Cookies
        {
            get { return _cookies; }
        }

        public Task<HttpResult> GetAsync(string url, IDictionary<string, string> headers)
        {
            return SendAsync("GET", url, null, headers, null);
        }

        public Task<HttpResult> PostJsonAsync(string url, string json, IDictionary<string, string> headers)
        {
            return SendAsync("POST", url, json, headers, "application/json;charset=UTF-8");
        }

        public async Task<HttpResult> SendAsync(string method, string url, string body, IDictionary<string, string> headers, string contentType)
        {
            Uri uri = new Uri(url, UriKind.Absolute);
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
            request.Method = method;
            request.AllowAutoRedirect = true;

            CookieContainer cookieContainer = _cookies.CreateContainer(uri);
            request.CookieContainer = cookieContainer;

            if (!String.IsNullOrEmpty(contentType))
                request.ContentType = contentType;

            if (headers != null)
            {
                foreach (KeyValuePair<string, string> item in headers)
                    SetHeader(request, item.Key, item.Value);
            }

            if (!String.IsNullOrEmpty(body))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                using (Stream stream = await Task<Stream>.Factory.FromAsync(request.BeginGetRequestStream, request.EndGetRequestStream, null))
                {
                    await stream.WriteAsync(bytes, 0, bytes.Length);
                }
            }

            HttpWebResponse response = null;
            try
            {
                response = (HttpWebResponse)await Task<WebResponse>.Factory.FromAsync(request.BeginGetResponse, request.EndGetResponse, null);
            }
            catch (WebException ex)
            {
                response = ex.Response as HttpWebResponse;
                if (response == null)
                    throw;
            }

            string text = String.Empty;
            using (response)
            {
                Uri responseUri = response.ResponseUri ?? uri;

                _cookies.Capture(responseUri, response.Headers, response.Cookies);
                _cookies.CaptureFromContainer(cookieContainer, responseUri);

                Stream stream = response.GetResponseStream();
                if (stream != null)
                {
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                        text = await reader.ReadToEndAsync();
                }

                return new HttpResult
                {
                    StatusCode = (int)response.StatusCode,
                    ResponseUri = responseUri,
                    Body = text
                };
            }
        }

        private static void SetHeader(HttpWebRequest request, string name, string value)
        {
            if (String.IsNullOrEmpty(name) || value == null)
                return;

            try
            {
                string lower = name.ToLowerInvariant();
                if (lower == "accept")
                    request.Accept = value;
                else if (lower == "content-type")
                    request.ContentType = value;
                else if (lower == "referer")
                    request.Headers["Referer"] = value;
                else if (lower == "user-agent")
                    request.UserAgent = value;
                else
                    request.Headers[name] = value;
            }
            catch
            {

            }
        }
    }

    public sealed class HttpResult
    {
        public int StatusCode { get; set; }
        public Uri ResponseUri { get; set; }
        public string Body { get; set; }

        public bool IsSuccess
        {
            get { return StatusCode >= 200 && StatusCode < 300; }
        }
    }
}
