using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using report_zycoda.Models;
using System.Security.Claims;
using Newtonsoft.Json; // 🚩 อย่าลืมเช็คว่าติดตั้ง NuGet นี้หรือยังครับ

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly ApiService _apiService;

    public HomeController(ILogger<HomeController> logger, ApiService apiService)
    {
        _logger = logger;
        _apiService = apiService;
    }

    [HttpGet]
    public IActionResult Login()
    {
        // ถ้าเคย Login ค้างไว้แล้ว ให้ข้ามไปหน้า Maintenance เลย
        if (!string.IsNullOrEmpty(HttpContext.Session.GetString("ApiUser")))
        {
            return RedirectToAction("Index", "Maintenance"); // ส่งไป Maintenance
        }
        return View();
    }

    [HttpPost]
    [HttpPost]
    public async Task<IActionResult> Login(string username, string password)
    {
        try
        {
            var user = await _apiService.LoginAsync(username, password);

            if (user != null)
            {
                var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.Username ?? ""),
                new Claim("FullName", user.FirstName ?? ""),
                new Claim("UserRole", user.Rule ?? "User"),
                new Claim("Section", user.Section ?? ""),
                // ✅ ต้องเก็บ password ไว้ใน Claim เพื่อให้หน้า Maintenance เอาไปใช้เรียก API
                new Claim("ApiPassword", password)
            };

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(claimsIdentity), new AuthenticationProperties { IsPersistent = true });

                HttpContext.Session.SetString("ApiUser", JsonConvert.SerializeObject(user));

                return RedirectToAction("Index", "Maintenance");
            }
            ViewBag.ErrorMessage = "Username หรือ Password ไม่ถูกต้อง";
        }
        catch (Exception ex)
        {
            ViewBag.ErrorMessage = $"เกิดข้อผิดพลาด: {ex.Message}";
        }
        return View();
    }
    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        HttpContext.Session.Clear();
        return RedirectToAction("Login");
    }

    [HttpGet]
    public async Task<IActionResult> GetFirstName(string username)
    {
        try
        {
            // ใช้ FirstOrDefault เพื่อความเร็วในการค้นหาชื่อตอนพิมพ์ Username
            var users = await _apiService.GetUsersFromApiAsync();
            var user = users?.FirstOrDefault(u => u.Username?.Trim() == username?.Trim());
            if (user != null) return Json(new { firstName = user.FirstName });
        }
        catch { /* ignored */ }
        return Json(new { firstName = "" });
    }
}