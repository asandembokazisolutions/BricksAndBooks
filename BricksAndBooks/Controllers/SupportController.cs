using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BricksAndBooks.Controllers
{
    [Authorize]
    public class SupportController : Controller
    {
        public IActionResult Index() => View();
    }
}
