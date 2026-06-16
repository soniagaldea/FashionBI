using FashionDataAnalysisPlatform.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionDataAnalysisPlatform.Controllers
{
    [Authorize]
    public class StoreConnectionController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;

        public StoreConnectionController(AppDbContext context, IHttpClientFactory httpClientFactory)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var connections = await _context.StoreConnections
                .AsNoTracking()
                .OrderBy(c => c.StoreName)
                .ToListAsync();
            return View(connections);
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> TestConnection(int id)
        {
            var conn = await _context.StoreConnections.FindAsync(id);
            if (conn == null) return Json(new { ok = false, message = "Connection not found." });

            var url = conn.StoreApiUrl.TrimEnd('/') + "/api/Stores";
            try
            {
                var http = _httpClientFactory.CreateClient("StoreSync");
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                return response.IsSuccessStatusCode
                    ? Json(new { ok = true,  message = "Connection successful." })
                    : Json(new { ok = false, message = $"API returned HTTP {(int)response.StatusCode}." });
            }
            catch
            {
                return Json(new { ok = false, message = "API unavailable. Sync skipped." });
            }
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> SyncNow(int id)
        {
            var conn = await _context.StoreConnections.FindAsync(id);
            if (conn == null) return Json(new { ok = false, message = "Connection not found." });

            if (!conn.IsActive)
                return Json(new { ok = false, message = "Connection is disabled. Enable it first." });

            var url = conn.StoreApiUrl.TrimEnd('/') + "/api/Stores";
            try
            {
                var http = _httpClientFactory.CreateClient("StoreSync");
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);

                if (!response.IsSuccessStatusCode)
                    return Json(new { ok = false, message = "API unavailable. Sync skipped." });

                conn.LastSyncAt = DateTime.Now;
                await _context.SaveChangesAsync();
                return Json(new
                {
                    ok       = true,
                    message  = "Sync completed successfully.",
                    lastSync = conn.LastSyncAt?.ToString("dd MMM yyyy, HH:mm")
                });
            }
            catch
            {
                return Json(new { ok = false, message = "API unavailable. Sync skipped." });
            }
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> Toggle(int id)
        {
            var conn = await _context.StoreConnections.FindAsync(id);
            if (conn == null) return Json(new { ok = false, message = "Connection not found." });

            conn.IsActive = !conn.IsActive;
            await _context.SaveChangesAsync();
            return Json(new { ok = true, isActive = conn.IsActive });
        }
    }
}
