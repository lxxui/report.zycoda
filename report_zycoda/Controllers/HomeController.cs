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
                    // --- เริ่มต้นการหาชื่อแผนกจากรหัส ---
                    string displaySectionName = user.section ?? "N/A"; // ค่าเริ่มต้นเผื่อหาไม่เจอ

                    try
                    {
                        string sections = Path.Combine(_env.WebRootPath, "data", "section.json");
                        if (System.IO.File.Exists(sections))
                        {
                            var sectionJson = await System.IO.File.ReadAllTextAsync(sections, System.Text.Encoding.UTF8);
                            var sectionList = JsonSerializer.Deserialize<List<SectionApiModels>>(sectionJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                            // เทียบรหัสจาก user.json กับรหัสใน section.json เพื่อเอาค่า 'name'
                            var match = sectionList?.FirstOrDefault(s => s.section == user.section);
                            if (match != null)
                            {
                                displaySectionName = match.name; // จะได้ "แผนกพัฒนาระบบ (SD MIS)"
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error matching section name");
                    }

                    // --- สร้าง Claims โดยใช้ชื่อแผนกที่หาได้ ---
                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.Name, user.username ?? ""),
                        new Claim("FullName", $"{user.firstname} {user.lastname}"),
                        new Claim("Section", displaySectionName),
                        new Claim("ApiPassword", user.password ?? "") // 🚩 เก็บ Password ลงในตั๋วด้วย
                    };

                    // แตก Role เหมือนเดิม
                    if (!string.IsNullOrEmpty(user.rule))
                    {
                        var roles = user.rule.Split(',');
                        foreach (var role in roles)
                        {
                            claims.Add(new Claim(ClaimTypes.Role, role.Trim()));
                        }
                    }

                    var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                    await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity));

                    // เก็บชื่อแผนกลง Session เผื่อใช้ที่อื่นด้วย
                    HttpContext.Session.SetString("UserSection", displaySectionName);

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