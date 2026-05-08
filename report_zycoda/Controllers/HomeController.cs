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
    public async Task<IActionResult> Login(string username, string password)
    {
        try
        {
            // สมมติว่า _apiService มี Method สำหรับดึงข้อมูล User ตามชื่อนะจ๊ะ
            var checkUser = await _apiService.GetUsersFromApiAsync(username);

            if (checkUser == null)
            {
                // เคสที่ 1: ไม่พบ Username เลยตั้งแต่แรก
                TempData["ErrorMessage"] = "ไม่พบชื่อผู้ใช้งานในระบบ กรุณาติดต่อฝ่ายไอทีเพื่อลงทะเบียน";
                return View();
            }

            // ❌ ไม่มี username
            if (checkUser == null)
            {
                TempData["ErrorMessage"] = "ไม่พบชื่อผู้ใช้งานนี้ในระบบ กรุณาติดต่อไอที";
                return View();
            }

            // ❌ password ผิด
            var user = await _apiService.LoginAsync(username, password);

            if (user == null)
            {
                TempData["ErrorMessage"] = "รหัสผ่านไม่ถูกต้อง กรุณาลองใหม่อีกครั้ง";
                return View();
            }

            // ✅ success
            var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, user.Username ?? ""),
            new Claim("FullName", $"{user.FirstName} {user.LastName}"),
            new Claim("UserRole", user.Rule ?? "User"),
            new Claim("Section", user.Section ?? ""),
            new Claim("ApiPassword", password)
        };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
                });

            HttpContext.Session.SetString("ApiUser", JsonConvert.SerializeObject(user));

            return RedirectToAction("Index", "Maintenance");
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "ระบบขัดข้อง: " + ex.Message;
            return View();
        }
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