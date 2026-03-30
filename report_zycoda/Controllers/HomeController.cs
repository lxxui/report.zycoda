using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using report_zycoda.Models;
using System.Security.Claims;
using System.Text.Json;
using System.Xml.Linq;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly IWebHostEnvironment _env;
    private readonly ApiService _apiService;

    // แก้ไข Constructor ให้รับค่า env จากระบบ
    public HomeController(ILogger<HomeController> logger, ApiService apiService, IWebHostEnvironment env)
    {
        _logger = logger;
        _apiService = apiService;
        _env = env; // ตอนนี้ _env จะไม่เป็น null แล้วครับ
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
        try
        {
            string userPath = Path.Combine(_env.WebRootPath, "data", "user.json");

            if (System.IO.File.Exists(userPath))
            {
                var userJson = await System.IO.File.ReadAllTextAsync(userPath, System.Text.Encoding.UTF8);

                // 🚩 ปรับ Option ให้ CaseInsensitive (ไม่สนตัวเล็กตัวใหญ่)
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var userList = JsonSerializer.Deserialize<List<UserApiModels>>(userJson, options);

                var user = userList?.FirstOrDefault(u =>
                    u.username?.Trim() == username?.Trim() &&
                    u.password?.Trim() == password?.Trim());

                if (user != null)
                {
                    // --- ส่วนการหาชื่อแผนก (เหมือนเดิมแต่ย้ายมาข้างล่างเพื่อความสะอาด) ---
                    string displaySectionName = user.section ?? "N/A";
                    try
                    {
                        string sectionPath = Path.Combine(_env.WebRootPath, "data", "section.json");
                        if (System.IO.File.Exists(sectionPath))
                        {
                            var sectionJson = await System.IO.File.ReadAllTextAsync(sectionPath, System.Text.Encoding.UTF8);
                            var sectionList = JsonSerializer.Deserialize<List<SectionApiModels>>(sectionJson, options);
                            var match = sectionList?.FirstOrDefault(s => s.section == user.section);
                            if (match != null) displaySectionName = match.name;
                        }
                    }
                    catch { /* ignored */ }

                    // --- 🚩 สร้าง Claims (จุดสำคัญที่ต้องแก้) ---
                    var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, user.username ?? ""),
                    new Claim("FullName", $"{user.firstname} {user.lastname}"),
                    new Claim("Section", displaySectionName),
                    new Claim("ApiPassword", user.password ?? ""),
                    
                    // 🚩 ยัดค่าเข้า Claims (ต้องใช้ .ToString() และเช็ค null)
                    new Claim("Class", user.@class.ToString()),
                    new Claim("SectionOption", user.sectionoption ?? ""),
                    new Claim("UserSectionCode", user.section ?? "")
                };

                    // จัดการ Role
                    if (!string.IsNullOrEmpty(user.rule))
                    {
                        foreach (var role in user.rule.Split(','))
                            claims.Add(new Claim(ClaimTypes.Role, role.Trim()));
                    }

                    var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

                    // 🚩 เคลียร์ Cookie เก่าก่อน Sign-In ใหม่
                    await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity));

                    // 1. เคลียร์ Session เก่าทิ้ง
                    HttpContext.Session.Clear();

                    HttpContext.Session.SetString("UserClass", user.@class.ToString());
                    HttpContext.Session.SetString("FullSectionOption", user.sectionoption ?? "");
                    HttpContext.Session.SetString("UserSectionCode", user.section ?? "");

                    // 🚩 เพิ่ม 2 บรรทัดนี้ เพื่อให้ PrintReport ดึงไปใช้ต่อได้
                    HttpContext.Session.SetString("ApiUser", user.username ?? "");
                    HttpContext.Session.SetString("ApiPass", user.password ?? "");

                    // บังคับ Commit Session ทันที
                    await HttpContext.Session.CommitAsync();

                    return RedirectToAction("Index", "Maintenance");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login Error");
        }

        ViewBag.ErrorMessage = "รหัสพนักงานหรือรหัสผ่านไม่ถูกต้อง";
        return View();
    }
    [HttpPost]
    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return RedirectToAction("Login");
    }
}