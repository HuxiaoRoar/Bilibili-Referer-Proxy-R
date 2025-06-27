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
        Console.WriteLine("代理服务器已启动：http://localhost:32181/");

        while (true)
        {
            var context = await listener.GetContextAsync();
            string query = context.Request.Url.Query;
            string targetUrl = WebUtility.UrlDecode(query.TrimStart('?'));

            if (string.IsNullOrWhiteSpace(targetUrl))
            {
                context.Response.StatusCode = 400;
                await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("Invalid request"));
                context.Response.Close();
                continue;
            }

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
            }
            catch (Exception ex)
            {
                byte[] error = Encoding.UTF8.GetBytes("Proxy error: " + ex.Message);
                context.Response.StatusCode = 500;
                await context.Response.OutputStream.WriteAsync(error);
                context.Response.Close();
            }
        }
    }
}
