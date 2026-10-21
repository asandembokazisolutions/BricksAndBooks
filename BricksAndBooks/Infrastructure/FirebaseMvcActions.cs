using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using BricksAndBooks.Models;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace BricksAndBooks.Infrastructure;

/// <summary>Trusted fixed-route adapter; no arbitrary URL, controller, role or UID from clients.</summary>
public sealed class FirebaseMvcActions
{
    private readonly NativeFirebaseQueries _queries;
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly IOptionsMonitor<CookieAuthenticationOptions> _cookieOptions;
    private readonly IHttpContextAccessor _http;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _environment;
    public FirebaseMvcActions(NativeFirebaseQueries queries, SignInManager<ApplicationUser> signIn,
        IOptionsMonitor<CookieAuthenticationOptions> cookieOptions, IHttpContextAccessor http, IConfiguration config, IWebHostEnvironment environment)
    { _queries = queries; _signIn = signIn; _cookieOptions = cookieOptions; _http = http; _config = config; _environment = environment; }

    public async Task<FirebaseActionResult> ExecuteAsync(string action, Dictionary<string, JsonElement> fields, ApplicationUser user, CancellationToken cancellationToken)
    {
        try
        {
            var read = await _queries.ExecuteAsync(action, fields, user);
            if (read != null)
            {
                var serialized = JsonSerializer.SerializeToElement(read, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                return new FirebaseActionResult(true, 200, "", Get(serialized, "item"), Get(serialized, "items"), Get(serialized, "summary"));
            }
            if (action == "cardRegister") return await _http.HttpContext!.RequestServices.GetRequiredService<FirebaseCardService>().RegisterAsync(fields,user,_http.HttpContext.User);
            var data = Flatten(fields);
            string Text(string key) => NativeFirebaseQueries.Text(fields, key);
            var edit = !string.IsNullOrWhiteSpace(Text("id"));
            var path = action switch
            {
                "profileUpdate" => "/Account/Manage",
                "changePassword" => "/Account/ChangePassword",
                "residenceSave" => edit ? "/Accommodation/Edit" : "/Accommodation/Create",
                "residenceDelete" => "/Accommodation/Delete",
                "unitSave" => edit ? "/Unit/Edit" : "/Unit/Create",
                "unitDelete" => "/Unit/Delete",
                "applicationCreate" => "/AccommodationApplication/Apply",
                "applicationReview" => Text("status") switch { "Approved" => "/AccommodationApplication/Approve", "Denied" => "/AccommodationApplication/Deny", _ => throw new ArgumentException("The source application supports approval or denial of pending applications.") },
                "applicationTerminationRequest" => "/AccommodationApplication/RequestTermination",
                "applicationTerminate" => "/AccommodationApplication/Terminate",
                "applicationApproveTermination" => "/AccommodationApplication/ApproveTermination",
                "applicationDenyTermination" => "/AccommodationApplication/DenyTermination",
                "paymentSubmit" => "/Payment/Process",
                "paymentVerify" => "/Payment/Verify",
                "cardDelete" => "/Payment/RemoveSavedCard",
                "calculateBills" => "/Admin/CalculateBills",
                "tariffSave" => edit ? "/Admin/TariffConfigEdit" : "/Admin/TariffConfigCreate",
                "tariffDelete" => "/Admin/TariffConfigDelete",
                "maintenanceCreate" => "/Maintenance/Create",
                "maintenanceUpdate" => "/Maintenance/Edit",
                "maintenanceDelete" => "/Maintenance/Delete",
                "vehicleSave" => edit ? "/Vehicle/Edit" : "/Vehicle/Create",
                "vehicleDelete" => "/Vehicle/Delete",
                "chatSend" => "/Chat/Send",
                "chatRead" => "@chatRead",
                "notificationRead" => "/Notification/MarkAsRead",
                "notificationsReadAll" => "/Notification/MarkAllAsRead",
                "reviewCreate" => "/Review/Create",
                "paymentAnomalyResolve" => "/PaymentAnomaly/Resolve",
                "userSave" => edit ? "/User/Edit" : "/User/Create",
                "userDelete" => "/User/Delete",
                "userResetPassword" => "/User/ResetPassword",
                "resolvePasswordReset" => "/Account/ResolvePasswordResetRequest",
                "announcementSave" when !edit => "/Announcement/Create",
                _ => throw new ArgumentException("This operation is not supported by the original web application: " + action)
            };
            if (action == "profileUpdate")
            {
                data["email"] = user.Email ?? "";
                data["isStudent"] = user.IsStudent.ToString(); data["isStaff"] = user.IsStaff.ToString();
                data["isAdmin"] = user.IsAdmin.ToString(); data["isFinancialOfficer"] = user.IsFinancialOfficer.ToString();
            }
            if (action == "changePassword") { data["oldPassword"] = Text("currentPassword"); data["confirmPassword"] = Text("newPassword"); }
            if (action == "applicationReview") data["assignedUnitId"] = Text("unitId");
            if (action == "paymentSubmit")
            {
                data["cardCvv"] = Text("cardCvc"); data["saveCardForFuture"] = Text("saveCard");
                if (fields.TryGetValue("items", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    int index = 0;
                    foreach (var item in items.EnumerateArray())
                    {
                        foreach (var key in new[] { "billId", "chargeType", "quantity" }) if (item.TryGetProperty(key, out var value)) data[$"Items[{index}].{key}"] = value.ToString();
                        data[$"Items[{index}].IsSelected"] = "true"; index++;
                    }
                }
            }
            if (action == "userSave")
            {
                if (edit && string.IsNullOrWhiteSpace(Text("userName")))
                {
                    var db = _http.HttpContext!.RequestServices.GetRequiredService<BricksAndBooks.Data.DataAccess.AppDbContext>();
                    var existing = await db.Users.FindAsync(Text("id"));
                    data["userName"] = existing?.UserName ?? Text("email");
                    data.TryAdd("studentNumber", existing?.StudentNumber ?? ""); data.TryAdd("employeeNumber", existing?.EmployeeNumber ?? "");
                }
            }
            if (action == "announcementSave") { data["isPublished"] = Text("isActive"); data.TryAdd("audience", "Everyone"); }
            using var session = await CreateInternalSession(user, cancellationToken);
            if (path == "@chatRead")
            {
                using var response = await session.Client.GetAsync("/Chat/Index?contactId=" + Uri.EscapeDataString(Text("contactId")), cancellationToken);
                return response.IsSuccessStatusCode ? new(true, 200, "Conversation marked as read.") : new(false, (int)response.StatusCode, "Unable to open conversation.");
            }
            using var form = new MultipartFormDataContent();
            data["__RequestVerificationToken"] = session.Antiforgery;
            foreach (var pair in data) form.Add(new StringContent(pair.Value ?? ""), pair.Key);
            var fileKey = action == "applicationCreate" ? "documentUrls" : action.StartsWith("maintenance") ? "attachmentUrls" : action == "paymentSubmit" ? "proofOfPaymentUrl" : action == "residenceSave" ? "imageUrls" : null;
            var fileField = action == "applicationCreate" ? "documents" : action.StartsWith("maintenance") ? "attachments" : action == "paymentSubmit" ? "proofOfPayment" : "images";
            if (fileKey != null) AddOwnedFiles(form, fileField, Text(fileKey), user.Id);
            using var posted = await session.Client.PostAsync(path, form, cancellationToken);
            var body = await posted.Content.ReadAsStringAsync(cancellationToken);
            if (posted.StatusCode is HttpStatusCode.Found or HttpStatusCode.SeeOther)
            {
                var location = posted.Headers.Location?.ToString() ?? "";
                if (location.StartsWith("/Account/Login") || location.StartsWith("/Account/AccessDenied")) return new(false, 403, "You do not have permission for this operation.");
                if (location.StartsWith('/') && !location.StartsWith("//") && !location.Contains("\\"))
                {
                    using var final = await session.Client.GetAsync(location, cancellationToken);
                    body = await final.Content.ReadAsStringAsync(cancellationToken);
                    var errors = Errors(body);
                    if (errors.Count > 0) return new(false, 400, string.Join(" ", errors));
                }
                return new(true, 200, "The source workflow completed.");
            }
            if (!posted.IsSuccessStatusCode) return new(false, (int)posted.StatusCode, posted.StatusCode == HttpStatusCode.Forbidden ? "You do not have permission for this operation." : "The source workflow rejected this request.");
            if (posted.Content.Headers.ContentType?.MediaType == "application/json")
            {
                using var parsed = JsonDocument.Parse(body);
                bool success = !parsed.RootElement.TryGetProperty("success", out var flag) || flag.GetBoolean();
                return new(success, success ? 200 : 400, Get(parsed.RootElement, "message")?.ToString() ?? "", Get(parsed.RootElement,"item"));
            }
            var validationErrors = Errors(body);
            if (validationErrors.Count > 0) return new(false, 400, string.Join(" ", validationErrors));
            var doc = new HtmlDocument();doc.LoadHtml(body);
            if (action == "calculateBills" && doc.DocumentNode.SelectSingleNode("//*[contains(@class,'alert-success')]") != null) return new(true, 200, "Bills calculated.");
            return new(false, 400, "The source workflow did not confirm this operation. Review all required fields.");
        }
        catch (UnauthorizedAccessException exception) { return new(false, 403, exception.Message); }
        catch (KeyNotFoundException exception) { return new(false, 404, exception.Message); }
        catch (ArgumentException exception) { return new(false, 400, exception.Message); }
    }

    public async Task<InternalSession> CreateInternalSession(ApplicationUser user, CancellationToken cancellationToken)
    {
        var context = _http.HttpContext!;
        var port = context.Connection.LocalPort;
        if (port <= 0) throw new InvalidOperationException("The backend listener port is unavailable.");
        // Never use client-controlled Host/URL for privileged loopback traffic.
        var origin = new Uri("http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture));
        var options = _cookieOptions.Get(IdentityConstants.ApplicationScheme);
        var principal = await _signIn.CreateUserPrincipalAsync(user);
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties { IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(2) }, IdentityConstants.ApplicationScheme);
        var cookies = new CookieContainer(); cookies.Add(origin, new Cookie(options.Cookie.Name!, options.TicketDataFormat.Protect(ticket), "/"));
        var handler = new HttpClientHandler { CookieContainer = cookies, AllowAutoRedirect = false, UseProxy = false };
        var client = new HttpClient(handler) { BaseAddress = origin, Timeout = TimeSpan.FromSeconds(90) };
        // This server-controlled loopback call bypasses the external TLS proxy. Preserve the
        // original public HTTPS scheme so UseHttpsRedirection does not redirect the antiforgery
        // bootstrap away from this internal client.
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        using var bootstrap = await client.GetAsync("/Account/Manage", cancellationToken);
        var html = await bootstrap.Content.ReadAsStringAsync(cancellationToken);
        var doc = new HtmlDocument();doc.LoadHtml(html);
        var antiforgery = doc.DocumentNode.SelectSingleNode("//input[@name='__RequestVerificationToken']")?.GetAttributeValue("value", "");
        if (string.IsNullOrWhiteSpace(antiforgery)) { client.Dispose(); throw new UnauthorizedAccessException("Unable to establish the protected source session."); }
        return new InternalSession(client, HtmlEntity.DeEntitize(antiforgery));
    }
    private void AddOwnedFiles(MultipartFormDataContent form, string key, string urls, string uid)
    {
        foreach (var raw in urls.Split(new[] { '|', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var path = Uri.TryCreate(raw, UriKind.Absolute, out var uri) ? uri.AbsolutePath : raw;
            var prefix = "/uploads/firebase/" + uid + "/";
            if (!path.StartsWith(prefix, StringComparison.Ordinal) || path.Contains("..") || path.Contains('\\')) throw new ArgumentException("Attachments must be uploaded by the signed-in account.");
            var fileName = path[prefix.Length..];
            if (fileName.Contains('/')) throw new ArgumentException("Invalid attachment path.");
            var file = Path.Combine(_environment.WebRootPath, "uploads", "firebase", uid, fileName);
            if (!File.Exists(file)) throw new ArgumentException("An uploaded attachment is no longer available.");
            var content = new StreamContent(File.OpenRead(file));
            content.Headers.ContentType = new MediaTypeHeaderValue(Path.GetExtension(file).ToLowerInvariant() switch { ".pdf" => "application/pdf", ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".webp" => "image/webp", _ => "application/octet-stream" });
            form.Add(content, key, fileName);
        }
    }
    private static Dictionary<string, string> Flatten(Dictionary<string, JsonElement> fields) => fields.Where(kv => kv.Value.ValueKind is not JsonValueKind.Object and not JsonValueKind.Array).ToDictionary(kv => kv.Key, kv => kv.Value.ValueKind == JsonValueKind.Null ? "" : kv.Value.ToString(), StringComparer.OrdinalIgnoreCase);
    private static object? Get(JsonElement element, string key) => element.TryGetProperty(key, out var value) ? value.Clone() : null;
    private static List<string> Errors(string html)
    {
        var doc = new HtmlDocument();doc.LoadHtml(html);
        var nodes = doc.DocumentNode.SelectNodes("//*[contains(@class,'validation-summary-errors') or contains(@class,'field-validation-error') or contains(@class,'alert-danger')]");
        return nodes?.Select(n => HtmlEntity.DeEntitize(n.InnerText).Trim()).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList() ?? new();
    }
    public sealed record InternalSession(HttpClient Client, string Antiforgery) : IDisposable { public void Dispose() => Client.Dispose(); }
}
public sealed record FirebaseActionResult(bool Success, int StatusCode, string Message, object? Item = null, object? Items = null, object? Summary = null);
