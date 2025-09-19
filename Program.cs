using System;
using System.Diagnostics;
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
        string targetUrl = ""; // 将 targetUrl 提到外部，以便 catch 块可以访问
        try
        {
            string query = context.Request.Url.Query;
            targetUrl = WebUtility.UrlDecode(query.TrimStart('?'));

            if (string.IsNullOrWhiteSpace(targetUrl))
            {
                //    Trace.WriteLine("[WARN] 收到无效请求，没有 URL 参数");
                context.Response.StatusCode = 400;
                await WriteAndCloseAsync(context.Response, "Invalid request");
                return;
            }

            //Trace.WriteLine($"[INFO] 收到请求：{targetUrl}");

            var request = new HttpRequestMessage(HttpMethod.Get, targetUrl);
            request.Headers.Referrer = new Uri("https://www.bilibili.com");
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0");

            using (var result = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
            {
                context.Response.StatusCode = (int)result.StatusCode;
                context.Response.ContentType = result.Content.Headers.ContentType?.ToString();

                using (var responseStream = await result.Content.ReadAsStreamAsync())
                {
                    // --- 核心修改点：为数据复制和连接关闭操作增加独立的异常处理 ---
                    try
                    {
                        await responseStream.CopyToAsync(context.Response.OutputStream);
                        context.Response.OutputStream.Close(); // 正常关闭
                    }
                    catch (HttpListenerException ex) when (ex.ErrorCode == 1229)
                    {
                        // 捕获到“企图在不存在的网络连接上进行操作”这个特定错误
                        // Trace.WriteLine($"[WARN] 连接被对方强制关闭 (可能是 hdnts 验证失败)。URL: {targetUrl}");
                        // 连接已经断开，我们什么都不用做，直接放弃即可
                    }
                    catch (Exception ex)
                    {
                        // 捕获其他在流复制过程中可能发生的错误
                        //Trace.WriteLine($"[ERROR] 在转发数据流时发生错误。URL: {targetUrl}\n原因：{ex.Message}");
                        // 尝试中止响应，而不是关闭
                        context.Response.Abort();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // 这是外层的 catch，处理发送请求之前的错误（如DNS解析失败、超时等）
            //  Trace.WriteLine($"[ERROR] 请求失败：{targetUrl}\n原因：{ex.Message}");
            try
            {
                context.Response.StatusCode = 500;
                await WriteAndCloseAsync(context.Response, "Proxy error: " + ex.Message);
            }
            catch { } // 如果此时 response 也出错了，就忽略
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
