using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace report_zycoda.Controllers
{
    // 🔒 ล็อกสิทธิ์ไว้เบื้องต้น (ถ้าพี่ทำระบบล็อกอินด้วย Cookie ไว้ ตัวนี้จะช่วยดักไม่ให้คนไม่ล็อกอินแอบเข้าตรงๆ)
    [Authorize]
    public class MachineController : Controller
    {
        private readonly ILogger<MachineController> _logger;

        // Constructor (เผื่อพี่ต้องเรียกใช้ดีบี หรือ ApiService ในอนาคต)
        public MachineController(ILogger<MachineController> logger)
        {
            _logger = logger;
        }

        // GET: /Machine หรือ /Machine/Index
        [HttpGet]
        public IActionResult Index()
        {
            // 💡 เช็คสิทธิ์ซ้ำอีกรอบเพื่อความปลอดภัยขั้นสุด แอดมินต้องเป็น Class 9 เท่านั้น
            var userClass = User.Claims.FirstOrDefault(c => c.Type == "UserClass")?.Value;

            return RedirectToAction("Machine", "Maintenance");

        }
    }
}