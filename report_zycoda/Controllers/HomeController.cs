using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using report_zycoda.Models;
using System.Security.Claims;

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
        if (!string.IsNullOrEmpty(HttpContext.Session.GetString("ApiUser")))
        {
            return RedirectToAction("Index", "Maintenance");
        }
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Login(string username, string password)
    {
        var debugLogs = new List<string>();
        try
        {
            debugLogs.Add($"🔍 เริ่มต้นตรวจสอบสำหรับ: {username}");

            // 1. ลองดึงข้อมูลทั้งหมดมาดูจำนวน
            var allUsers = await _apiService.GetUsersFromApiAsync();
            debugLogs.Add($"✅ เชื่อมต่อ DB สำเร็จ: พบพนักงานทั้งหมด {allUsers.Count} คน");

            // 2. ลองหา User ที่ Username ตรงกัน (ยังไม่เช็ค Pass)
            var findUser = allUsers.FirstOrDefault(u => u.Username?.Trim().ToLower() == username?.Trim().ToLower());

            if (findUser != null)
            {
                debugLogs.Add($"✅ เจอ Username นี้ในระบบ!");
                debugLogs.Add($"📝 ข้อมูลใน DB: Name='{findUser.FirstName}', Active='{findUser.Active}', PassInDB='{findUser.Password}'");

                // 3. ตรวจสอบรหัสผ่านและสถานะ Active
                var user = await _apiService.LoginAsync(username, password);
                if (user != null)
                {
                    // ... ส่วนการสร้าง Claims และ Session (เหมือนเดิม) ...
                    return RedirectToAction("Index", "Maintenance");
                }
                else
                {
                    debugLogs.Add($"❌ Login ไม่ผ่าน: รหัสผ่านไม่ตรง หรือ Active ไม่เป็น true");
                }
            }
            else
            {
                debugLogs.Add($"❌ ไม่พบ Username '{username}' ในรายชื่อ {allUsers.Count} คนที่ดึงมา");
            }
        }
        catch (Exception ex)
        {
            debugLogs.Add($"🚩 Error: {ex.Message}");
        }

        ViewBag.DebugLog = debugLogs;
        ViewBag.ErrorMessage = "ตรวจสอบสถานะจาก Log ด้านล่าง";
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
            var users = await _apiService.GetUsersFromApiAsync();
            var user = users?.FirstOrDefault(u => u.Username?.Trim() == username?.Trim());
            if (user != null) return Json(new { firstName = user.FirstName });
        }
        catch { /* ignored */ }
        return Json(new { firstName = "" });
    }
}