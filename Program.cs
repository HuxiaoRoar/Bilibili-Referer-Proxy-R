using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

class BilibiliAudioProxy
{
    static HttpClient http = new HttpClient();

    static async Task Main(string[] args)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:32181/");
        listener.Start();
        Console.WriteLine("[INFO] 代理服务器已启动：http://localhost:32181/");

        while (true)
        {
            var context = await listener.GetContextAsync();
            string query = context.Request.Url.Query;
            string targetUrl = WebUtility.UrlDecode(query.TrimStart('?'));

            if (string.IsNullOrWhiteSpace(targetUrl))
            {
                Console.WriteLine("[WARN] 收到无效请求，没有 URL 参数");
                context.Response.StatusCode = 400;
                await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("Invalid request"));
                context.Response.Close();
                continue;
            }

            Console.WriteLine($"[INFO] 收到请求：{targetUrl}");

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, targetUrl);
                request.Headers.Referrer = new Uri("https://www.bilibili.com");
                request.Headers.UserAgent.ParseAdd("Mozilla/5.0");

                var result = await http.SendAsync(request);

                context.Response.StatusCode = (int)result.StatusCode;
                context.Response.ContentType = result.Content.Headers.ContentType?.ToString();
                await result.Content.CopyToAsync(context.Response.OutputStream);
                context.Response.Close();

                Console.WriteLine($"[INFO] 请求成功：{targetUrl} -> 状态码 {(int)result.StatusCode}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] 请求失败：{targetUrl}\n原因：{ex.Message}");
                byte[] error = Encoding.UTF8.GetBytes("Proxy error: " + ex.Message);
                context.Response.StatusCode = 500;
                await context.Response.OutputStream.WriteAsync(error);
                context.Response.Close();
            }
        }
    }
}
