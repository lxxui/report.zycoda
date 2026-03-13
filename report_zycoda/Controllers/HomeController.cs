using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using report_zycoda.Models;

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
            string jsonPath = Path.Combine(_env.WebRootPath, "data", "user.json");

            if (System.IO.File.Exists(jsonPath))
            {
                using (var reader = new StreamReader(jsonPath, System.Text.Encoding.UTF8, true))
                {
                    var jsonString = await reader.ReadToEndAsync();
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var userList = JsonSerializer.Deserialize<List<UserApiModels>>(jsonString, options);

                    if (userList != null)
                    {
                        // ค้นหา User โดยใช้ Trim() เพื่อป้องกันช่องว่างเกิน
                        var user = userList.FirstOrDefault(u =>
                            u.username?.ToString().Trim() == username?.Trim() &&
                            u.password?.ToString().Trim() == password?.Trim());

                        if (user != null)
                        {
                            // 🚩 หัวใจสำคัญ: เก็บ Password ลง Session ชื่อ "ApiPass" 
                            // เพื่อให้ MaintenanceController ดึงไปใช้ต่อ API ของ Zycoda ได้
                            HttpContext.Session.SetString("ApiUser", user.username?.Trim() ?? "");
                            HttpContext.Session.SetString("ApiPass", user.password?.Trim() ?? ""); // บรรทัดนี้ห้ามลืม!

                            HttpContext.Session.SetString("UserFullName", $"{user.firstname} {user.lastname}");
                            HttpContext.Session.SetString("UserSection", user.section ?? "N/A");
                            HttpContext.Session.SetString("UserRole", user.rule ?? "");

                            // บังคับ Save Session ลง Memory ทันที
                            await HttpContext.Session.CommitAsync();

                            return RedirectToAction("Index", "Dashboard");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "เกิดข้อผิดพลาดตอน Login");
        }

        // ถ้าเข้าเงื่อนไขนี้แสดงว่ารหัสผิด หรือหาไฟล์ไม่เจอ
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