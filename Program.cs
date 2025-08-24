using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

class BilibiliAudioProxy
{
    // 限制最大并发数，防止内存溢出
    private static readonly int MaxConcurrentRequests = 20;
    private static readonly System.Threading.SemaphoreSlim Semaphore = new System.Threading.SemaphoreSlim(MaxConcurrentRequests);
    static readonly HttpClient http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true })
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    static async Task Main(string[] args)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:32181/");
        listener.Start();
        Console.WriteLine("[INFO] 代理服务器已启动：http://localhost:32181/");

        while (true)
        {
            var context = await listener.GetContextAsync();
            _ = Task.Run(() => HandleRequestAsync(context));
        }
    }

    private static async Task HandleRequestAsync(HttpListenerContext context)
    {
        await Semaphore.WaitAsync();
        try
        {
            string query = context.Request.Url.Query;
            string targetUrl = WebUtility.UrlDecode(query.TrimStart('?'));

            if (string.IsNullOrWhiteSpace(targetUrl))
            {
                Console.WriteLine("[WARN] 收到无效请求，没有 URL 参数");
                context.Response.StatusCode = 400;
                await WriteAndCloseAsync(context.Response, "Invalid request");
                return;
            }

            Console.WriteLine($"[INFO] 收到请求：{targetUrl}");

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, targetUrl);
                request.Headers.Referrer = new Uri("https://www.bilibili.com");
                request.Headers.UserAgent.ParseAdd("Mozilla/5.0");

                using (var result = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                {
                    context.Response.StatusCode = (int)result.StatusCode;
                    context.Response.ContentType = result.Content.Headers.ContentType?.ToString();
                    // 流式转发，防止大文件占用内存
                    using (var responseStream = await result.Content.ReadAsStreamAsync())
                    {
                        await responseStream.CopyToAsync(context.Response.OutputStream);
                    }
                    context.Response.Close();
                }
                Console.WriteLine($"[INFO] 请求成功：{targetUrl}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] 请求失败：{targetUrl}\n原因：{ex.Message}");
                context.Response.StatusCode = 500;
                await WriteAndCloseAsync(context.Response, "Proxy error: " + ex.Message);
            }
        }
        finally
        {
            Semaphore.Release();
        }
    }

    private static async Task WriteAndCloseAsync(HttpListenerResponse response, string message)
    {
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            await response.OutputStream.WriteAsync(data, 0, data.Length);
        }
        catch { }
        finally
        {
            response.Close();
        }
    }
}
