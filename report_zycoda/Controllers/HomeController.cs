using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http; // 👈 เพื่อใช้งานดึง-เซ็ต Session
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
        // ถ้าเคย Login ค้างไว้แล้ว ให้ข้ามไปหน้า Maintenance เลย
        if (!string.IsNullOrEmpty(HttpContext.Session.GetString("ApiUser")))
        {
            return RedirectToAction("Index", "Maintenance");
        }
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Login(string username, string password)
    {
        try
        {
            // 1. ตรวจสอบ Username ว่ามีอยู่ในระบบเกตเวย์ไหม
            var checkUser = await _apiService.GetUsersFromApiAsync(username);

            if (checkUser == null)
            {
                TempData["ErrorMessage"] = "ไม่พบชื่อผู้ใช้งานในระบบ กรุณาติดต่อฝ่ายไอทีเพื่อลงทะเบียน";
                return View();
            }

            // 2. ตรวจสอบรหัสผ่านผ่าน ApiService
            var user = await _apiService.LoginAsync(username, password);

            if (user == null)
            {
                TempData["ErrorMessage"] = "รหัสผ่านไม่ถูกต้อง กรุณาลองใหม่อีกครั้ง";
                return View();
            }

            // ✅ บันทึกค่าลง Session แยกตัวแปรเดี่ยวตรง ๆ 
            // ป้องกันปัญหา SyncController เรียกหาค่า Username/Password แล้วเจอ Null จนเกิด Ajax Error ค่ะ
            HttpContext.Session.SetString("Username", username?.Trim() ?? "");
            HttpContext.Session.SetString("Password", password ?? "");
            HttpContext.Session.SetString("ApiUser", JsonConvert.SerializeObject(user));

            // ✅ ยัดค่าข้อมูลเข้า Claims Principal สำหรับใช้ตรวจสอบสิทธิ์ในระบบ
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.Username ?? ""),
                new Claim("FullName", $"{user.FirstName} {user.LastName}"),
                new Claim("UserRole", user.Rule ?? "User"),
                
                // ⚠️ ดึงค่าจากตัวแปร user.Class (หรือตรวจสอบ Property ในโมเดลของฝ้ายว่าสะกดอย่างไร)
                // เพื่อเอาตัวเลขสิทธิ์ (เช่น 9, 3, 2) ส่งต่อไปควบคุมหน้า Layout หน้าบ้าน
                new Claim("Class", user.Class?.ToString() ?? "0"),

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
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8) // ตั้งเวลา Session ฝั่งคุกกี้ให้อยู่ได้ 8 ชั่วโมงการทำงาน
                });

            return RedirectToAction("Index", "Maintenance");
        }
        catch (Exception ex)
        {
            _logger.LogError($"❌ Login Error: {ex.Message}");
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
            var users = await _apiService.GetUsersFromApiAsync();
            var user = users?.FirstOrDefault(u => u.Username?.Trim() == username?.Trim());
            if (user != null) return Json(new { firstName = user.FirstName });
        }
        catch { /* ignored */ }
        return Json(new { firstName = "" });
    }
}