namespace SETUNA.Main
{
    public enum HotKeyID
    {
        Capture = 0,
        Function1,

        __Count__ = 2,
    }

    /// <summary>
    /// 单个热键的注册结果。区分「未启用」和「注册失败」很重要：
    /// 以前两者都返回 true/false 混在一起，调用方只能靠猜，
    /// 结果一个热键冲突会连带关掉用户的全局热键设置。
    /// </summary>
    public enum HotKeyRegistrationResult
    {
        /// <summary>注册成功。</summary>
        Registered,

        /// <summary>用户没有为该热键配置按键，或全局热键被用户关闭——不算失败。</summary>
        NotEnabled,

        /// <summary>已配置但注册失败，通常是被其他程序占用。</summary>
        Failed,
    }
}
