using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BricksAndBooks.Data;
using BricksAndBooks.Infrastructure;
using BricksAndBooks.Models;

namespace BricksAndBooks.Controllers
{
    [Authorize(Roles = "Admin,FinancialOfficer")] // Only admins and financial officers can view payment anomalies
    public class PaymentAnomalyController : Controller
    {
        private readonly PaymentAnomalyDetectionService _anomalyDetectionService;
        private readonly AuditService _auditService;

        public PaymentAnomalyController(PaymentAnomalyDetectionService anomalyDetectionService, AuditService auditService)
        {
            _anomalyDetectionService = anomalyDetectionService;
            _auditService = auditService;
        }

        // GET: PaymentAnomaly
        public async Task<IActionResult> Index()
        {
            var anomalies = _anomalyDetectionService.DetectAnomalies();
            return View(anomalies);
        }

        // GET: PaymentAnomaly/Details/5
        public async Task<IActionResult> Details(int id)
        {
            var payment = _anomalyDetectionService
                .DetectAnomalies()
                .FirstOrDefault(a => a.Payment.Id == id)?
                .Payment;

            if (payment == null)
            {
                return NotFound();
            }

            // Get the anomaly details for this payment
            var anomaly = _anomalyDetectionService.DetectAnomalies()
                .FirstOrDefault(a => a.Payment.Id == id);

            ViewBag.AnomalyType = anomaly?.AnomalyType;
            ViewBag.Description = anomaly?.Description;
            ViewBag.Severity = anomaly?.Severity;

            return View(payment);
        }

        // POST: PaymentAnomaly/Resolve/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Resolve(int id)
        {
            var anomaly = _anomalyDetectionService.DetectAnomalies().FirstOrDefault(a => a.Payment.Id == id);
            if (anomaly == null)
            {
                return NotFound();
            }
            _auditService.Record(User, "Investigate", "PaymentAnomaly", id, $"Reviewed {anomaly.AnomalyType} anomaly. {anomaly.Description}");
            TempData["StatusMessage"] = "The anomaly investigation was recorded in the audit log.";
            return RedirectToAction(nameof(Index));
        }
    }
}