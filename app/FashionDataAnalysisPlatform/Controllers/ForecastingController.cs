using FashionDataAnalysisPlatform.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FashionDataAnalysisPlatform.Controllers
{
    [Authorize]
    public class ForecastingController : Controller
    {
        private readonly ForecastingService _forecastingService;

        public ForecastingController(ForecastingService forecastingService)
        {
            _forecastingService = forecastingService;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? store, string? category)
        {
            var vm = await _forecastingService.BuildViewModelAsync(store, category);

            // Pass any TempData feedback from a previous TriggerRefresh
            ViewBag.ForecastSuccess = TempData["ForecastSuccess"] as string;
            ViewBag.ForecastError   = TempData["ForecastError"]   as string;

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TriggerRefresh(string? store, string? category)
        {
            var (success, message) = await _forecastingService.TriggerRefreshAsync();

            if (success)
                TempData["ForecastSuccess"] = message;
            else
                TempData["ForecastError"] = message;

            return RedirectToAction(nameof(Index), new { store, category });
        }
    }
}
