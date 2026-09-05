using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Threading;

namespace Netch.Utils;

public static class WebUtil
{
    public const string DefaultUserAgent =
        @"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/94.0.4606.61 Safari/537.36 Edg/94.0.992.31";

    private static readonly HttpClient DefaultClient = new(new HttpClientHandler
    {
        UseProxy = false
    });

    static WebUtil()
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
    }

    private static int DefaultGetTimeout => Global.Settings.RequestTimeout;

    public static HttpRequestMessage CreateRequest(string url, string? userAgent = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", string.IsNullOrWhiteSpace(userAgent) ? DefaultUserAgent : userAgent);
        req.Headers.TryAddWithoutValidation("Accept", "*/*");
        req.Headers.TryAddWithoutValidation("Accept-Charset", "utf-8");
        return req;
    }

    public static async Task<byte[]> DownloadBytesAsync(HttpRequestMessage req, IWebProxy? proxy = null, int? timeout = null)
    {
        using var client = GetClient(proxy, timeout);
        using var response = await client.SendAsync(req);
        return await response.Content.ReadAsByteArrayAsync();
    }

    public static async Task<(HttpStatusCode, string)> DownloadStringAsync(HttpRequestMessage req, IWebProxy? proxy = null, int? timeout = null, Encoding? encoding = null)
    {
        using var client = GetClient(proxy, timeout);
        using var response = await client.SendAsync(req);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var str = (encoding ?? Encoding.UTF8).GetString(bytes);
        return (response.StatusCode, str);
    }

    public static Task DownloadFileAsync(string address, string fileFullPath, IProgress<int>? progress = null)
    {
        return DownloadFileAsync(CreateRequest(address), fileFullPath, progress);
    }

    public static async Task DownloadFileAsync(HttpRequestMessage req, string fileFullPath, IProgress<int>? progress = null, IWebProxy? proxy = null, int? timeout = null)
    {
        using var client = GetClient(proxy, timeout);
        using var response = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        
        var total = response.Content.Headers.ContentLength ?? -1L;
        using var stream = await response.Content.ReadAsStreamAsync();
        
        await using var fileStream = new FileStream(fileFullPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);
        
        if (progress != null && total > 0)
        {
            var buffer = new byte[8192];
            int bytesRead;
            long totalRead = 0;
            var lastReport = DateTime.Now;
            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                totalRead += bytesRead;
                if ((DateTime.Now - lastReport).TotalMilliseconds >= 200)
                {
                    progress.Report((int)(totalRead * 100 / total));
                    lastReport = DateTime.Now;
                }
            }
        }
        else
        {
            await stream.CopyToAsync(fileStream);
        }
        progress?.Report(100);
    }

    private static HttpClient GetClient(IWebProxy? proxy, int? timeout)
    {
        if (proxy == null && (timeout == null || timeout == DefaultGetTimeout))
        {
            DefaultClient.Timeout = TimeSpan.FromMilliseconds(DefaultGetTimeout);
            return DefaultClient;
        }

        var handler = new HttpClientHandler();
        if (proxy != null)
        {
            handler.Proxy = proxy;
            handler.UseProxy = true;
        }
        
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMilliseconds(timeout ?? DefaultGetTimeout)
        };
        return client;
    }
}