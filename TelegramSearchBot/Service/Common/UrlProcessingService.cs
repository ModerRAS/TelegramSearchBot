using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web; // For HttpUtility. NuGet: System.Web.HttpUtility if not available by default
using TelegramSearchBot.Intrerface; // Added for IService

namespace TelegramSearchBot.Service.Common
{
    
    public class UrlProcessingService : IService // Implements IService
    {
        public string ServiceName => nameof(UrlProcessingService); // Implementation of IService.ServiceName

        private readonly HttpClient _httpClient;
        // Common tracking parameters. This list can be expanded or moved to configuration.
        private static readonly HashSet<string> TrackingParameters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Bilibili specific (from user example)
            "buvid", "from_spmid", "is_story_h5", "mid", "plat_id", "share_from", "share_medium", "share_plat",
            "share_session_id", "share_source", "share_tag", "spmid", "timestamp", // Removed start_progress
            "unique_k", "up_id", "vd_source", "seid", "b_lsid", "launch_id", "session_id", "ab_id",
            // Common UTM parameters
            "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content",
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

        public UrlProcessingService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public List<string> ExtractUrls(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new List<string>();
            }

            // Regex to find URLs (http, https, www).
            // It tries to avoid matching URLs ending with punctuation that's part of the sentence.
            const string urlPattern = @"\b(?:https?://|www\.)[-A-Z0-9+&@#/%?=~_|$!:,.;]*[A-Z0-9+&@#/%=~_|$]";
            var matches = Regex.Matches(text, urlPattern, RegexOptions.IgnoreCase);
            
            var extractedUrls = new List<string>();
            foreach (Match match in matches.Cast<Match>())
            {
                string url = match.Value;
                // Prepend http:// to www URLs for Uri class compatibility if no scheme is present
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
                // Consider logging this invalid URL format
                return null;
            }

            try
            {
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
                // SendAsync with HttpCompletionOption.ResponseHeadersRead is efficient
                HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                
                // The final URL after all redirects
                return response.RequestMessage?.RequestUri?.ToString();
            }
            catch (HttpRequestException e)
            {
                Console.Error.WriteLine($"Error fetching URL '{originalUrl}': {e.Message}"); // Replace with proper logging
                return null;
            }
            catch (TaskCanceledException e) // Handles timeouts
            {
                Console.Error.WriteLine($"Timeout fetching URL '{originalUrl}': {e.Message}"); // Replace with proper logging
                return null;
            }
            catch (Exception e) // Catch-all for other unexpected errors
            {
                Console.Error.WriteLine($"Unexpected error fetching URL '{originalUrl}': {e.Message}"); // Replace with proper logging
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
                // Consider logging this
                return url; // Return original if it's not a valid absolute URI
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
                // Failed to get final redirected URL. As per requirement, we need the link *after* 301.
                // So, if redirection fails, we return null.
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
                    // Optional: Add only if cleaned URL is different from original extracted one,
                    // or if it's significantly shorter (e.g. after unshortening)
                    // For now, add any successfully processed URL.
                    processedUrls.Add(processedUrl);
                }
            }
            // Return distinct processed URLs, in case multiple original URLs resolve to the same cleaned URL
            return processedUrls.Distinct().ToList(); 
        }
    }
}
