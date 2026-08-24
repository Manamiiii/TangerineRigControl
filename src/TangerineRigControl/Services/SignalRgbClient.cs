using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace TangerineRigControl.Services
{
    internal sealed class SignalRgbClient
    {
        private static readonly Uri Endpoint = new Uri("http://127.0.0.1:16038/api/v1/lighting/enabled");
        private readonly HttpClient _client;

        public SignalRgbClient()
        {
            // Never send local control traffic through a configured system proxy.
            _client = new HttpClient(new HttpClientHandler { UseProxy = false })
            {
                Timeout = TimeSpan.FromSeconds(3)
            };
        }

        public async Task SetEnabledAsync(bool enabled)
        {
            var request = new HttpRequestMessage(new HttpMethod("PATCH"), Endpoint);
            request.Content = new StringContent("{\"enabled\":" + enabled.ToString().ToLowerInvariant() + "}", Encoding.UTF8, "application/json");
            using (var response = await _client.SendAsync(request).ConfigureAwait(false))
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
}
