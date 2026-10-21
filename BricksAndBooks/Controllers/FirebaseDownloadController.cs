using System.Text;
using BricksAndBooks.Controllers;
using BricksAndBooks.Infrastructure;
using BricksAndBooks.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BricksAndBooks.Controllers;

[ApiController]
[Route("api/firebase")]
public sealed class FirebaseDownloadController : ControllerBase
{
    private readonly NativeFirebaseQueries _queries;
    private readonly FirebaseMvcActions _actions;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IWebHostEnvironment _environment;
    public FirebaseDownloadController(NativeFirebaseQueries queries, FirebaseMvcActions actions, UserManager<ApplicationUser> users, IWebHostEnvironment environment) { _queries=queries;_actions=actions;_users=users;_environment=environment; }
    [HttpGet("health")]
    public IActionResult Health() => Ok(new { success=true, version=4, project="bricks-and-books", architecture="spark-hybrid-native" });
    [HttpPost("public")]
    public IActionResult Public([FromBody] Dictionary<string,string>? data) => Ok(new { success=true, items=NativeFirebaseQueries.ReadStaticSections(data?.GetValueOrDefault("section")=="privacy" ? "Views/Home/Privacy.cshtml" : "Views/Support/Index.cshtml") });
    [HttpGet("download")]
    public async Task<IActionResult> Download(string type,string? id,CancellationToken cancellationToken)
    {
        if(HttpContext.Items["FirebaseVerified"] is not true)return Unauthorized();
        var user=await _users.GetUserAsync(User);if(user==null)return Unauthorized();
        if(type=="proof")return await _queries.Result<AccommodationApplicationController>("DownloadProof");
        if(type=="paymentProof" && int.TryParse(id,out var proofPaymentId))
        {
            // Details applies the same owner/finance/admin access policy as the native payment screen.
            var detail=await _queries.Result<PaymentController>("Details",proofPaymentId);
            if(detail is not ViewResult { Model: Payment payment })return detail;
            const string prefix="/uploads/payments/";
            var stored=payment.ProofOfPaymentUrl;
            if(string.IsNullOrWhiteSpace(stored)||!stored.StartsWith(prefix,StringComparison.Ordinal))return NotFound();
            var fileName=stored[prefix.Length..];
            if(string.IsNullOrWhiteSpace(fileName)||fileName!=Path.GetFileName(fileName)||fileName.Contains('\\'))return NotFound();
            var root=Path.GetFullPath(Path.Combine(_environment.WebRootPath,"uploads","payments"));
            var file=Path.GetFullPath(Path.Combine(root,fileName));
            if(!file.StartsWith(root+Path.DirectorySeparatorChar,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal)||!System.IO.File.Exists(file))return NotFound();
            var contentType=Path.GetExtension(file).ToLowerInvariant() switch { ".pdf"=>"application/pdf", ".jpg" or ".jpeg"=>"image/jpeg", ".png"=>"image/png", _=>"application/octet-stream" };
            return PhysicalFile(file,contentType,fileName);
        }
        if(type=="receipt" && int.TryParse(id,out var paymentId))return await _queries.Result<PaymentController>("DownloadReceipt",paymentId);
        if(type=="revenue"||type=="occupancy")
        {
            if(!User.IsInRole("Admin")&&!User.IsInRole("FinancialOfficer"))return Forbid();
            return await _queries.Result<ReportingController>(type=="revenue"?"DownloadRevenue":"DownloadOccupancy");
        }
        if(type=="bill" && int.TryParse(id,out var billId))
        {
            var response=await _queries.ExecuteAsync("myBills",new Dictionary<string,System.Text.Json.JsonElement>(),user);
            var root=System.Text.Json.JsonSerializer.SerializeToElement(response,new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            var bill=root.GetProperty("items").EnumerateArray().FirstOrDefault(x=>x.GetProperty("id").GetInt32()==billId);
            if(bill.ValueKind==System.Text.Json.JsonValueKind.Undefined)return NotFound();
            return File(Encoding.UTF8.GetBytes("BRICKS & BOOKS — BILL STATEMENT\n\n"+System.Text.Json.JsonSerializer.Serialize(bill,new System.Text.Json.JsonSerializerOptions{WriteIndented=true})),"text/plain",$"bill-{billId}.txt");
        }
        return BadRequest(new {success=false,message="Unsupported or unavailable download."});
    }
}
