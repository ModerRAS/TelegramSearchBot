using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service.AI.LLM {
    /// <summary>
    /// 运行时解析出的确定路由：channel（品牌/共享 secret）+ binding（endpoint/协议/认证）+ 模型行。
    /// Binding 为 null 表示 legacy 临时路由（channel.Provider/Gateway 回退）。
    /// General 路径与 Agent 路径共用。
    /// </summary>
    public sealed record ResolvedLlmRoute(LLMChannel Channel, LLMApiBinding? Binding, ChannelWithModel Model) {
        /// <summary>是否为 legacy 临时路由（无 binding，走 channel.Provider/Gateway）。</summary>
        public bool IsLegacyFallback => Binding == null;
    }

    /// <summary>
    /// 确定性路由解析：General 路径与 Agent 路径共用的唯一选择逻辑，避免协议分叉。
    /// 规则：渠道 Priority DESC 由调用方保证；渠道内先取唯一 IsPreferred=true 行，
    /// 否则取有效默认 binding 行（ApiBindingId==null 解释为渠道默认 binding），
    /// 最终以 (binding.Id, 行 Id) 稳定排序；数据异常仅告警，绝不 throw。
    /// 协议永远不按模型名/厂商/Gateway 字符串猜测（见 blueprint §六.7）。
    /// </summary>
    public static class LlmRouteResolver {
        /// <summary>
        /// 在单个渠道内为 modelName 解析路由。rows 必须是该渠道 + 该模型 + !IsDeleted 的已加载行，
        /// 且 ApiBindingId!=null 的行需已 Include(ApiBinding)，channel.Bindings 需已加载。
        /// </summary>
        public static ResolvedLlmRoute? Resolve(LLMChannel channel, string modelName, IReadOnlyList<ChannelWithModel> rows, ILogger logger) {
            if (channel == null) return null;
            if (rows == null || rows.Count == 0) {
                Log(logger, () => $"模型 {modelName} 在渠道 {channel.Id} 无可用配置行");
                return null;
            }

            // 渠道级默认 binding（异常：多个 IsDefault → 稳定排序 + 告警，不 throw）
            var defaults = (channel.Bindings ?? Enumerable.Empty<LLMApiBinding>())
                .Where(b => b.IsDefault)
                .OrderBy(b => b.Id)
                .ToList();
            if (defaults.Count > 1) {
                Log(logger, () => $"渠道 {channel.Id} 存在多个 IsDefault binding（{defaults.Count} 个），按 binding.Id 稳定排序，请管理员修复");
            }
            var defaultBinding = defaults.FirstOrDefault();

            List<ChannelWithModel> selected;
            var preferred = rows.Where(r => r.IsPreferred).ToList();
            if (preferred.Count > 0) {
                if (preferred.Count > 1) {
                    Log(logger, () => $"模型 {modelName} 渠道 {channel.Id} 存在多个 IsPreferred 行（{preferred.Count} 个），按 binding.Id 稳定排序，请管理员修复");
                }
                selected = preferred;
            } else {
                // ApiBindingId==null 的 legacy 行解释为渠道默认 binding；无默认 binding 时走 legacy 回退
                var effectiveDefaults = rows
                    .Where(r => r.ApiBindingId == null ? defaultBinding != null : (r.ApiBinding?.IsDefault ?? false))
                    .ToList();
                if (effectiveDefaults.Count > 0) {
                    var distinctBindingIds = effectiveDefaults.Select(r => r.ApiBindingId ?? defaultBinding!.Id).Distinct().ToList();
                    if (distinctBindingIds.Count > 1) {
                        Log(logger, () => $"模型 {modelName} 渠道 {channel.Id} 存在多个默认 binding 候选（{distinctBindingIds.Count} 个），按 binding.Id 稳定排序，请管理员修复");
                    }
                    selected = effectiveDefaults;
                } else {
                    Log(logger, () => $"模型 {modelName} 渠道 {channel.Id} 无默认 binding（legacy 行），临时回退 channel.Provider/Gateway，请管理员补建默认 binding");
                    selected = rows.ToList();
                }
            }

            var pick = selected
                .OrderBy(r => r.ApiBindingId ?? defaultBinding?.Id ?? int.MaxValue)
                .ThenBy(r => r.Id)
                .First();

            var binding = pick.ApiBindingId != null ? pick.ApiBinding : defaultBinding;
            if (pick.ApiBindingId != null && pick.ApiBinding == null) {
                // 调用方未加载 ApiBinding 导航：防御性回退渠道默认，不 throw
                Log(logger, () => $"模型 {modelName} 渠道 {channel.Id} 行 {pick.Id} 的 ApiBinding 未加载，回退渠道默认 binding");
                binding = defaultBinding;
            }
            return new ResolvedLlmRoute(channel, binding, pick);
        }

        /// <summary>
        /// 按渠道 Priority DESC 顺序传入候选（每渠道的模型行），返回第一个可解析路由。
        /// 供 Agent 路径使用；选择核心仍为 <see cref="Resolve"/>。
        /// </summary>
        public static ResolvedLlmRoute? ResolveFirst(IEnumerable<(LLMChannel Channel, List<ChannelWithModel> Rows)> candidates, string modelName, ILogger logger) {
            foreach (var (channel, rows) in candidates) {
                var route = Resolve(channel, modelName, rows, logger);
                if (route != null) return route;
            }
            return null;
        }

        private static void Log(ILogger logger, Func<string> message) {
            if (logger != null) logger.LogWarning(message());
        }
    }

    /// <summary>
    /// binding → 客户端构造参数的最小共享映射：endpoint 取 binding（缺省回退 channel.Gateway）；
    /// ApiKey 始终共享自 channel（blueprint §六.1）；AuthProfile=None 走无 key 路径（空 key，本地/无鉴权端点）。
    /// 认证传输由各 SDK 原生实现：OpenAI SDK 发 Authorization: Bearer，Anthropic SDK 发 x-api-key。
    /// </summary>
    public static class LlmBindingSupport {
        public static string ResolveEndpoint(LLMChannel channel, LLMApiBinding? binding)
            => !string.IsNullOrWhiteSpace(binding?.Endpoint) ? binding!.Endpoint : channel?.Gateway ?? string.Empty;

        public static string ResolveApiKey(LLMChannel channel, LLMApiBinding? binding)
            => binding?.AuthProfile == LlmAuthProfile.None ? string.Empty : channel?.ApiKey ?? string.Empty;
    }
}
