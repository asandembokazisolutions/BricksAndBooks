using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace BricksAndBooks.Infrastructure;

/// <summary>
/// Server-driven native screen contract. MVC owns validation, Identity, authorization,
/// side effects and persistence. Android renders semantic components, never a WebView.
/// Normal browser routes are not intercepted.
/// </summary>
public sealed class NativeScreenMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly HashSet<string> Controllers = new(StringComparer.OrdinalIgnoreCase)
    { "Home", "Account", "Accommodation", "AccommodationApplication", "Admin", "Announcement", "Audit", "Chat", "Financial", "Maintenance", "Notification", "Payment", "PaymentAnomaly", "Reporting", "Review", "Search", "Support", "Unit", "User", "Vehicle" };
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase) { "script", "style", "noscript" };
    public NativeScreenMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api/native/screen", out var remaining)) { await _next(context); return; }
        var target = remaining.HasValue ? remaining.Value! : "/";
        var controller = target.Trim('/').Split('/').FirstOrDefault() ?? "Home";
        if (target == "/") controller = "Home";
        if (!Controllers.Contains(controller) || target.Contains("..") || target.Contains('\\') || target.StartsWith("//"))
        { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = "Unknown native screen route." }); return; }

        var originalPath = context.Request.Path;
        var originalBody = context.Response.Body;
        var nativeRoute = target + context.Request.QueryString;
        await using var buffer = new MemoryStream();
        context.Request.Path = target;
        context.Items["NativeScreen"] = true;
        context.Response.Body = buffer;
        try
        {
            await _next(context);
            buffer.Position = 0;
            object payload;
            var location = context.Response.Headers.Location.ToString();
            if (context.Response.StatusCode is >= 300 and < 400 && location.Length > 0)
            {
                if (Uri.TryCreate(location, UriKind.Absolute, out var redirectUri) && redirectUri.Authority.Equals(context.Request.Host.Value, StringComparison.OrdinalIgnoreCase)) location = redirectUri.PathAndQuery;
                payload = new { version = 1, redirect = location, route = nativeRoute };
                context.Response.Headers.Remove("Location");
                context.Response.StatusCode = 200;
            }
            else if (context.Response.Headers.ContentDisposition.ToString() is var disposition && disposition.Contains("attachment", StringComparison.OrdinalIgnoreCase))
            {
                var fileName = Regex.Match(disposition, "filename\\*?=(?:UTF-8''|\")?([^;\"]+)", RegexOptions.IgnoreCase).Groups[1].Value;
                payload = new { version = 1, route = target, download = new { name = Uri.UnescapeDataString(fileName), mime = context.Response.ContentType ?? "application/octet-stream", base64 = Convert.ToBase64String(buffer.ToArray()) } };
                context.Response.Headers.Remove("Content-Disposition");
            }
            else if ((context.Response.ContentType ?? "").Contains("text/html", StringComparison.OrdinalIgnoreCase))
            {
                var html = await new StreamReader(buffer, Encoding.UTF8).ReadToEndAsync();
                payload = Extract(html, nativeRoute, context.Response.StatusCode);
            }
            else if (buffer.Length > 0 && (context.Response.ContentType ?? "").Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                var document = await JsonDocument.ParseAsync(buffer);
                payload = new { version = 1, route = target, data = document.RootElement.Clone() };
            }
            else payload = new { version = 1, route = target, status = context.Response.StatusCode, error = context.Response.StatusCode >= 400 ? $"Request rejected ({context.Response.StatusCode})." : null };

            context.Response.Body = originalBody;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength = null;
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsJsonAsync(payload);
        }
        finally { context.Request.Path = originalPath; context.Response.Body = originalBody; }
    }

    private static object Extract(string html, string route, int status)
    {
        var doc = new HtmlDocument(); doc.LoadHtml(html);
        var title = Decode(doc.DocumentNode.SelectSingleNode("//title")?.InnerText ?? "Bricks & Books").Split(" | ")[0];
        var body = doc.DocumentNode.SelectSingleNode("//body");
        var main = doc.DocumentNode.SelectSingleNode("//*[contains(concat(' ', normalize-space(@class), ' '), ' page-content ')]") ?? body ?? doc.DocumentNode;
        var nav = doc.DocumentNode.SelectSingleNode("//nav[@class='side-nav']") ?? doc.DocumentNode.SelectSingleNode("//nav[@aria-label='Main navigation']");
        var footer = doc.DocumentNode.SelectSingleNode("//footer");
        var topbar = doc.DocumentNode.SelectSingleNode("//header[contains(@class,'topbar')]");
        foreach (Match chart in Regex.Matches(html, @"new Chart\((\w+),.*?labels:\s*(\[.*?\]).*?data:\s*(\[.*?\])", RegexOptions.Singleline))
        {
            var variable = chart.Groups[1].Value;
            var id = Regex.Match(html, @"const\s+" + variable + @"\s*=\s*document.getElementById\(['"" ]+([^'"" ]+)").Groups[1].Value;
            var canvas = doc.DocumentNode.SelectSingleNode($"//canvas[@id='{id}']");
            if (canvas != null)
            {
                canvas.SetAttributeValue("data-labels", chart.Groups[2].Value);
                canvas.SetAttributeValue("data-values", chart.Groups[3].Value);
            }
        }
        return new { version = 1, route, title, status, shell = body?.GetAttributeValue("class", "") ?? "", navigation = nav == null ? null : ConvertNode(nav), account = topbar == null ? null : ConvertNode(topbar), content = ConvertNode(main), footer = footer == null ? null : ConvertNode(footer) };
    }

    private static NativeNode? ConvertNode(HtmlNode node)
    {
        if (Excluded.Contains(node.Name) || node.NodeType == HtmlNodeType.Comment) return null;
        if (node.NodeType == HtmlNodeType.Text)
        {
            var text = Decode(node.InnerText);
            if (string.IsNullOrWhiteSpace(text)) return null;
            return new NativeNode { Tag = "text", Text = text };
        }
        var result = new NativeNode { Tag = node.Name.ToLowerInvariant(), Attributes = node.Attributes.ToDictionary(a => a.Name.ToLowerInvariant(), a => Decode(a.Value)) };
        if (node.Name is "input" or "select" or "textarea" or "button" or "a" or "label" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "p" or "option" or "th" or "td") result.Text = Decode(node.InnerText);
        result.Children = node.ChildNodes.Select(ConvertNode).Where(n => n != null).Cast<NativeNode>().ToList();
        return result;
    }
    private static string Decode(string text) => HtmlEntity.DeEntitize(text).Replace('\u00a0', ' ');
    private sealed class NativeNode
    {
        public string Tag { get; set; } = "";
        public string? Text { get; set; }
        public Dictionary<string, string> Attributes { get; set; } = new();
        public List<NativeNode> Children { get; set; } = new();
    }
}
