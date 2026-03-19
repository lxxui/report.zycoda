using Microsoft.AspNetCore.Mvc;
using report_zycoda.Models;
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
            string sectionPath = Path.Combine(_env.WebRootPath, "data", "section.json");

            if (System.IO.File.Exists(userPath))
            {
                var userJson = await System.IO.File.ReadAllTextAsync(userPath, System.Text.Encoding.UTF8);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var userList = JsonSerializer.Deserialize<List<UserApiModels>>(userJson, options);

                // 1. หา User ที่ Login เข้ามา
                var user = userList?.FirstOrDefault(u =>
                    u.username?.Trim() == username?.Trim() &&
                    u.password?.Trim() == password?.Trim());

                if (user != null)
                {
                    // เก็บข้อมูลพื้นฐานลง Session
                    HttpContext.Session.SetString("ApiUser", user.username?.Trim() ?? "");
                    HttpContext.Session.SetString("ApiPass", user.password?.Trim() ?? "");
                    HttpContext.Session.SetString("UserFullName", $"{user.firstname} {user.lastname}");
                    HttpContext.Session.SetString("UserRole", user.rule ?? "");

                    // 2. 🚩 เจาะจงหาชื่อแผนก (name) จาก section.json
                    string displaySection = user.section ?? "N/A"; // ค่า Default ถ้าหาไม่เจอ

                    if (System.IO.File.Exists(sectionPath))
                    {
                        var sectionJson = await System.IO.File.ReadAllTextAsync(sectionPath, System.Text.Encoding.UTF8);
                        // ใช้ Model Section โดยตรงเพื่อให้ดึงค่า 'name' ได้แม่นยำ
                        var sectionList = JsonSerializer.Deserialize<List<SectionApiModels>>(sectionJson, options);

                        // เทียบรหัสแผนกจาก User กับรหัสแผนกในไฟล์ JSON
                        var match = sectionList?.FirstOrDefault(s => s.section == user.section);
                        if (match != null)
                        {
                            displaySection = match.name; // เปลี่ยนจากรหัส "140200" เป็น "แผนกพัฒนาระบบ..."
                        }
                    }

                    HttpContext.Session.SetString("UserSection", displaySection);

                    // บังคับ Save Session
                    await HttpContext.Session.CommitAsync();

                    return RedirectToAction("Index", "Dashboard");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "เกิดข้อผิดพลาดตอน Login");
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