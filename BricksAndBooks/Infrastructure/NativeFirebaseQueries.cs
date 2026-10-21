using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using BricksAndBooks.Controllers;
using BricksAndBooks.Data.DataAccess;
using BricksAndBooks.Models;
using BricksAndBooks.Models.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;

namespace BricksAndBooks.Infrastructure;

/// <summary>Read-only DTOs reuse the source MVC calculations. They are not screen descriptors.</summary>
public sealed class NativeFirebaseQueries
{
    private readonly IServiceProvider _services;
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IHttpContextAccessor _http;
    public NativeFirebaseQueries(IServiceProvider services, AppDbContext db, UserManager<ApplicationUser> users, IHttpContextAccessor http)
    { _services = services; _db = db; _users = users; _http = http; }

    public async Task<object?> ExecuteAsync(string action, IReadOnlyDictionary<string, JsonElement> fields, ApplicationUser user)
    {
        var principal = _http.HttpContext!.User;
        var admin = principal.IsInRole("Admin");
        var finance = admin || principal.IsInRole("FinancialOfficer");
        void Need(bool allowed) { if (!allowed) throw new UnauthorizedAccessException("You do not have permission for this operation."); }
        int Id() => int.TryParse(Text(fields, "id"), out var id) ? id : 0;
        object Items(object? value) => new { success = true, items = Safe(value) };
        switch (action)
        {
            case "myBills":
                return Items(await Model<PaymentController>("MyBills"));
            case "myPayments":
                return Items(await Model<PaymentController>("Index"));
            case "paymentOptions":
                Need(principal.IsInRole("Student") || principal.IsInRole("Staff"));
                var options = (FlexiblePaymentViewModel?)await Model<PaymentController>("Process");
                return Items(options?.Items);
            case "paymentGet":
                var details = await Result<PaymentController>("Details", Id());
                if (details is not ViewResult paymentView) throw new KeyNotFoundException("Payment not found or access denied.");
                var payment = paymentView.Model as Payment;
                return new { success = true, item = Safe(payment), items = Safe(payment?.Allocations) };
            case "cardsList":
                return Items(await _db.SavedPaymentCards.Where(card => card.UserId == user.Id).ToListAsync());
            case "notificationsList":
                return Items(await Model<NotificationController>("Index"));
            case "applicationGet":
                var applicationId = Id();
                var application = await _db.AccommodationApplications.Include(x => x.Accommodation).Include(x => x.AssignedUnit).FirstOrDefaultAsync(x => x.Id == applicationId);
                Need(application != null && (admin || application.UserId == user.Id));
                var dto = (Dictionary<string, object?>)Safe(application)!;
                dto["accommodationName"] = application!.Accommodation?.Name;
                dto["unitNumber"] = application.AssignedUnit?.UnitNumber;
                return new { success = true, item = dto };
            case "maintenanceWorkers":
                Need(admin || principal.IsInRole("FinancialOfficer") || principal.IsInRole("MaintenanceWorker"));
                return Items((await _users.GetUsersInRoleAsync("MaintenanceWorker")).Select(PublicUser));
            case "chatContacts":
            case "chatThread":
                var chat = await Model<ChatController>("Index", Text(fields, "contactId"));
                var property = chat?.GetType().GetProperty(action == "chatContacts" ? "Contacts" : "Messages");
                return Items(property?.GetValue(chat));
            case "usersList":
                Need(finance);
                return Items((await _users.Users.ToListAsync()).Select(PublicUser));
            case "auditList":
                Need(finance);
                return Items(await _db.AuditLogs.OrderByDescending(x => x.Timestamp).Take(250).ToListAsync());
            case "adminSummary":
                Need(admin);
                return new { success = true, summary = Safe(await Model<AdminController>("Index")) };
            case "financeSummary":
                Need(finance);
                return new { success = true, summary = new { totalRevenue = _db.Payments.Where(p => p.Status == PaymentStatus.Successful).Sum(p => p.Amount), outstandingBalance = _db.Bills.AsEnumerable().Where(b => b.Status == BillStatus.Pending).Sum(b => b.TotalAmount), pendingPayments = _db.Payments.Count(p => p.Status == PaymentStatus.Pending) }, items = Safe(await _db.Payments.OrderByDescending(p => p.PaymentDate).ToListAsync()) };
            case "residentSummary":
                var home = await Model<HomeController>("Index") as CustomerDashboardViewModel;
                if (home == null) return new { success = true, summary = new { outstandingBalance = 0m, residenceName = "Not allocated", unitNumber = "Awaiting allocation", unreadMessages = 0 } };
                return new { success = true, summary = new { home.OutstandingBalance, residenceName = home.Accommodation?.Name ?? "Not allocated", unitNumber = home.Unit?.UnitNumber ?? "Awaiting allocation", home.UnreadMessages, home.UnreadNotifications }, item = Safe(home.LatestApplication), items = Safe(home.RecentBills) };
            case "report":
                Need(finance);
                var reportType = Text(fields, "type");
                return Items(await Model<ReportingController>(reportType.Equals("occupancy", StringComparison.OrdinalIgnoreCase) ? "Occupancy" : "Revenue"));
            case "paymentAnomalies":
            case "paymentAnomalyAnalyze":
                Need(finance);
                var anomalies = _services.GetRequiredService<PaymentAnomalyDetectionService>().DetectAnomalies();
                return Items(anomalies.Select(a => new { id = a.Payment.Id, type = a.AnomalyType, a.Description, a.Severity, transactionReferenceId = a.Payment.TransactionReferenceId }));
            case "globalSearch":
                Need(finance);
                var model = new SearchViewModel();
                foreach (var prop in typeof(SearchViewModel).GetProperties().Where(p => p.CanWrite))
                {
                    var key = JsonNamingPolicy.CamelCase.ConvertName(prop.Name);
                    if (fields.TryGetValue(key, out var value))
                    {
                        if (prop.PropertyType == typeof(string)) prop.SetValue(model, value.GetString());
                        else if (prop.PropertyType == typeof(bool)) prop.SetValue(model, value.ValueKind == JsonValueKind.True);
                    }
                }
                model.GetType().GetProperty("Query")?.SetValue(model, Text(fields, "query"));
                var search = await Model<SearchController>("Index", model);
                var searchMap = (Dictionary<string, object?>?)Safe(search) ?? new();
                var found = searchMap.Where(kv => kv.Value is IList).SelectMany(kv => ((IList)kv.Value!).Cast<object?>()).ToList();
                return new { success = true, items = found, summary = searchMap };
            case "supportInfo":
                return Items(ReadStaticSections("Views/Support/Index.cshtml"));
            case "passwordResetRequests":
                Need(finance);
                return Items(await Model<AccountController>("PasswordResetRequests"));
            default:
                return null;
        }
    }

