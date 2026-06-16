using FashionDataAnalysisPlatform.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FashionDataAnalysisPlatform.Controllers
{
    [Authorize]
    public class NotificationController : Controller
    {
        private readonly NotificationService _service;

        public NotificationController(NotificationService service)
        {
            _service = service;
        }

        [HttpGet]
        public IActionResult Preferences() => View();

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var notifications = await _service.GetNotificationsAsync();
            return Json(notifications);
        }
    }
}
