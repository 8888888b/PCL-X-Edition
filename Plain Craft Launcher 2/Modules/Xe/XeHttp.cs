using System.Net.Http;
using PCL.Core.IO.Net;

namespace PCL;

/// <summary>
/// PCL XE 扩展功能使用的 HTTP 客户端。
/// 启动器的通用客户端有 100 秒默认超时，AI 流式/长思考请求容易超时，因此 AI 使用独立长超时客户端；
/// 其余请求尽量复用启动器已配置好代理与 DoH 的客户端。
/// </summary>
internal static class XeHttp
{
    private static readonly HttpClient Fallback = Create(TimeSpan.FromSeconds(60));
    private static readonly HttpClient AiClient = Create(TimeSpan.FromMinutes(5));

    private static HttpClient Create(TimeSpan timeout)
    {
        var client = new HttpClient(new HttpClientHandler { UseProxy = true }) { Timeout = timeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PCL-XE/1.0");
        return client;
    }

    /// <summary>普通第三方 API（Modrinth 等），优先复用启动器网络服务以继承代理设置。</summary>
    public static HttpClient General
    {
        get
        {
            try
            {
                return NetworkService.GetClient(NetworkService.ModrinthApi);
            }
            catch
            {
                // 生命周期尚未初始化时（例如单元测试 / 极早启动阶段）退回独立客户端
                return Fallback;
            }
        }
    }

    /// <summary>AI 对话专用客户端，长超时且不经过重试策略，避免重复扣费。</summary>
    public static HttpClient Ai => AiClient;
}
