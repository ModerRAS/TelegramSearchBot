using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Model.Bilibili;
using System.Text.Json;
using System.Text.Json.Nodes;
using TelegramSearchBot.Manager; // For Env (though BiliCookie will now come from service)
using TelegramSearchBot.Service.Common; // For IAppConfigurationService
// Removed: using Telegram.Bot.Extensions.MarkdownV2; 

namespace TelegramSearchBot.Service.Bilibili;

public class BiliApiService : IBiliApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BiliApiService> _logger;
    private readonly IAppConfigurationService _appConfigService;

    // Bilibili API endpoints
    private const string BiliApiBaseUrl = "https://api.bilibili.com";

    // Regex for Bilibili URLs
    private static readonly Regex BiliUrlParseRegex = new(
        @"bilibili\.com/(?:video/(?:(?<bvid>BV[1-9A-HJ-NP-Za-km-z]{10})|av(?<aid>\d+))/?(?:[?&;]*p=(?<page>\d+))?|bangumi/play/(?:ep(?<epid>\d+)|ss(?<ssid>\d+))/?)|b23\.tv/(?<shortid>\w+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BiliOpusUrlRegex = new(@"(?:https?://)?(?:t\.bilibili\.com/|space\.bilibili\.com/\d+/dynamic)/(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Characters to escape for MarkdownV2
    private static readonly char[] MarkdownV2EscapeChars = { '_', '*', '[', ']', '(', ')', '~', '`', '>', '#', '+', '-', '=', '|', '{', '}', '.', '!' };


    public BiliApiService(IHttpClientFactory httpClientFactory, ILogger<BiliApiService> logger, IAppConfigurationService appConfigService)
    {
        _httpClient = httpClientFactory.CreateClient("BiliApiClient");
        // Configure HttpClient
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Referrer = new Uri("https://www.bilibili.com/");
        _logger = logger;
        _appConfigService = appConfigService; // Store injected service
    }

    // Helper method to escape text for MarkdownV2 parse mode
    private static string EscapeMarkdownV2(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        foreach (char c in MarkdownV2EscapeChars)
        {
            text = text.Replace(c.ToString(), "\\" + c);
        }
        return text;
    }


    public async Task<BiliVideoInfo> GetVideoInfoAsync(string videoUrl)
    {
        _logger.LogInformation("Attempting to get video info for URL: {VideoUrl}", videoUrl);

        if (string.IsNullOrWhiteSpace(videoUrl))
        {
            _logger.LogWarning("Video URL is null or empty.");
            return null;
        }

        string aid = null;
        string bvid = null;
        string epid = null;
        string ssid = null;
        int page = 1;
        string originalUrlToParse = videoUrl;

        // Resolve short URL if necessary
        if (videoUrl.Contains("b23.tv/"))
        {
            originalUrlToParse = await ResolveShortUrlAsync(videoUrl);
            if (originalUrlToParse == videoUrl) // Resolution failed
            {
                _logger.LogWarning("Failed to resolve b23.tv short URL: {VideoUrl}", videoUrl);
                return null;
            }
            _logger.LogInformation("Resolved b23.tv URL {ShortUrl} to {FullUrl}", videoUrl, originalUrlToParse);
        }
        
        // Parse the potentially resolved URL
        var match = BiliUrlParseRegex.Match(originalUrlToParse);
        if (!match.Success)
        {
            _logger.LogWarning("URL did not match Bilibili video/bangumi pattern: {ParsedUrl}", originalUrlToParse);
            return null;
        }

        // Extract IDs from regex match
        bvid = match.Groups["bvid"].Value;
        aid = match.Groups["aid"].Value;
        epid = match.Groups["epid"].Value;
        ssid = match.Groups["ssid"].Value;
        if (match.Groups["page"].Success && int.TryParse(match.Groups["page"].Value, out int p))
        {
            page = p > 0 ? p : 1; // Ensure page is at least 1
        }

        BiliVideoInfo videoInfo = new BiliVideoInfo { OriginalUrl = videoUrl, Page = page };
        JsonNode pgcSeasonJson = null;

        try
        {
            // Handle Bangumi (EP/SS) first to potentially get AID/BVID
            if (!string.IsNullOrWhiteSpace(epid) || !string.IsNullOrWhiteSpace(ssid))
            {
                string pgcApiUrl = $"{BiliApiBaseUrl}/pgc/view/web/season";
                var pgcParams = new Dictionary<string, string>();
                if (!string.IsNullOrWhiteSpace(epid)) pgcParams["ep_id"] = epid;
                else if (!string.IsNullOrWhiteSpace(ssid)) pgcParams["season_id"] = ssid;
                
                pgcSeasonJson = await GetBiliApiJsonAsync(pgcApiUrl, pgcParams);
                if (pgcSeasonJson?["code"]?.GetValue<int>() != 0)
                {
                    _logger.LogWarning("Failed to get PGC season info for {Url}. Response: {Response}", originalUrlToParse, pgcSeasonJson?.ToString() ?? "null");
                    // Don't return null yet, maybe view API still works with original ID
                }
                else
                {
                    // Extract aid/bvid/cid from PGC response
                    var episodesArray = pgcSeasonJson?["result"]?["episodes"]?.AsArray();
                    if (episodesArray != null)
                    {
                        JsonNode targetEpisode = null;
                        if (!string.IsNullOrWhiteSpace(epid))
                        {
                            targetEpisode = episodesArray.FirstOrDefault(ep => ep?["id"]?.GetValue<long>().ToString() == epid);
                        }
                        targetEpisode ??= episodesArray.LastOrDefault(); // Fallback for ss or if epid not found

                        if (targetEpisode != null)
                        {
                            aid = targetEpisode["aid"]?.GetValue<long>().ToString() ?? aid;
                            bvid = targetEpisode["bvid"]?.GetValue<string>() ?? bvid;
                            // Use TryGetValue for safer parsing
                            if (targetEpisode["cid"] is JsonValue cidNode && cidNode.TryGetValue<long>(out long cidValue))
                            {
                                videoInfo.Cid = cidValue;
                            }
                        }
                    }
                    // Populate some info from PGC
                    videoInfo.Title = pgcSeasonJson?["result"]?["title"]?.GetValue<string>() ?? videoInfo.Title;
                    videoInfo.CoverUrl = pgcSeasonJson?["result"]?["cover"]?.GetValue<string>() ?? videoInfo.CoverUrl;
                }
            }

            // Get main video info using /x/web-interface/view
            if (string.IsNullOrWhiteSpace(aid) && string.IsNullOrWhiteSpace(bvid))
            {
                 _logger.LogWarning("Could not determine AID or BVID for URL: {Url}", originalUrlToParse);
                return null;
            }

            string viewApiUrl = $"{BiliApiBaseUrl}/x/web-interface/view";
            var viewParams = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(bvid)) viewParams["bvid"] = bvid;
            else if (!string.IsNullOrWhiteSpace(aid)) viewParams["aid"] = aid;

            JsonNode viewJson = await GetBiliApiJsonAsync(viewApiUrl, viewParams);
            if (viewJson?["code"]?.GetValue<int>() != 0 || viewJson?["data"] == null)
            {
                _logger.LogWarning("Failed to get video view info for {Url}. Response: {Response}", originalUrlToParse, viewJson?.ToString() ?? "null");
                return null; // Cannot proceed without view data
            }

            var data = viewJson["data"];
            // Populate/Overwrite videoInfo with data from view API (usually more complete)
            videoInfo.Aid = data["aid"]?.GetValue<long>().ToString() ?? videoInfo.Aid;
            videoInfo.Bvid = data["bvid"]?.GetValue<string>() ?? videoInfo.Bvid;
            videoInfo.Title = data["title"]?.GetValue<string>() ?? videoInfo.Title;
            videoInfo.Description = data["desc"]?.GetValue<string>();
            videoInfo.OwnerName = data["owner"]?["name"]?.GetValue<string>();
            videoInfo.OwnerMid = data["owner"]?["mid"]?.GetValue<long>() ?? 0;
            videoInfo.CoverUrl = data["pic"]?.GetValue<string>() ?? videoInfo.CoverUrl;
            videoInfo.PublishDateText = data["pubdate"]?.GetValue<long>().ToString(); // Unix timestamp
            videoInfo.TName = data["tname"]?.GetValue<string>();
            videoInfo.TotalPages = data["videos"]?.GetValue<int>() ?? 1;

            // Get page-specific info (Cid, Duration, Dimensions)
            var pages = data["pages"]?.AsArray();
            if (pages != null && pages.Count > 0)
            {
                var currentPageData = pages.FirstOrDefault(p => p?["page"]?.GetValue<int>() == videoInfo.Page) ?? pages.FirstOrDefault();
                if (currentPageData != null)
                {
                    // Use TryGetValue for safer parsing
                    if (currentPageData["cid"] is JsonValue pageCidNode && pageCidNode.TryGetValue<long>(out long pageCid)) videoInfo.Cid = pageCid;
                    if (currentPageData["duration"] is JsonValue pageDurationNode && pageDurationNode.TryGetValue<int>(out int pageDuration)) videoInfo.Duration = pageDuration;
                    if (currentPageData["dimension"]?["width"] is JsonValue wNode && wNode.TryGetValue<int>(out int w)) videoInfo.DimensionWidth = w;
                    if (currentPageData["dimension"]?["height"] is JsonValue hNode && hNode.TryGetValue<int>(out int h)) videoInfo.DimensionHeight = h;
                    // Optionally use page part title: videoInfo.Title = currentPageData["part"]?.GetValue<string>() ?? videoInfo.Title;
                }
            } else if (videoInfo.Cid == 0) { // Single page video, ensure Cid and Duration are read if not set by PGC
                 if (data["cid"] is JsonValue singleCidNode && singleCidNode.TryGetValue<long>(out long singleCid)) videoInfo.Cid = singleCid;
                 if (data["duration"] is JsonValue singleDurationNode && singleDurationNode.TryGetValue<int>(out int singleDuration)) videoInfo.Duration = singleDuration;
            }
            
            // Format helper strings
            videoInfo.FormattedContentInfo = $"{videoInfo.TName ?? "N/A"} - {data["dynamic"]?.GetValue<string>() ?? videoInfo.Description ?? "No description"}";
            videoInfo.MarkdownFormattedLink = $"[{EscapeMarkdownV2(videoInfo.Title ?? "Video")}]({videoInfo.OriginalUrl})"; // Use local escape method

            // Fetch play URLs
            await GetPlayUrlInfoAsync(videoInfo);

            _logger.LogInformation("Successfully fetched video info (including play URLs) for: {VideoTitle}", videoInfo.Title);
            return videoInfo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetVideoInfoAsync for URL: {VideoUrl}", videoUrl);
            return null;
        }
    }

    public async Task<BiliOpusInfo> GetOpusInfoAsync(string opusUrl)
    {
        _logger.LogInformation("Attempting to get opus info for URL: {OpusUrl}", opusUrl);

        if (string.IsNullOrWhiteSpace(opusUrl))
        {
            _logger.LogWarning("Opus URL is null or empty.");
            return null;
        }

        var match = BiliOpusUrlRegex.Match(opusUrl);
        if (!match.Success)
        {
            _logger.LogWarning("URL did not match Bilibili opus pattern: {OpusUrl}", opusUrl);
            return null;
        }

        string dynamicId = match.Groups[1].Value;
        if (string.IsNullOrWhiteSpace(dynamicId))
        {
            _logger.LogWarning("Could not extract dynamic ID from URL: {OpusUrl}", opusUrl);
            return null;
        }

        try
        {
            string apiUrl = $"{BiliApiBaseUrl}/x/polymer/web-dynamic/desktop/v1/detail";
            var queryParams = new Dictionary<string, string> { { "id", dynamicId } };

            JsonNode responseNode = await GetBiliApiJsonAsync(apiUrl, queryParams, useCookies: true);

            if (responseNode?["code"]?.GetValue<int>() != 0 || responseNode?["data"]?["item"] == null)
            {
                _logger.LogWarning("Failed to get opus detail for ID {DynamicId}. Response: {Response}", dynamicId, responseNode?.ToString() ?? "null");
                return null;
            }

            var itemNode = responseNode["data"]["item"];
            var opusInfo = ParseOpusItem(itemNode, opusUrl);
            
            _logger.LogInformation("Successfully fetched opus info for dynamic ID: {DynamicId}", dynamicId);
            return opusInfo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetOpusInfoAsync for dynamic ID: {DynamicId}, URL: {OpusUrl}", dynamicId, opusUrl);
            return null;
        }
    }

    private BiliOpusInfo ParseOpusItem(JsonNode itemNode, string originalUrl)
    {
        if (itemNode == null) return null;

        var opusInfo = new BiliOpusInfo { OriginalUrl = originalUrl };

        // Safely parse dynamic ID
        string idStr = itemNode["basic"]?["comment_id_str"]?.GetValue<string>() ?? itemNode["id_str"]?.GetValue<string>();
        if (long.TryParse(idStr, out long dynId))
        {
            opusInfo.DynamicId = dynId;
        } else {
             _logger.LogWarning("Could not parse dynamic ID from itemNode: {ItemNode}", itemNode.ToString());
             return null; 
        }

        var modules = itemNode["modules"];
        var moduleAuthor = modules?["module_author"];
        opusInfo.UserName = moduleAuthor?["name"]?.GetValue<string>();
        opusInfo.UserMid = moduleAuthor?["mid"]?.GetValue<long>() ?? 0;
        opusInfo.Timestamp = moduleAuthor?["pub_ts"]?.GetValue<long>() ?? (moduleAuthor?["ptime"]?.GetValue<long>() ?? 0);

        // Extract description text safely
        var moduleDescNode = modules?["module_descriptor"]?["desc"] ?? modules?["module_dynamic"]?["desc"];
        if (moduleDescNode?["rich_text_nodes"]?.AsArray() is JsonArray richTextNodes)
        {
            opusInfo.ContentText = string.Join("", richTextNodes
                .Select(n => n?["text"]?.GetValue<string>() ?? (n?["emoji"]?["text"]?.GetValue<string>() ?? "")));
        }
        else
        {
            opusInfo.ContentText = moduleDescNode?["text"]?.GetValue<string>();
        }

        // Parse major content module
        var moduleDynamic = modules?["module_dynamic"];
        var major = moduleDynamic?["major"];
        if (major != null)
        {
            ParseOpusMajorContent(major, opusInfo);
        }
        
        // Fallback for images in additional/draw modules if major parsing didn't find them
        if (!opusInfo.ImageUrls.Any())
        {
             if (moduleDynamic?["additional"]?["type"]?.GetValue<string>() == "ADDITIONAL_TYPE_DRAW")
             {
                 var drawItems = moduleDynamic["additional"]?["reserve_attach_card"]?["reserve_draw"]?["item"]?["draw_item_list"]?.AsArray();
                 if (drawItems != null)
                 {
                    opusInfo.ImageUrls.AddRange(drawItems.Select(d => d?["src"]?.GetValue<string>()).Where(s => s != null));
                 }
             }
             else if (moduleDynamic?["type"]?.GetValue<string>() == "DYNAMIC_TYPE_DRAW") // Older draw type
             {
                 var drawItems = moduleDynamic["draw"]?["items"]?.AsArray();
                 if (drawItems != null)
                 {
                     opusInfo.ImageUrls.AddRange(drawItems.Select(d => d?["src"]?.GetValue<string>()).Where(s => s != null));
                 }
             }
        }

        // Construct FormattedContentMarkdown and MarkdownFormattedLink
        opusInfo.FormattedContentMarkdown = opusInfo.ContentText ?? ""; // Ensure not null
        if (opusInfo.ForwardedOpus != null)
        {
            // Recursively build the forward chain text if needed, or keep it simple
            opusInfo.FormattedContentMarkdown += $"\n// @{opusInfo.ForwardedOpus.UserName}: {opusInfo.ForwardedOpus.ContentText ?? ""}";
        }
        
        string linkText = opusInfo.OriginalResource?.Title ?? "动态链接";
        string linkUrl = opusInfo.OriginalResource?.Url ?? $"https://t.bilibili.com/{opusInfo.DynamicId}";
        opusInfo.MarkdownFormattedLink = $"[{EscapeMarkdownV2(linkText)}]({linkUrl})"; // Use local escape method
        // Add original dynamic link if it was a share
        if (opusInfo.OriginalResource != null)
        {
             opusInfo.MarkdownFormattedLink += $"\n[原始动态](https://t.bilibili.com/{opusInfo.DynamicId})";
        }

        return opusInfo;
    }

    private void ParseOpusMajorContent(JsonNode majorNode, BiliOpusInfo currentOpus)
    {
        string type = majorNode?["type"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(type)) return;

        string typeKey = type.Replace("MAJOR_TYPE_", "").ToLower(); // e.g., "draw", "archive"
        JsonNode contentNode = majorNode[typeKey]; // Get the node corresponding to the type

        switch (type)
        {
            case "MAJOR_TYPE_DRAW":
                var drawItems = contentNode?["items"]?.AsArray();
                if (drawItems != null)
                {
                    currentOpus.ImageUrls.AddRange(drawItems.Select(d => d?["src"]?.GetValue<string>()).Where(s => s != null));
                }
                break;
            case "MAJOR_TYPE_ARCHIVE":
            case "MAJOR_TYPE_PGC": // Treat PGC similar to Archive for resource info
            case "MAJOR_TYPE_ARTICLE":
            case "MAJOR_TYPE_MUSIC":
            case "MAJOR_TYPE_COMMON": // Generic share card
            case "MAJOR_TYPE_LIVE_RCMD": // Live recommendation card
                if (contentNode != null)
                {
                    currentOpus.OriginalResource = new OpusOriginalResourceInfo
                    {
                        Type = typeKey,
                        Title = contentNode["title"]?.GetValue<string>() ?? contentNode["head_text"]?.GetValue<string>(), // Common fields
                        Url = contentNode["jump_url"]?.GetValue<string>()?.StartsWith("//") == true ?
                              "https:" + contentNode["jump_url"].GetValue<string>() :
                              contentNode["jump_url"]?.GetValue<string>(),
                        CoverUrl = contentNode["cover"]?.GetValue<string>() ?? contentNode["covers"]?.AsArray().FirstOrDefault()?.GetValue<string>(), // Different cover fields
                        Aid = contentNode["aid"]?.GetValue<string>(), // Specific to archive/pgc?
                        Bvid = contentNode["bvid"]?.GetValue<string>(), // Specific to archive/pgc?
                        Description = contentNode["desc"]?.GetValue<string>() ?? contentNode["sub_title"]?.GetValue<string>() ?? contentNode["desc1"]?.GetValue<string>() // Various description fields
                    };
                }
                break;
            case "MAJOR_TYPE_OPUS": // Forwarded Opus
                var opusNode = contentNode; // majorNode["opus"]
                if (opusNode != null)
                {
                    // Recursively parse the forwarded item
                    currentOpus.ForwardedOpus = ParseOpusItem(opusNode, opusNode["jump_url"]?.GetValue<string>());
                }
                break;
            default:
                _logger.LogDebug("Unhandled major opus type: {MajorType}", type);
                break;
        }
    }

    // Helper method to handle b23.tv short links
    private async Task<string> ResolveShortUrlAsync(string shortUrl)
    {
        try
        {
            int redirectCount = 0;
            string currentUrl = shortUrl;

            // Ensure the URL has a scheme
            if (!Uri.TryCreate(currentUrl, UriKind.Absolute, out Uri testUri) || testUri.Scheme == null)
            {
                currentUrl = "https://" + currentUrl; // Default to https
            }
            
            // Use a temporary handler to disable automatic redirects for this specific task
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var tempClient = new HttpClient(handler);
            tempClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (iPhone; CPU iPhone OS 13_2_3 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/13.0.3 Mobile/15E148 Safari/604.1");

            while (redirectCount < 5) // Limit redirects
            {
                var request = new HttpRequestMessage(HttpMethod.Get, currentUrl); 
                var response = await tempClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                if (response.Headers.Location != null)
                {
                    currentUrl = response.Headers.Location.AbsoluteUri;
                    // If it redirects back to b23.tv (unlikely but possible), loop again. Otherwise, return the bilibili.com URL.
                    if (!currentUrl.Contains("b23.tv/"))
                    {
                        return currentUrl;
                    }
                    redirectCount++;
                }
                else
                {
                    // No Location header, assume this is the final URL or an error page
                    return currentUrl; // Return the last URL we tried
                }
            }
             _logger.LogWarning("Too many redirects resolving short URL: {ShortUrl}", shortUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving short URL: {ShortUrl}", shortUrl);
        }
        return shortUrl; // Return original if resolution failed
    }

    // Helper to make API calls and parse JSON
    private async Task<JsonNode> GetBiliApiJsonAsync(string apiUrl, Dictionary<string, string> queryParams = null, bool useCookies = false)
    {
        var uriBuilder = new UriBuilder(apiUrl);
        if (queryParams != null && queryParams.Any())
        {
            var query = string.Join("&", queryParams.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
            uriBuilder.Query = query;
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, uriBuilder.Uri);
            if (useCookies)
            {
                string biliCookie = await _appConfigService.GetConfigurationValueAsync(AppConfigurationService.BiliCookieKey);
                if (!string.IsNullOrWhiteSpace(biliCookie))
                {
                    request.Headers.Add("Cookie", biliCookie);
                    _logger.LogDebug("Using BiliCookie from AppConfigurationService for API request: {ApiUrl}", uriBuilder.ToString());
                }
                else
                {
                    _logger.LogWarning("BiliCookie use was requested for API call to {ApiUrl}, but it's not configured in the database.", uriBuilder.ToString());
                }
            }

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode(); // Throws if not 2xx
            var jsonString = await response.Content.ReadAsStringAsync();
            return JsonNode.Parse(jsonString);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP request to BiliAPI failed for URL: {ApiUrl}", uriBuilder.ToString());
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse JSON response from BiliAPI for URL: {ApiUrl}", uriBuilder.ToString());
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Generic error fetching from BiliAPI for URL: {ApiUrl}", uriBuilder.ToString());
            return null;
        }
    }

    // Helper to get Play URL info (DASH and DURL)
    private async Task GetPlayUrlInfoAsync(BiliVideoInfo videoInfo)
    {
        if (videoInfo.Cid == 0 || (string.IsNullOrWhiteSpace(videoInfo.Aid) && string.IsNullOrWhiteSpace(videoInfo.Bvid)))
        {
            _logger.LogWarning("Cannot fetch play URLs without Cid and (Aid or Bvid) for {VideoTitle}", videoInfo.Title);
            return;
        }

        string playUrlApi = $"{BiliApiBaseUrl}/x/player/playurl";
        
        // Attempt to get DASH streams first
        var dashParams = new Dictionary<string, string>
        {
            ["cid"] = videoInfo.Cid.ToString(),
            ["qn"] = "0", // Max quality for DASH
            ["fnver"] = "0",
            ["fnval"] = "4048", // Request DASH format
            ["fourk"] = "1"
        };
        if (!string.IsNullOrWhiteSpace(videoInfo.Bvid)) dashParams["bvid"] = videoInfo.Bvid;
        else dashParams["avid"] = videoInfo.Aid;

        JsonNode dashResponse = await GetBiliApiJsonAsync(playUrlApi, dashParams, useCookies: true); 

        if (dashResponse?["code"]?.GetValue<int>() == 0 && dashResponse["data"]?["dash"] != null)
        {
            var dashData = dashResponse["data"]["dash"];
            var videoStreams = dashData["video"]?.AsArray();
            var audioStreams = dashData["audio"]?.AsArray();

            if (videoStreams != null && audioStreams != null && videoStreams.Any() && audioStreams.Any())
            {
                // Select best available video and audio stream (simplified: highest bandwidth)
                var bestVideo = videoStreams.OrderByDescending(v => v?["bandwidth"]?.GetValue<long>() ?? 0).FirstOrDefault();
                var bestAudio = audioStreams.OrderByDescending(a => a?["bandwidth"]?.GetValue<long>() ?? 0).FirstOrDefault();

                if (bestVideo != null && bestAudio != null)
                {
                     long videoBw = bestVideo["bandwidth"]?.GetValue<long>() ?? 0;
                     long audioBw = bestAudio["bandwidth"]?.GetValue<long>() ?? 0;
                     long estimatedSize = (videoBw + audioBw) * (videoInfo.Duration > 0 ? videoInfo.Duration : 300) / 8; // Estimate size

                    videoInfo.DashStreams = new DashStreamInfo
                    {
                        VideoStream = new DashMedia
                        {
                            Url = bestVideo["baseUrl"]?.GetValue<string>() ?? bestVideo["base_url"]?.GetValue<string>(),
                            Codec = bestVideo["codecs"]?.GetValue<string>(),
                            SizeBytes = estimatedSize > 0 ? estimatedSize : 0, // Use estimate, ensure non-negative
                            QualityDescription = bestVideo["id"]?.GetValue<int>().ToString()
                        },
                        AudioStream = new DashMedia
                        {
                            Url = bestAudio["baseUrl"]?.GetValue<string>() ?? bestAudio["base_url"]?.GetValue<string>(),
                            Codec = bestAudio["codecs"]?.GetValue<string>(),
                            SizeBytes = 0, // Size included in VideoStream estimate
                            QualityDescription = bestAudio["id"]?.GetValue<int>().ToString()
                        }
                    };
                    _logger.LogInformation("Successfully fetched DASH streams for {VideoTitle}", videoInfo.Title);
                }
            }
        }
        else
        {
            _logger.LogWarning("Failed to get DASH streams for {VideoTitle}. Response: {Response}", videoInfo.Title, dashResponse?.ToString() ?? "null");
        }

        // Fetch normal DURL streams if DASH failed or as fallback
        if (videoInfo.DashStreams == null) // Only fetch DURL if DASH is not available
        {
            int[] preferredQualities = { 80, 64, 32, 16 }; // 1080P, 720P, 480P, 360P
            foreach (var qn in preferredQualities)
            {
                var durlParams = new Dictionary<string, string>
                {
                    ["cid"] = videoInfo.Cid.ToString(),
                    ["qn"] = qn.ToString(),
                    ["fnver"] = "0",
                    ["fnval"] = "16" // Request MP4
                };
                if (!string.IsNullOrWhiteSpace(videoInfo.Bvid)) durlParams["bvid"] = videoInfo.Bvid;
                else durlParams["avid"] = videoInfo.Aid;
                
                JsonNode durlResponse = await GetBiliApiJsonAsync(playUrlApi, durlParams, useCookies: true);

                if (durlResponse?["code"]?.GetValue<int>() == 0 && durlResponse["data"]?["durl"]?.AsArray() is JsonArray durlArray && durlArray.Count > 0)
                {
                    foreach(var durlItem in durlArray)
                    {
                        if (durlItem == null) continue;
                        // Use TryGetValue for safer parsing
                        long sizeBytes = 0;
                        if (durlItem["size"] is JsonValue sizeNode) sizeNode.TryGetValue<long>(out sizeBytes);
                        
                        int qualityInt = 0;
                        if (durlResponse["data"]["quality"] is JsonValue qualityNode) qualityNode.TryGetValue<int>(out qualityInt);

                        videoInfo.PlayUrls.Add(new PlayUrlDetail
                        {
                            QualityNumeric = qn,
                            QualityDescription = qualityInt > 0 ? qualityInt.ToString() : qn.ToString(),
                            Url = durlItem["url"]?.GetValue<string>(),
                            SizeBytes = sizeBytes,
                            BackupUrls = durlItem["backup_url"]?.AsArray().Select(b => b?.GetValue<string>()).Where(s => s != null).ToList() ?? new List<string>()
                        });
                    }
                    _logger.LogInformation("Successfully fetched DURL streams (QN: {QN}) for {VideoTitle}", qn, videoInfo.Title);
                    if (videoInfo.PlayUrls.Any()) break; // Stop after finding the first available quality
                }
            }
        }

        if (!videoInfo.PlayUrls.Any() && videoInfo.DashStreams == null)
        {
             _logger.LogWarning("Failed to get any playable streams (DASH or DURL) for {VideoTitle}", videoInfo.Title);
        }
    }
}
