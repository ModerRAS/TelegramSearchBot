using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using TelegramSearchBot.Model.Tools;

namespace TelegramSearchBot.Helper {
    /// <summary>
    /// 包装 IAsyncEnumerable&lt;string&gt; 流，检测并剥离迭代限制标记。
    /// 当 LLM 服务达到 MaxToolCycles 上限后，会在最后一次 yield 的累积内容末尾
    /// 追加 IterationLimitReachedPayload.Marker。此包装器：
    /// 1. 将标记从内容中剥离，确保消息服务只看到正常文本
    /// 2. 设置 IterationLimitReached = true，供 Controller 检查
    /// </summary>
    public class IterationLimitAwareStream {
        /// <summary>
        /// 流枚举结束后为 true 表示达到了迭代上限
        /// </summary>
        public bool IterationLimitReached { get; private set; }

        /// <summary>
        /// 包装原始流，透传所有正常内容，检测并剥离末尾的迭代限制标记
        /// </summary>
        public async IAsyncEnumerable<string> WrapAsync(
            IAsyncEnumerable<string> source,
            [EnumeratorCancellation] CancellationToken cancellationToken = default) {

            await foreach (var item in source.WithCancellation(cancellationToken)) {
                if (IterationLimitReachedPayload.IsIterationLimitMessage(item)) {
                    // 检测到标记 → 剥离标记后 yield 干净的内容，设置标志
                    IterationLimitReached = true;
                    var cleanContent = IterationLimitReachedPayload.StripMarker(item);
                    if (!string.IsNullOrEmpty(cleanContent)) {
                        yield return cleanContent;
                    }
                    // 标记只会出现在最后一条，之后流自然结束
                } else {
                    yield return item;
                }
            }
        }
    }
}
