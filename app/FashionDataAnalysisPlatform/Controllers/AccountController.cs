using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FashionDataAnalysisPlatform.Controllers
{
    public class AccountController : Controller
    {
        private readonly IConfiguration _config;

        public AccountController(IConfiguration config)
        {
            _config = config;
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Index", "Dashboard");

            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string email, string password, string? returnUrl = null)
        {
            var demoEmail    = _config["DemoUser:Email"]    ?? "";
            var demoPassword = _config["DemoUser:Password"] ?? "";
            var demoFullName = _config["DemoUser:FullName"] ?? "";
            var demoRole     = _config["DemoUser:Role"]     ?? "";

            if (!string.Equals(email?.Trim(), demoEmail, StringComparison.OrdinalIgnoreCase)
                || password != demoPassword)
            {
                ViewBag.ReturnUrl = returnUrl;
                ViewBag.Error     = "Invalid email or password.";
                return View();
            }

            await SignInUserAsync(demoFullName, demoEmail, demoRole);

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction("Index", "Dashboard");
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DemoLogin(string? returnUrl = null)
        {
            var demoFullName = _config["DemoUser:FullName"] ?? "";
            var demoEmail    = _config["DemoUser:Email"]    ?? "";
            var demoRole     = _config["DemoUser:Role"]     ?? "";

            await SignInUserAsync(demoFullName, demoEmail, demoRole);

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction("Index", "Dashboard");
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        [Authorize]
        [HttpGet]
        public IActionResult Profile()
        {
            return View();
        }

        private async Task SignInUserAsync(string fullName, string email, string role)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name,  fullName),
                new Claim(ClaimTypes.Email, email),
                new Claim(ClaimTypes.Role,  role),
                new Claim("Initials",       GetInitials(fullName)),
                new Claim("LoginAt",        DateTime.UtcNow.ToString("o")),
            };

            var identity  = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties { IsPersistent = true });
        }

        private static string GetInitials(string fullName)
        {
            var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2
                ? $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant()
                : fullName.Length > 0 ? fullName[0].ToString().ToUpperInvariant() : "?";
        }
    }
}
