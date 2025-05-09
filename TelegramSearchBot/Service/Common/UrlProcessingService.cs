using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web; // For HttpUtility. NuGet: System.Web.HttpUtility if not available by default
using Microsoft.Extensions.Logging; // Added for ILogger
using TelegramSearchBot.Intrerface;

namespace TelegramSearchBot.Service.Common
{
    
    public class UrlProcessingService : IService
    {
        public string ServiceName => nameof(UrlProcessingService);

        private readonly HttpClient _httpClient;
        private readonly ILogger<UrlProcessingService> _logger;

        // Common tracking parameters to be removed from URLs.
        private static readonly HashSet<string> TrackingParameters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Bilibili specific
            "buvid", "from_spmid", "is_story_h5", "mid", "plat_id", "share_from", "share_medium", "share_plat", "share_pattern", 
            "share_session_id", "share_source", "share_tag", "spmid", "timestamp", 
            "unique_k", "up_id", "vd_source", "seid", "b_lsid", "launch_id", "session_id", "ab_id",
            // Common UTM parameters
            "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content",
            // Taobao specific
            "tk", "suid", "shareUniqueId", "ut_sk", "un", "share_crt_v", "un_site", "spm", 
            "wxsign", "tbSocialPopKey", "sp_tk", "cpp", "shareurl", "short_name", "bxsign", "app",
            "sourceType", 
            // Other common tracking parameters
            "fbclid", "gclid", "msclkid", "mc_eid", "yclid", "_hsenc", "_hsmi",
            "vero_conv", "vero_id", "trk", "trkCampaign", "sc_ichannel", "otc",
            "igshid", // Instagram
            "si",     // Spotify, YouTube Music, etc.
            "WT.mc_id", "WT.tsrc", // Webtrends
            "piwik_campaign", "piwik_kwd", // Matomo (formerly Piwik)
            "cjevent", // Commission Junction
            "ICID", // Adobe Analytics
            "mkt_tok", // Marketo
            "elqTrackId", // Eloqua
            "_openstat" // Yandex Metrica
        };

        public UrlProcessingService(HttpClient httpClient, ILogger<UrlProcessingService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public List<string> ExtractUrls(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new List<string>();
            }

            // Regex to find URLs (http, https, www).
            const string urlPattern = @"\b(?:https?://|www\.)[-A-Z0-9+&@#/%?=~_|$!:,.;]*[A-Z0-9+&@#/%=~_|$]";
            var matches = Regex.Matches(text, urlPattern, RegexOptions.IgnoreCase);
            
            var extractedUrls = new List<string>();
            foreach (Match match in matches.Cast<Match>())
            {
                string url = match.Value;
                // Prepend http:// to www URLs if no scheme is present
                if (url.StartsWith("www.", StringComparison.OrdinalIgnoreCase) && 
                    !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                    !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    url = "http://" + url;
                }
                extractedUrls.Add(url);
            }
            return extractedUrls;
        }
        
