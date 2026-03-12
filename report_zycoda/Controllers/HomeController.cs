using Microsoft.AspNetCore.Mvc;
using report_zycoda.Models;

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
        // ถ้ามี Session อยู่แล้ว ไม่ต้องล็อกอินซ้ำ
        if (!string.IsNullOrEmpty(HttpContext.Session.GetString("ApiUser")))
        {
            return RedirectToAction("Index", "Maintenance");
        }
        return View();
    }

    [HttpPost]
    public IActionResult Login(string username, string password)
    {
        // 🚩 เช็คทางลัด (Hardcode)
        if (username == "723582" && password == "3582")
        {
            // ✅ จำลองข้อมูล User ใส่ Session
            HttpContext.Session.SetString("ApiUser", "723582");
            HttpContext.Session.SetString("ApiPass", "3582");
            HttpContext.Session.SetString("UserFullName", "Fai Narirat");
            HttpContext.Session.SetString("UserSection", "IT");

            return RedirectToAction("Index", "Maintenance");
        }

        // ถ้าไม่ใช่รหัสนี้ ให้เด้งกลับหน้าเดิมพร้อม Error
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