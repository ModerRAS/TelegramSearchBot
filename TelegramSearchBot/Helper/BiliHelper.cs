using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Helper;
using TelegramSearchBot.Model.Bilibili;

namespace TelegramSearchBot.Helper;

public static class BiliHelper {
    public static readonly Regex BiliUrlParseRegex = new(
        @"bilibili\.com/(?:video/(?:(?<bvid>BV[1-9A-HJ-NP-Za-km-z]{10})|av(?<aid>\d+))/?(?:[?&;]*p=(?<page>\d+))?|bangumi/play/(?:ep(?<epid>\d+)|ss(?<ssid>\d+))/?)|b23\.tv/(?<shortid>\w+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static readonly Regex BiliOpusUrlRegex = new(@"(?:https?://)?(?:t\.bilibili\.com/|space\.bilibili\.com/\d+/dynamic)/(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static async Task<string> ResolveShortUrlAsync(string shortUrl, ILogger logger = null) {
        try {
            string currentUrl = shortUrl.StartsWith("http") ? shortUrl : "https://" + shortUrl;
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var tempClient = new HttpClient(handler);
            tempClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (iPhone; CPU iPhone OS 13_2_3 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/13.0.3 Mobile/15E148 Safari/604.1");
            for (int i = 0; i < 5; i++) {
                var response = await tempClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, currentUrl), HttpCompletionOption.ResponseHeadersRead);
                if (response.Headers.Location != null) {
                    currentUrl = response.Headers.Location.AbsoluteUri;
                    if (!currentUrl.Contains("b23.tv/")) return currentUrl;
                } else return currentUrl;
            }
            logger?.LogWarning("Too many redirects resolving short URL: {ShortUrl}", shortUrl);
        } catch (Exception ex) {
            logger?.LogError(ex, "Error resolving short URL: {ShortUrl}", shortUrl);
        }
        return shortUrl;
    }

    public static BiliOpusInfo ParseOpusItem(JsonNode itemNode, string originalUrl, ILogger logger = null) {
        if (itemNode == null) return null;
        var opusInfo = new BiliOpusInfo { OriginalUrl = originalUrl };
        string idStr = itemNode["basic"]?["comment_id_str"]?.GetValue<string>() ?? itemNode["id_str"]?.GetValue<string>();
        if (!long.TryParse(idStr, out long dynId)) {
            logger?.LogWarning("Could not parse dynamic ID from itemNode.");
            return null;
        }
        opusInfo.DynamicId = dynId;
        var moduleAuthor = itemNode["modules"]?["module_author"];
        opusInfo.UserName = moduleAuthor?["name"]?.GetValue<string>();
        opusInfo.UserMid = moduleAuthor?["mid"]?.GetValue<long>() ?? 0;
        opusInfo.Timestamp = moduleAuthor?["pub_ts"]?.GetValue<long>() ?? ( moduleAuthor?["ptime"]?.GetValue<long>() ?? 0 );
        var moduleDescNode = itemNode["modules"]?["module_descriptor"]?["desc"] ?? itemNode["modules"]?["module_dynamic"]?["desc"];
        if (moduleDescNode?["rich_text_nodes"]?.AsArray() is JsonArray richTextNodes)
            opusInfo.ContentText = string.Join("", richTextNodes.Select(n => n?["text"]?.GetValue<string>() ?? ( n?["emoji"]?["text"]?.GetValue<string>() ?? "" )));
        else
            opusInfo.ContentText = moduleDescNode?["text"]?.GetValue<string>();
        var major = itemNode["modules"]?["module_dynamic"]?["major"];
        if (major != null) ParseOpusMajorContent(major, opusInfo, logger);
        if (!opusInfo.ImageUrls.Any()) {
            var addDrawItems = itemNode["modules"]?["module_dynamic"]?["additional"]?["reserve_attach_card"]?["reserve_draw"]?["item"]?["draw_item_list"]?.AsArray();
            if (addDrawItems != null)
                opusInfo.ImageUrls.AddRange(addDrawItems.Select(d => d?["src"]?.GetValue<string>()).Where(s => s != null));
            else {
                var dynDrawItems = itemNode["modules"]?["module_dynamic"]?["draw"]?["items"]?.AsArray();
                if (dynDrawItems != null)
                    opusInfo.ImageUrls.AddRange(dynDrawItems.Select(d => d?["src"]?.GetValue<string>()).Where(s => s != null));
            }
        }
        opusInfo.FormattedContentMarkdown = opusInfo.ContentText ?? "";
        if (opusInfo.ForwardedOpus != null)
            opusInfo.FormattedContentMarkdown += $"\n// @{opusInfo.ForwardedOpus.UserName}: {opusInfo.ForwardedOpus.ContentText ?? ""}";
        string linkText = opusInfo.OriginalResource?.Title ?? "动态链接";
        string linkUrl = opusInfo.OriginalResource?.Url ?? $"https://t.bilibili.com/{opusInfo.DynamicId}";
        opusInfo.MarkdownFormattedLink = $"[{MessageFormatHelper.EscapeMarkdownV2(linkText)}]({linkUrl})";
        if (opusInfo.OriginalResource != null)
            opusInfo.MarkdownFormattedLink += $"\n[原始动态](https://t.bilibili.com/{opusInfo.DynamicId})";
        return opusInfo;
    }

    public static void ParseOpusMajorContent(JsonNode majorNode, BiliOpusInfo currentOpus, ILogger logger = null) {
        string type = majorNode?["type"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(type)) return;
        string typeKey = type.Replace("MAJOR_TYPE_", "").ToLower();
        JsonNode contentNode = majorNode[typeKey];
        if (contentNode == null) return;
        switch (type) {
            case "MAJOR_TYPE_DRAW":
                var dItems = contentNode["items"]?.AsArray();
                if (dItems != null)
                    currentOpus.ImageUrls.AddRange(dItems.Select(d => d?["src"]?.GetValue<string>()).Where(s => s != null));
                break;
            case "MAJOR_TYPE_ARCHIVE":
            case "MAJOR_TYPE_PGC":
            case "MAJOR_TYPE_ARTICLE":
            case "MAJOR_TYPE_MUSIC":
            case "MAJOR_TYPE_COMMON":
            case "MAJOR_TYPE_LIVE_RCMD":
                currentOpus.OriginalResource = new OpusOriginalResourceInfo {
                    Type = typeKey,
                    Title = contentNode["title"]?.GetValue<string>() ?? contentNode["head_text"]?.GetValue<string>(),
                    Url = ( contentNode["jump_url"]?.GetValue<string>()?.StartsWith("//") == true ? "https:" : "" ) + contentNode["jump_url"]?.GetValue<string>(),
                    CoverUrl = contentNode["cover"]?.GetValue<string>() ?? contentNode["covers"]?.AsArray().FirstOrDefault()?.GetValue<string>(),
                    Aid = contentNode["aid"]?.GetValue<string>(),
                    Bvid = contentNode["bvid"]?.GetValue<string>(),
                    Description = contentNode["desc"]?.GetValue<string>() ?? contentNode["sub_title"]?.GetValue<string>() ?? contentNode["desc1"]?.GetValue<string>()
                };
                break;
            case "MAJOR_TYPE_OPUS":
                if (contentNode != null)
                    currentOpus.ForwardedOpus = ParseOpusItem(contentNode, contentNode["jump_url"]?.GetValue<string>(), logger);
                break;
            default:
                logger?.LogDebug("Unhandled major opus type: {MajorType}", type);
                break;
        }
    }
}