        private async Task<string?> GetFinalRedirectedUrlAsync(string originalUrl)
        {
            if (!Uri.TryCreate(originalUrl, UriKind.Absolute, out var uri))
            {
                _logger.LogWarning("Invalid URL format: {OriginalUrl}", originalUrl);
                return null;
            }

            try
            {
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/97.0.4692.99 Safari/537.36");
                
                HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                response.EnsureSuccessStatusCode(); 

                var resolvedByHttpClientUrl = response.RequestMessage?.RequestUri?.ToString();
                _logger.LogInformation("Original URL: {OriginalUrl} -> HttpClient initially resolved to: {ResolvedByHttpClientUrl}", originalUrl, resolvedByHttpClientUrl);

                // Check if HttpClient already resolved to a likely final target (e.g., different host)
                bool isLikelyHttpRedirectedToFinal = resolvedByHttpClientUrl != null && 
                                                     Uri.TryCreate(originalUrl, UriKind.Absolute, out var originalUriObj) &&
                                                     Uri.TryCreate(resolvedByHttpClientUrl, UriKind.Absolute, out var resolvedUriObj) &&
                                                     originalUriObj.Host != resolvedUriObj.Host;

                if (isLikelyHttpRedirectedToFinal)
                {
                    // Assume standard HTTP redirect is sufficient if host changed
                    _logger.LogInformation("Assuming HTTP redirect to {ResolvedByHttpClientUrl} is sufficient for {OriginalUrl}.", resolvedByHttpClientUrl, originalUrl);
                    return resolvedByHttpClientUrl;
                }
                else
                {
                    // If host didn't change, try parsing HTML for JS pattern as fallback
                    _logger.LogInformation("HTTP client did not significantly redirect for {OriginalUrl}. Reading content to check for JS redirect pattern.", originalUrl);
                    string htmlContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    Match match = Regex.Match(htmlContent, @"var\s+url\s*=\s*'([^']+)';"); 
                    if (match.Success)
                    {
                        string extractedUrl = match.Groups[1].Value;
                        _logger.LogInformation("Extracted URL from JS: {ExtractedUrl} for original: {OriginalUrl}", extractedUrl, originalUrl);
                        if (Uri.TryCreate(extractedUrl, UriKind.Absolute, out _))
                        {
                            return extractedUrl; // Return URL found in JS
                        }
                        else
                        {
                            _logger.LogWarning("Extracted URL '{ExtractedUrl}' from JS for original '{OriginalUrl}' is not a valid absolute URI.", extractedUrl, originalUrl);
                        }
                    }
                    else
                    {
                        _logger.LogInformation("JS redirect pattern not found in HTML content for original URL: {OriginalUrl}", originalUrl);
                    }
                    
                    // Fallback to whatever HttpClient gave us if JS parsing fails
                    _logger.LogInformation("Falling back to HttpClient's resolved URL: {ResolvedByHttpClientUrl} for original: {OriginalUrl}", resolvedByHttpClientUrl, originalUrl);
                    return resolvedByHttpClientUrl; 
                }
            }
            catch (HttpRequestException e)
            {
                _logger.LogError(e, "Error fetching URL '{OriginalUrl}' due to HttpRequestException.", originalUrl);
                return null;
            }
            catch (TaskCanceledException e) // Handles timeouts
            {
                _logger.LogWarning(e, "Timeout fetching URL '{OriginalUrl}'.", originalUrl);
                return null;
            }
            catch (Exception e) // Catch-all for other unexpected errors
            {
                _logger.LogError(e, "Unexpected error fetching URL '{OriginalUrl}'.", originalUrl);
                return null;
            }
        }

        private string? CleanUrlOfTrackingParameters(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return url;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                _logger.LogWarning("Cannot clean URL as it's not a valid absolute URI: {Url}", url);
                return url; 
            }

            var queryParameters = HttpUtility.ParseQueryString(uri.Query);
            var keysToRemove = queryParameters.AllKeys
                                .Where(key => key != null && TrackingParameters.Contains(key))
                                .ToList();

            foreach (var key in keysToRemove)
            {
                queryParameters.Remove(key);
            }

            var uriBuilder = new UriBuilder(uri);

            if (queryParameters.Count > 0)
            {
                uriBuilder.Query = string.Join("&", queryParameters.AllKeys
                    .SelectMany(key => queryParameters.GetValues(key) ?? Array.Empty<string>(), 
                                (key, value) => $"{HttpUtility.UrlEncode(key)}={HttpUtility.UrlEncode(value)}"));
            }
            else
            {
                uriBuilder.Query = string.Empty;
            }
            
            return uriBuilder.Uri.ToString();
        }

        public async Task<string?> ProcessUrlAsync(string originalUrl)
        {
            string? finalUrl = await GetFinalRedirectedUrlAsync(originalUrl).ConfigureAwait(false);
            
            if (finalUrl == null)
            {
                _logger.LogWarning("Failed to get final URL for {OriginalUrl} after attempting redirects and JS parsing.", originalUrl);
                return null; 
            }

            return CleanUrlOfTrackingParameters(finalUrl);
        }

        public async Task<List<string>> ProcessUrlsInTextAsync(string text)
        {
            var extractedUrls = ExtractUrls(text);
            var processedUrls = new List<string>();

            if (!extractedUrls.Any())
            {
                return processedUrls;
            }

            foreach (var url in extractedUrls)
            {
                var processedUrl = await ProcessUrlAsync(url).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(processedUrl))
                {
                    processedUrls.Add(processedUrl);
                }
            }
            return processedUrls.Distinct().ToList(); 
        }
    }
}
