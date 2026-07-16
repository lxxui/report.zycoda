using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using report_zycoda.Models;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Linq;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;

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
            _logger.LogInformation("🔄 [Login-Get] พบ Session เดิม ข้ามไปหน้า Maintenance");
            return RedirectToAction("Index", "Maintenance");
        }
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Login(string username, string password, string userClass)
    {
        // ทำการ Clean ค่าตัดช่องว่างว่างล่วงหน้า ป้องกันปัญหาข้อมูลประเภท char ใน DB 
        string cleanUsername = username?.Trim() ?? "";
        string cleanPassword = password?.Trim() ?? "";

        try
        {
            _logger.LogInformation($"🎬 [Login-Post] เริ่มขั้นตอนการเข้าสู่ระบบสำหรับ User: '{cleanUsername}'");

            // 1. ตรวจสอบ Username ว่ามีอยู่ในระบบไหม
            var checkUser = await _apiService.GetUsersFromApiAsync(cleanUsername);

            if (checkUser == null)
            {
                _logger.LogWarning($"⚠️ [Login Step 1] ไม่พบ Username: '{cleanUsername}' ในระบบเกตเวย์");
                TempData["ErrorMessage"] = "ไม่พบชื่อผู้ใช้งานในระบบ กรุณาติดต่อฝ่ายไอทีเพื่อลงทะเบียน";
                return View();
            }
            _logger.LogInformation($"🔍 [Login Step 1] พบ Username: '{cleanUsername}' ในระบบเกตเวย์แล้ว");

            // 2. ตรวจสอบรหัสผ่านผ่าน ApiService (✅ ตอนนี้คืนค่าเป็น LoginResult แทน UserApiModels)
            _logger.LogInformation($"🚀 [Login Step 2] กำลังส่งตรวจสอบรหัสผ่านไปยัง ApiService...");
            var loginResult = await _apiService.LoginAsync(cleanUsername, cleanPassword);

            // ❌ กรณีบัญชีถูกล็อค (ใส่รหัสผิดครบ 3 ครั้ง) -> ต้องรอไอทีปลดล็อคเท่านั้น
            if (loginResult.Status == LoginStatus.AccountLocked)
            {
                _logger.LogWarning($"🔒 [Login Step 2] บัญชี '{cleanUsername}' ถูกล็อค (ล็อคเมื่อ: {loginResult.LockedAt})");
                TempData["ErrorMessage"] = "บัญชีของคุณถูกล็อคเนื่องจากใส่รหัสผ่านผิดเกิน 3 ครั้ง กรุณาติดต่อฝ่ายไอทีเพื่อปลดล็อคบัญชี";
                return View();
            }

            // ❌ กรณีรหัสผ่านผิด (ยังไม่ถึงจำนวนครั้งที่ล็อค)
            if (loginResult.Status == LoginStatus.InvalidCredentials || loginResult.User == null)
            {
                _logger.LogError($"❌ [Login Step 2] รหัสผ่านไม่ถูกต้องสำหรับ '{cleanUsername}' (เหลือโอกาสอีก {loginResult.RemainingAttempts} ครั้ง)");

                TempData["ErrorMessage"] = loginResult.RemainingAttempts > 0
                    ? $"รหัสผ่านไม่ถูกต้อง กรุณาลองใหม่อีกครั้ง (เหลือโอกาสอีก {loginResult.RemainingAttempts} ครั้งก่อนบัญชีถูกล็อค)"
                    : "รหัสผ่านไม่ถูกต้อง กรุณาลองใหม่อีกครั้ง";

                return View();
            }

            // ✅ Login สำเร็จ -> ดึง user object จาก loginResult.User
            var user = loginResult.User;

            _logger.LogInformation($"✅ [Login Step 2] ตรวจสอบรหัสผ่านผ่านสำเร็จ! ยินดีต้อนรับคุณ: {user.FirstName} {user.LastName} (Class: {user.Class}, Active: {user.Active})");

            // บันทึกค่าลง Session
            HttpContext.Session.SetString("Username", cleanUsername);
            HttpContext.Session.SetString("Password", cleanPassword);
            HttpContext.Session.SetString("ApiUser", JsonConvert.SerializeObject(user));
            _logger.LogInformation("💾 [Login Step 3] บันทึกข้อมูลลง Session เรียบร้อย");

            // ยัดค่าข้อมูลเข้า Claims Principal
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.Username ?? cleanUsername),
                new Claim("FullName", $"{user.FirstName} {user.LastName}"),
                new Claim("UserRole", user.Rule ?? "User"),
                //new Claim("UserClass", user.Class?.ToString() ?? "0"),
                new Claim("Class", user.Class?.ToString() ?? "0"), // 👈 🛠️ แก้จาก "UserClass" เป็น "Class"
                new Claim("Section", user.Section ?? ""),
                new Claim("ApiPassword", cleanPassword)
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

            _logger.LogInformation("🎫 [Login Step 4] ออก Cookie Claims สำเร็จ -> ย้ายหน้าไป Maintenance");
            return RedirectToAction("Index", "Maintenance");
        }
        catch (Exception ex)
        {
            _logger.LogError($"💥 [Login Exception] เกิดข้อผิดพลาดร้ายแรง: {ex.Message} \nStackTrace: {ex.StackTrace}");
            TempData["ErrorMessage"] = "ระบบขัดข้อง: " + ex.Message;
            return View();
        }
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        _logger.LogInformation("🚪 [Logout] กำลังออกจากระบบ และล้าง Session");
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        HttpContext.Session.Clear();
        return RedirectToAction("Login");
    }

    [HttpGet]
    public async Task<IActionResult> GetFirstName(string username)
    {
        try
        {
            if (string.IsNullOrEmpty(username)) return Json(null);

            string cleanName = username.Trim();

            var user = await _apiService.GetUsersFromApiAsync(cleanName);

            if (user != null)
            {
                _logger.LogInformation($"[DEBUG] เจอพนักงาน: {user.FirstName} | ค่า Class ใน Model คือ: {user.Class}");

                return Json(new
                {
                    firstName = user.FirstName,
                    userClass = user.Class?.ToString() ?? ""
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"🚨 [GetFirstName Error]: {ex.Message}");
        }
        return Json(null);
    }
}