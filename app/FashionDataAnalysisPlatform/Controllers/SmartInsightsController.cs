using FashionDataAnalysisPlatform.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FashionDataAnalysisPlatform.Controllers
{
    [Authorize]
    public class SmartInsightsController : Controller
    {
        private readonly SmartInsightsService _service;

        public SmartInsightsController(SmartInsightsService service)
            => _service = service;

        public async Task<IActionResult> Index(List<int>? storeIds)
        {
            storeIds ??= new List<int>();
            ViewBag.SelectedStoreIds = storeIds;

            return View(await _service.BuildViewModelAsync(storeIds));
        }
    }
}
