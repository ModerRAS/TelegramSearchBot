using System;
using System.Net;
using System.Net.Http;

namespace TelegramSearchBot.Helper {
    public static class HttpClientHelper {
        public static IWebProxy GetSystemProxyOrFallback() {
            var systemProxy = WebRequest.DefaultWebProxy;

            if (systemProxy == null)
                return null;

            var testHttpUri = new Uri("http://www.example.com");
            var testHttpsUri = new Uri("https://www.example.com");

            var httpProxy = systemProxy.GetProxy(testHttpUri);
            var httpsProxy = systemProxy.GetProxy(testHttpsUri);

            bool httpEnabled = httpProxy != null && httpProxy != testHttpUri;
            bool httpsEnabled = httpsProxy != null && httpsProxy != testHttpsUri;

            if (httpsEnabled) {
                return systemProxy; // 系统 HTTPS 代理已设置
            }

            if (httpEnabled) {
                // 系统未设置 HTTPS，但设置了 HTTP，就用 HTTP 代理伪装成 HTTPS 的
                var fallbackProxy = new WebProxy(httpProxy) {
                    Credentials = CredentialCache.DefaultCredentials
                };
                return fallbackProxy;
            }

            return null;
        }

        /// <summary>
        /// 创建使用系统代理的HttpClient
        /// </summary>
        public static HttpClient CreateProxyHttpClient() {
            var proxy = GetSystemProxyOrFallback();

            var handler = new HttpClientHandler {
                UseProxy = true,
                Proxy = proxy,
                DefaultProxyCredentials = CredentialCache.DefaultCredentials,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            return new HttpClient(handler) {
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        /// <summary>
        /// 创建使用系统代理的HttpClientHandler
        /// </summary>
        public static HttpClientHandler CreateProxyHandler() {
            return new HttpClientHandler {
                UseProxy = true,
                Proxy = WebRequest.DefaultWebProxy,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
        }
    }
}
