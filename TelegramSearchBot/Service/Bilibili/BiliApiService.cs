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
using TelegramSearchBot.Helper;

namespace TelegramSearchBot.Service.Bilibili;

public class BiliApiService : IBiliApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BiliApiService> _logger;
    private readonly IAppConfigurationService _appConfigService;

    private const string BiliApiBaseUrl = "https://api.bilibili.com";

    public BiliApiService(IHttpClientFactory httpClientFactory, ILogger<BiliApiService> logger, IAppConfigurationService appConfigService)
    {
        _httpClient = httpClientFactory.CreateClient("BiliApiClient");
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Referrer = new Uri("https://www.bilibili.com/");
        _logger = logger;
        _appConfigService = appConfigService;
    }

    public async Task<BiliVideoInfo> GetVideoInfoAsync(string videoUrl)
    {
        _logger.LogInformation("Attempting to get video info for URL: {VideoUrl}", videoUrl);
        if (string.IsNullOrWhiteSpace(videoUrl)) { _logger.LogWarning("Video URL is null or empty."); return null; }

        string aid = null, bvid = null, epid = null, ssid = null;
        int page = 1; string originalUrlToParse = videoUrl;

            if (videoUrl.Contains("b23.tv/")) {
            originalUrlToParse = await BiliHelper.ResolveShortUrlAsync(videoUrl, _logger);
            if (originalUrlToParse == videoUrl) { _logger.LogWarning("Failed to resolve b23.tv short URL: {VideoUrl}", videoUrl); return null; }
            _logger.LogInformation("Resolved b23.tv URL {ShortUrl} to {FullUrl}", videoUrl, originalUrlToParse);
        }
        
        var match = BiliHelper.BiliUrlParseRegex.Match(originalUrlToParse);
        if (!match.Success) { _logger.LogWarning("URL did not match Bilibili video/bangumi pattern: {ParsedUrl}", originalUrlToParse); return null; }

        bvid = match.Groups["bvid"].Value; aid = match.Groups["aid"].Value; epid = match.Groups["epid"].Value; ssid = match.Groups["ssid"].Value;
        if (match.Groups["page"].Success && int.TryParse(match.Groups["page"].Value, out int pVal)) 
            page = pVal > 0 ? pVal : 1;

        BiliVideoInfo videoInfo = new BiliVideoInfo { OriginalUrl = videoUrl, Page = page };

        try {
            if (!string.IsNullOrWhiteSpace(epid) || !string.IsNullOrWhiteSpace(ssid)) {
                var pgcParams = new Dictionary<string, string>();
                if (!string.IsNullOrWhiteSpace(epid)) pgcParams["ep_id"] = epid; else pgcParams["season_id"] = ssid;
                JsonNode pgcSeasonJson = await GetBiliApiJsonAsync($"{BiliApiBaseUrl}/pgc/view/web/season", pgcParams);
                if (pgcSeasonJson?["code"]?.GetValue<int>() == 0 && pgcSeasonJson["result"] != null) {
                    var episodesArray = pgcSeasonJson["result"]?["episodes"]?.AsArray();
                    if (episodesArray != null) {
                        JsonNode targetEpisode = string.IsNullOrWhiteSpace(epid) ? episodesArray.LastOrDefault() : episodesArray.FirstOrDefault(ep => ep?["id"]?.GetValue<long>().ToString() == epid);
                        targetEpisode ??= episodesArray.LastOrDefault();
                        if (targetEpisode != null) {
                            aid = targetEpisode["aid"]?.GetValue<long>().ToString() ?? aid; bvid = targetEpisode["bvid"]?.GetValue<string>() ?? bvid;
                            if (targetEpisode["cid"] is JsonValue cidNode && cidNode.TryGetValue<long>(out long cidVal)) videoInfo.Cid = cidVal;
                        }
                    }
                    videoInfo.Title = pgcSeasonJson["result"]?["title"]?.GetValue<string>() ?? videoInfo.Title;
                    videoInfo.CoverUrl = pgcSeasonJson["result"]?["cover"]?.GetValue<string>() ?? videoInfo.CoverUrl;
                } else { _logger.LogWarning("Failed to get PGC season info. Response: {Response}", pgcSeasonJson?.ToString() ?? "null"); }
            }

            if (string.IsNullOrWhiteSpace(aid) && string.IsNullOrWhiteSpace(bvid)) { _logger.LogWarning("Could not determine AID or BVID for URL: {Url}", originalUrlToParse); return null; }

            var viewParams = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(bvid)) viewParams["bvid"] = bvid; else viewParams["aid"] = aid;
            JsonNode viewJson = await GetBiliApiJsonAsync($"{BiliApiBaseUrl}/x/web-interface/view", viewParams);
            if (viewJson?["code"]?.GetValue<int>() != 0 || viewJson?["data"] == null) { _logger.LogWarning("Failed to get video view info. Response: {Response}", viewJson?.ToString() ?? "null"); return null; }

            var data = viewJson["data"];
            videoInfo.Aid = data["aid"]?.GetValue<long>().ToString() ?? videoInfo.Aid; videoInfo.Bvid = data["bvid"]?.GetValue<string>() ?? videoInfo.Bvid;
            videoInfo.Title = data["title"]?.GetValue<string>() ?? videoInfo.Title; videoInfo.Description = data["desc"]?.GetValue<string>();
            videoInfo.OwnerName = data["owner"]?["name"]?.GetValue<string>(); videoInfo.OwnerMid = data["owner"]?["mid"]?.GetValue<long>() ?? 0;
            videoInfo.CoverUrl = data["pic"]?.GetValue<string>() ?? videoInfo.CoverUrl;
            if(data["pubdate"] is JsonValue pdNode && pdNode.TryGetValue<long>(out long pdVal)) videoInfo.PublishDateText = pdVal.ToString();
            videoInfo.TName = data["tname"]?.GetValue<string>(); videoInfo.TotalPages = data["videos"]?.GetValue<int>() ?? 1;

            var pagesArray = data["pages"]?.AsArray();
            if (pagesArray != null && pagesArray.Count > 0) {
                var currentPageData = pagesArray.FirstOrDefault(p => p?["page"]?.GetValue<int>() == videoInfo.Page) ?? pagesArray.FirstOrDefault();
                if (currentPageData != null) {
                    if (currentPageData["cid"] is JsonValue cNode && cNode.TryGetValue<long>(out long cVal)) videoInfo.Cid = cVal;
                    if (currentPageData["duration"] is JsonValue durNode && durNode.TryGetValue<int>(out int durVal)) videoInfo.Duration = durVal;
                    if (currentPageData["dimension"]?["width"] is JsonValue wNode && wNode.TryGetValue<int>(out int wVal)) videoInfo.DimensionWidth = wVal;
                    if (currentPageData["dimension"]?["height"] is JsonValue hNode && hNode.TryGetValue<int>(out int hVal)) videoInfo.DimensionHeight = hVal;
                }
            } else if (videoInfo.Cid == 0) {
                 if (data["cid"] is JsonValue cNode && cNode.TryGetValue<long>(out long cVal)) videoInfo.Cid = cVal;
                 if (data["duration"] is JsonValue durNode && durNode.TryGetValue<int>(out int durVal)) videoInfo.Duration = durVal;
            }
            
            videoInfo.FormattedContentInfo = $"{videoInfo.TName ?? "N/A"} - {data["dynamic"]?.GetValue<string>() ?? videoInfo.Description ?? "No description"}";
            videoInfo.MarkdownFormattedLink = $"[{MessageFormatHelper.EscapeMarkdownV2(videoInfo.Title ?? "Video")}]({videoInfo.OriginalUrl})";
            await GetPlayUrlInfoAsync(videoInfo);
            _logger.LogInformation("Successfully fetched video info (including play URLs) for: {VideoTitle}", videoInfo.Title);
            return videoInfo;
        } catch (Exception ex) { _logger.LogError(ex, "Error in GetVideoInfoAsync for URL: {VideoUrl}", videoUrl); return null; }
    }

    public async Task<BiliOpusInfo> GetOpusInfoAsync(string opusUrl)
    {
        _logger.LogInformation("Attempting to get opus info for URL: {OpusUrl}", opusUrl);
        if (string.IsNullOrWhiteSpace(opusUrl)) return null;
        var match = BiliHelper.BiliOpusUrlRegex.Match(opusUrl);
        if (!match.Success) { _logger.LogWarning("URL did not match Bilibili opus pattern: {OpusUrl}", opusUrl); return null; }
        string dynamicId = match.Groups[1].Value; if (string.IsNullOrWhiteSpace(dynamicId)) return null;
        try {
            JsonNode responseNode = await GetBiliApiJsonAsync($"{BiliApiBaseUrl}/x/polymer/web-dynamic/desktop/v1/detail", new Dictionary<string, string> { { "id", dynamicId } }, true);
            if (responseNode?["code"]?.GetValue<int>() != 0 || responseNode?["data"]?["item"] == null) { _logger.LogWarning("Failed to get opus detail. Response: {Response}", responseNode?.ToString() ?? "null"); return null; }
            var opusInfo = BiliHelper.ParseOpusItem(responseNode["data"]["item"], opusUrl, _logger);
            _logger.LogInformation("Successfully fetched opus info for dynamic ID: {DynamicId}", dynamicId); return opusInfo;
        } catch (Exception ex) { _logger.LogError(ex, "Error in GetOpusInfoAsync for URL: {OpusUrl}", opusUrl); return null; }
    }


    private async Task<JsonNode> GetBiliApiJsonAsync(string apiUrl, Dictionary<string, string> queryParams = null, bool useCookies = false)
    {
        var uriBuilder = new UriBuilder(apiUrl);
        if (queryParams != null && queryParams.Any()) uriBuilder.Query = string.Join("&", queryParams.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
        try {
            var request = new HttpRequestMessage(HttpMethod.Get, uriBuilder.Uri);
            if (useCookies) {
                string biliCookie = await _appConfigService.GetConfigurationValueAsync(AppConfigurationService.BiliCookieKey);
                if (!string.IsNullOrWhiteSpace(biliCookie)) request.Headers.Add("Cookie", biliCookie);
                else _logger.LogWarning("BiliCookie use requested for {ApiUrl}, but not configured.", uriBuilder.ToString());
            }
            var response = await _httpClient.SendAsync(request); response.EnsureSuccessStatusCode();
            return JsonNode.Parse(await response.Content.ReadAsStringAsync());
        } catch (Exception ex) { _logger.LogError(ex, "BiliAPI request failed for URL: {ApiUrl}", uriBuilder.ToString()); return null; }
    }

    private async Task GetPlayUrlInfoAsync(BiliVideoInfo videoInfo)
    {
        if (videoInfo.Cid == 0 || (string.IsNullOrWhiteSpace(videoInfo.Aid) && string.IsNullOrWhiteSpace(videoInfo.Bvid))) {
            _logger.LogWarning("Cannot fetch play URLs without Cid and (Aid or Bvid) for {VideoTitle}", videoInfo.Title); return;
        }
        string playUrlApi = $"{BiliApiBaseUrl}/x/player/playurl";
        var dashParams = new Dictionary<string, string> {
            ["cid"] = videoInfo.Cid.ToString(), ["qn"] = "0", ["fnver"] = "0", ["fnval"] = "4048", ["fourk"] = "1"
        };
        if (!string.IsNullOrWhiteSpace(videoInfo.Bvid)) dashParams["bvid"] = videoInfo.Bvid; else dashParams["avid"] = videoInfo.Aid;

        JsonNode dashResponse = await GetBiliApiJsonAsync(playUrlApi, dashParams, useCookies: true); 
        videoInfo.DashStreams = null; 

        if (dashResponse?["code"]?.GetValue<int>() == 0 && dashResponse["data"]?["dash"] != null) {
            var dashData = dashResponse["data"]["dash"]; var videoStreams = dashData["video"]?.AsArray(); var audioStreams = dashData["audio"]?.AsArray();
            if (videoStreams != null && audioStreams != null && videoStreams.Any() && audioStreams.Any()) {
                var bestVideo = videoStreams.OrderByDescending(v => v?["bandwidth"]?.GetValue<long>() ?? 0).FirstOrDefault();
                var bestAudio = audioStreams.OrderByDescending(a => a?["bandwidth"]?.GetValue<long>() ?? 0).FirstOrDefault();
                if (bestVideo != null && bestAudio != null) {
                    string videoBaseUrl = bestVideo["baseUrl"]?.GetValue<string>() ?? bestVideo["base_url"]?.GetValue<string>();
                    string audioBaseUrl = bestAudio["baseUrl"]?.GetValue<string>() ?? bestAudio["base_url"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(videoBaseUrl) && !string.IsNullOrWhiteSpace(audioBaseUrl)) {
                        long videoBw = bestVideo["bandwidth"]?.GetValue<long>() ?? 0; long audioBw = bestAudio["bandwidth"]?.GetValue<long>() ?? 0; long actualDurationSeconds = 0;
                        if (dashData["duration"] is JsonValue durNode && durNode.TryGetValue<long>(out actualDurationSeconds) && actualDurationSeconds > 0) {
                            _logger.LogInformation("Using duration from DASH response ({DashDuration}s) for {VideoTitle}.", actualDurationSeconds, videoInfo.Title);
                        } else if (videoInfo.Duration > 0) { actualDurationSeconds = videoInfo.Duration; _logger.LogInformation("Using duration from VideoInfo ({VideoInfoDuration}s) for {VideoTitle}.", actualDurationSeconds, videoInfo.Title);
                        } else { actualDurationSeconds = 300; _logger.LogWarning("No duration for {VideoTitle}, defaulting to {DefaultDuration}s.", videoInfo.Title, actualDurationSeconds); }
                        long estimatedTotalSizeBytes = (videoBw + audioBw) * actualDurationSeconds / 8;
                        _logger.LogInformation("Estimated DASH size for {VideoTitle}: {SizeMB:F2}MB ({SizeBytes} bytes), Duration: {DurationS}s", videoInfo.Title, estimatedTotalSizeBytes / (1024.0 * 1024.0), estimatedTotalSizeBytes, actualDurationSeconds);
                        videoInfo.DashStreams = new DashStreamInfo {
                            VideoStream = new DashMedia { Url = videoBaseUrl, Codec = bestVideo["codecs"]?.GetValue<string>(), SizeBytes = (videoBw * actualDurationSeconds / 8), QualityDescription = bestVideo["id"]?.GetValue<int>().ToString() },
                            AudioStream = new DashMedia { Url = audioBaseUrl, Codec = bestAudio["codecs"]?.GetValue<string>(), SizeBytes = (audioBw * actualDurationSeconds / 8), QualityDescription = bestAudio["id"]?.GetValue<int>().ToString() },
                            EstimatedTotalSizeBytes = estimatedTotalSizeBytes };
                        _logger.LogInformation("Processed DASH streams for {VideoTitle}", videoInfo.Title);
                    } else { _logger.LogWarning("Video or Audio DASH base URL is empty for {VideoTitle}.", videoInfo.Title); }
                } else { _logger.LogWarning("Could not find bestVideo or bestAudio DASH streams for {VideoTitle}", videoInfo.Title); }
            } else { _logger.LogWarning("DASH video/audio streams array null/empty for {VideoTitle}", videoInfo.Title); }
        } else { _logger.LogWarning("Failed to get DASH API response for {VideoTitle}. Response: {Response}", videoInfo.Title, dashResponse?.ToString() ?? "null"); }

        // Removed internal DASH size threshold check in BiliApiService.
        // The decision to download based on size is now solely handled by BiliMessageController using its configured maxFileSize.
        // long dashServiceThresholdBytes = 150 * 1024 * 1024; 
        // if (videoInfo.DashStreams != null && videoInfo.DashStreams.EstimatedTotalSizeBytes > dashServiceThresholdBytes) {
        //     _logger.LogWarning("Estimated DASH size for {VideoTitle} ({SizeMB:F2}MB) > threshold ({ThresholdMB:F2}MB). Clearing DASH.", videoInfo.Title, videoInfo.DashStreams.EstimatedTotalSizeBytes / (1024.0 * 1024.0), dashServiceThresholdBytes / (1024.0 * 1024.0));
        //     videoInfo.DashStreams = null;
        // }

        _logger.LogInformation("Attempting to fetch DURL streams for {VideoTitle}", videoInfo.Title);
        videoInfo.PlayUrls.Clear(); 
        int[] preferredQualities = { 80, 64, 32, 16 };
        foreach (var qn in preferredQualities) {
            var durlParams = new Dictionary<string, string> { ["cid"] = videoInfo.Cid.ToString(), ["qn"] = qn.ToString(), ["fnver"] = "0", ["fnval"] = "16" };
            if (!string.IsNullOrWhiteSpace(videoInfo.Bvid)) durlParams["bvid"] = videoInfo.Bvid; else durlParams["avid"] = videoInfo.Aid;
            
            JsonNode durlResponse = await GetBiliApiJsonAsync(playUrlApi, durlParams, useCookies: true);
            _logger.LogDebug("DURL response for QN {QN_Log} for '{VideoTitle}': {DurlResponseJson}", qn, videoInfo.Title, durlResponse?.ToJsonString(new JsonSerializerOptions{WriteIndented=false}) ?? "null");

            var durlDataNode = durlResponse?["data"];
            if (durlResponse?["code"]?.GetValue<int>() == 0 && durlDataNode?["durl"]?.AsArray() is JsonArray durlArray && durlArray.Count > 0) {
                _logger.LogInformation("DURL array count for QN {QN_Log} for '{VideoTitle}': {DurlArrayCount}", qn, videoInfo.Title, durlArray.Count);
                foreach(var durlItem in durlArray) {
                    if (durlItem == null) continue;
                    long sizeBytes = 0; if (durlItem["size"] is JsonValue sizeNode) sizeNode.TryGetValue<long>(out sizeBytes);
                    int qualityInt = 0; if (durlDataNode["quality"] is JsonValue qualityNode) qualityNode.TryGetValue<int>(out qualityInt); // quality is in data node, not durlItem
                    videoInfo.PlayUrls.Add(new PlayUrlDetail {
                        QualityNumeric = qn, QualityDescription = qualityInt > 0 ? qualityInt.ToString() : qn.ToString(),
                        Url = durlItem["url"]?.GetValue<string>(), SizeBytes = sizeBytes,
                        BackupUrls = durlItem["backup_url"]?.AsArray().Select(b => b?.GetValue<string>()).Where(s => s != null).ToList() ?? new List<string>()
                    });
                }
                _logger.LogInformation("Successfully processed DURL streams (QN: {QN}) for {VideoTitle}", qn, videoInfo.Title);
            } else {
                _logger.LogWarning("Failed to get valid DURL data for QN {QN_Log} for '{VideoTitle}'. Code: {DurlCode}, HasDataNode: {HasData}, HasDurlNode: {HasDurl}, DurlArrayCount: {Count}",
                    qn, videoInfo.Title, durlResponse?["code"]?.GetValue<int>() ?? -1, durlDataNode != null, durlDataNode?["durl"] != null, durlDataNode?["durl"]?.AsArray()?.Count ?? 0);
            }
        }

        if (!videoInfo.PlayUrls.Any() && videoInfo.DashStreams == null) {
             _logger.LogWarning("Failed to get any playable streams (neither DASH nor DURL) for {VideoTitle}", videoInfo.Title);
        }
    }
}
