namespace TelegramSearchBot.Model.AI {
    /// <summary>
    /// ChannelWithModel 授权来源。Manual = 管理员手工添加，永不被刷新/目录结果软删；
    /// Discovered = 来自授权快照来源，仅真正快照刷新成功后处理。
    /// </summary>
    public enum AuthorizationSource {
        Manual = 0,
        Discovered = 1
    }
}
