using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Newtonsoft.Json;
using report_zycoda.Models;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using static System.Collections.Specialized.BitVector32;


namespace report_zycoda.Controllers
{
    //private readonly IWebHostEnvironment _env;

    public class MaintenanceController : Controller
    {
        private readonly ApiService _apiService; // ✅ อย่าลืม Inject service เข้ามาใช้งาน

        public MaintenanceController(ApiService apiService)
        {
            _apiService = apiService;
        }

        public async Task<IActionResult> Index(string jobtype, string start, string end, string sectionFrom, string sectionTo, string Status, string v = "all")
        {
            // 1. ตรวจสอบบัตรพนักงาน (IsAuthenticated)
            if (User.Identity == null || !User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Login", "Home");
            }

            // ดึงข้อมูลจาก Claims ที่เราสร้างไว้ตอน Login
            var sessionUser = User.Identity.Name;
            var sessionPass = User.Claims.FirstOrDefault(c => c.Type == "ApiPassword")?.Value;

            // ถ้าไม่มี Password ในบัตร ให้เตะกลับไป Login ใหม่ (ป้องกัน Error เรียก API)
            if (string.IsNullOrEmpty(sessionPass)) return RedirectToAction("Login", "Home");

            // 2. จัดการค่าพื้นฐาน (Default Values)
            if (string.IsNullOrEmpty(jobtype)) jobtype = "EM,CM";
            if (string.IsNullOrEmpty(start)) start = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(end)) end = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            // 3. ส่งค่ากลับไปให้ View (สำหรับ Dropdown/Inputs)
            ViewBag.CurrentStart = start;
            ViewBag.CurrentEnd = end;
            ViewBag.CurrentJobType = jobtype;
            ViewBag.CurrentSectionFrom = sectionFrom;
            ViewBag.CurrentSectionTo = sectionTo;
            ViewBag.SelectedStatus = Status;

            // --- ส่วนที่ 4 ใน MaintenanceController ---

            // 1. ดึง Section จาก API 
            var sectionMaster = await _apiService.GetSectionsFromApiAsync();
            ViewBag.SectionList = sectionMaster.OrderBy(x => x.Sections).ToList();

            var statusMaster = await _apiService.GetStatusesFromApiAsync();
            ViewBag.StatusList = statusMaster.OrderBy(x => x.StatusId).ToList();


            // 5. เรียก API เพื่อดึงข้อมูล Job Order
            var queryParams = new Dictionary<string, string?>
        {
            { "plant", "FARMHOUSE" },
            { "jobtype", jobtype },
            { "start", start },
            { "end", end },
            { "v", v },
            { "status", "ALL" }
        };

            var baseUrl = "https://api.zycoda.com/apimpros/get_job_order";
            var apiUrl = QueryHelpers.AddQueryString(baseUrl, queryParams);

            List<MaintenanceApiModels> data = new();

            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("username", sessionUser);
                client.DefaultRequestHeaders.Add("password", sessionPass);

                var response = await client.GetAsync(apiUrl);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    data = JsonConvert.DeserializeObject<List<MaintenanceApiModels>>(json) ?? new();
                }
            }

            // 6. Filter ข้อมูลตามเงื่อนไขที่เลือก
            if (!string.IsNullOrEmpty(sectionFrom) || !string.IsNullOrEmpty(sectionTo))
            {
                data = data.Where(x =>
                {
                    var target = (x?.sectioncreate ?? x?.section ?? "").Trim();
                    if (string.IsNullOrEmpty(target)) return false;
                    bool matchFrom = string.IsNullOrEmpty(sectionFrom) || string.Compare(target, sectionFrom.Trim()) >= 0;
                    bool matchTo = string.IsNullOrEmpty(sectionTo) || string.Compare(target, sectionTo.Trim()) <= 0;
                    return matchFrom && matchTo;
                }).ToList();
            }

            if (!string.IsNullOrEmpty(Status))
            {
                data = data.Where(x => !string.IsNullOrEmpty(x.status) &&
                                        x.status.Equals(Status, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            return View(data);
        }
    }
}