    public async Task<IActionResult> Result<T>(string method, params object?[] args) where T : Controller
    {
        using var controller = ActivatorUtilities.CreateInstance<T>(_services);
        controller.ControllerContext = new ControllerContext { HttpContext = _http.HttpContext! };
        controller.TempData = new TempDataDictionary(_http.HttpContext!, _services.GetRequiredService<ITempDataProvider>());
        var methodInfo = typeof(T).GetMethods().Single(m => m.Name == method && m.GetParameters().Length == args.Length);
        var result = methodInfo.Invoke(controller, args);
        if (result is Task task)
        {
            await task;
            return (IActionResult)task.GetType().GetProperty("Result")!.GetValue(task)!;
        }
        return (IActionResult)result!;
    }
    private async Task<object?> Model<T>(string method, params object?[] args) where T : Controller
    {
        var result = await Result<T>(method, args);
        if (result is ViewResult view) return view.Model;
        if (result is NotFoundResult) throw new KeyNotFoundException("Record not found.");
        if (result is ForbidResult or ChallengeResult) throw new UnauthorizedAccessException("You do not have permission to view this record.");
        throw new InvalidOperationException("The source workflow did not return readable records.");
    }
    public static string Text(IReadOnlyDictionary<string, JsonElement> fields, string key) => fields.TryGetValue(key, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : "";
    public static object PublicUser(ApplicationUser u) => new { id = u.Id, uid = u.Id, u.FirstName, u.LastName, u.Email, u.UserName, u.PhoneNumber, u.StudentNumber, u.EmployeeNumber, u.IsStudent, u.IsStaff, u.IsAdmin, u.IsFinancialOfficer, u.IsMaintenanceWorker, specialty = u.MaintenanceSpecialty?.ToString() };
    public static object? Safe(object? value, int depth = 0)
    {
        if (value == null || depth > 5) return null;
        if (value is ApplicationUser user) return Safe(PublicUser(user), depth + 1);
        if (value is SavedPaymentCard card) return new Dictionary<string, object?> { ["id"] = card.Id, ["cardholderName"] = card.CardholderName, ["lastFour"] = card.LastFour, ["expiry"] = card.Expiry, ["createdAt"] = card.CreatedAt };
        var type = value.GetType();
        if (type.IsEnum) return value.ToString();
        if (value is string || type.IsPrimitive || value is decimal or DateTime or DateTimeOffset or Guid) return value;
        if (value is IEnumerable items) return items.Cast<object?>().Select(x => Safe(x, depth + 1)).ToList();
        var entity = type.Namespace == "BricksAndBooks.Models";
        var result = new Dictionary<string, object?>();
        foreach (var prop in type.GetProperties().Where(p => p.CanRead && p.GetIndexParameters().Length == 0))
        {
            if (prop.Name.Contains("Password", StringComparison.OrdinalIgnoreCase) || prop.Name.Contains("Cvc", StringComparison.OrdinalIgnoreCase) || prop.Name.EndsWith("Stamp")) continue;
            var propType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            if (entity && !(propType.IsPrimitive || propType.IsEnum || propType == typeof(string) || propType == typeof(decimal) || propType == typeof(DateTime) || propType == typeof(DateTimeOffset))) continue;
            result[JsonNamingPolicy.CamelCase.ConvertName(prop.Name)] = Safe(prop.GetValue(value), depth + 1);
        }
        return result;
    }
    public static IEnumerable<object> ReadStaticSections(string relative)
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), relative);
        if (!File.Exists(path)) return Array.Empty<object>();
        var doc = new HtmlAgilityPack.HtmlDocument(); doc.Load(path);
        return (doc.DocumentNode.SelectNodes("//h1|//h2|//h3|//h4|//p") ?? new HtmlAgilityPack.HtmlNodeCollection(null))
            .Where(n => !n.InnerText.Contains('@')).Select(n => new { title = n.Name.StartsWith('h') ? HtmlAgilityPack.HtmlEntity.DeEntitize(n.InnerText.Trim()) : "", text = n.Name == "p" ? HtmlAgilityPack.HtmlEntity.DeEntitize(n.InnerText.Trim()) : "" });
    }
}
