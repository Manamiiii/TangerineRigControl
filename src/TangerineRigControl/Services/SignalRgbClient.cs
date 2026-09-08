using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace TangerineRigControl.Services
{
    internal sealed class SignalRgbClient : IDisposable
    {
        private static readonly Uri Endpoint = new Uri("http://127.0.0.1:16038/api/v1/lighting/enabled");
        private readonly HttpClient _client;

        public SignalRgbClient() : this(CreateLocalHandler()) { }

        internal static HttpClientHandler CreateLocalHandler()
        {
            return new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false };
        }

        internal SignalRgbClient(HttpMessageHandler handler)
        {
            _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
        }

        public async Task SetEnabledAsync(bool enabled)
        {
            using (var request = new HttpRequestMessage(new HttpMethod("PATCH"), Endpoint))
            {
                request.Content = new StringContent("{\"enabled\":" + enabled.ToString().ToLowerInvariant() + "}", Encoding.UTF8, "application/json");
                HttpResponseMessage response;
                try
                {
                    response = await _client.SendAsync(request).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    throw new InvalidOperationException("SignalRGB 本地服务响应超时，请确认 SignalRGB 已启动。");
                }
                catch (HttpRequestException)
                {
                    throw new InvalidOperationException("无法连接 SignalRGB 本地服务，请先安装并启动 SignalRGB。");
                }

                using (response)
                {
                    if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        throw new InvalidOperationException("SignalRGB 本地控制接口返回 403；该功能通常需要 SignalRGB Pro。");
                    }
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException("SignalRGB 控制失败（HTTP " + (int)response.StatusCode + "）。");
                    }
                }
            }
        }

        public void Dispose() { _client.Dispose(); }
    }
}
