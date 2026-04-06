using System;
using System.Net.Http;
using System.Net.Http.Headers;

namespace OpenClaudeCodeWPF.Utils
{
    public static class HttpClientFactory
    {
        private static readonly object _lock = new object();

        public static HttpClient Create(int timeoutSeconds = 120)
        {
            var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }

        public static HttpClient CreateForStreaming(int timeoutSeconds = 300)
        {
            var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            return client;
        }
    }
}
