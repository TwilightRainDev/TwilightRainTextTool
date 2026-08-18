using System.Net;
using System.Text;

namespace TextTool.Tests.Services;

public class UpdateClientTests
{
    [Theory]
    [InlineData("https://github.com/TwilightRainDev/TwilightRainTextTool/releases/download/v2.4.0/TextTool-CLI-2.4.0-win-x64.zip", true)]
    [InlineData("https://objects.githubusercontent.com/x/y.zip", true)]
    [InlineData("https://release-assets.githubusercontent.com/x/y.zip", true)]
    [InlineData("https://api.github.com/repos/x/y/releases/latest", true)]
    [InlineData("https://github.com:4444/a.zip", false)]                       // 非常规端口
    [InlineData("https://github.com:443/a.zip", true)]                         // 显式 443 放行
    [InlineData("http://github.com/a/b.zip", false)]                        // 非 https
    [InlineData("https://evil.com/a.zip", false)]                           // 任意主机
    [InlineData("https://github.com.attacker.com/a.zip", false)]            // 相似域名
    [InlineData("https://127.0.0.1/a.zip", false)]                          // 内网地址
    [InlineData("https://169.254.169.254/latest/meta-data/", false)]        // metadata 地址
    public void IsAllowedDownloadUrl_EnforcesHttpsAndHost(string url, bool expected)
    {
        Assert.Equal(expected, UpdateClient.IsAllowedDownloadUrl(new Uri(url)));
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
            };
            if (request.RequestUri!.AbsolutePath == "/redirect-allowed")
            {
                response.StatusCode = HttpStatusCode.Redirect;
                response.Headers.Location = new Uri("https://objects.githubusercontent.com/final.bin");
            }
            else if (request.RequestUri.AbsolutePath == "/redirect-evil")
            {
                response.StatusCode = HttpStatusCode.Redirect;
                response.Headers.Location = new Uri("http://evil.com/evil.bin");
            }
            else
            {
                response.Content = new ByteArrayContent(Encoding.UTF8.GetBytes("ok"));
            }
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task FetchBytesAsync_FollowsAllowedRedirect()
    {
        using var client = new HttpClient(new FakeHandler());
        byte[] bytes = await UpdateClient.FetchBytesAsync(client, "https://github.com/redirect-allowed");
        Assert.Equal("ok", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task FetchBytesAsync_RejectsRedirectToDisallowedHost()
    {
        using var client = new HttpClient(new FakeHandler());
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            UpdateClient.FetchBytesAsync(client, "https://github.com/redirect-evil"));
        Assert.Contains("不允许的下载地址", ex.Message);
    }

    [Fact]
    public async Task FetchBytesAsync_RejectsDisallowedInitialUrl()
    {
        using var client = new HttpClient(new FakeHandler());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            UpdateClient.FetchBytesAsync(client, "http://evil.com/x.bin"));
    }
}
