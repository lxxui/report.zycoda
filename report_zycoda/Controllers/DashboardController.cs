using Microsoft.AspNetCore.Mvc;

namespace report_zycoda.Controllers
{
    public class DashboardController : Controller
    {
        public IActionResult Index()
        {
            // เช็ค Session กันคนแอบเข้า
            //if (string.IsNullOrEmpty(HttpContext.Session.GetString("ApiUser")))
            //{
            //    return RedirectToAction("Login", "Home");
            //}
            return View();
        }
    }
}
