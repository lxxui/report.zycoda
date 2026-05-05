using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Newtonsoft.Json;
using report_zycoda.Models;
using System.Globalization;

namespace report_zycoda.Controllers
{
    public class MaintenanceController : Controller
    {
        private readonly ApiService _apiService;

        public MaintenanceController(ApiService apiService)
        {
            _apiService = apiService;
        }

        public async Task<IActionResult> Index(string jobtype, string start, string end, string sectionFrom, string sectionTo, string Status, string v = "all")
        {
            // 1. Authentication Check
            if (User.Identity == null || !User.Identity.IsAuthenticated)
                return RedirectToAction("Login", "Home");

            var sessionUser = User.Identity.Name;
            var sessionPass = User.Claims.FirstOrDefault(c => c.Type == "ApiPassword")?.Value;
            if (string.IsNullOrEmpty(sessionPass)) return RedirectToAction("Login", "Home");

            // 2. Default Values (บังคับใช้ InvariantCulture เพื่อปี ค.ศ.)
            var culture = CultureInfo.InvariantCulture;
            if (string.IsNullOrEmpty(jobtype)) jobtype = "EM,CM";
            if (string.IsNullOrEmpty(start)) start = DateTime.Now.ToString("yyyy-MM-dd", culture);
            if (string.IsNullOrEmpty(end)) end = DateTime.Now.ToString("yyyy-MM-dd", culture);

            // 3. ส่งค่ากลับไปให้ View (ชื่อต้องตรงกับที่ View เรียกใช้)
            ViewBag.StartDate = start;
            ViewBag.EndDate = end;
            ViewBag.CurrentJobType = jobtype;
            ViewBag.CurrentSectionFrom = sectionFrom;
            ViewBag.CurrentSectionTo = sectionTo;
            ViewBag.CurrentStatus = Status;

            // 4. ดึง Master Data (Sections & Statuses)
            var sectionMaster = await _apiService.GetSectionsFromApiAsync();
            ViewBag.SectionList = sectionMaster?.OrderBy(x => x.Sections).ToList() ?? new List<SectionApiModels>();

            var statusMaster = await _apiService.GetStatusesFromApiAsync();
            ViewBag.StatusList = statusMaster?.OrderBy(x => x.StatusId).ToList() ?? new List<StatusApiModels>();

            // 5. ดึงข้อมูล Job Order จาก API
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

            // 6. Filter ข้อมูล (ทำใน Memory หลังจากได้ข้อมูลจาก API)
            data = data.Where(x =>
            {
                // Filter Section Range
                var targetSec = (x.sectioncreate ?? x.section ?? "").Trim();
                bool matchFrom = string.IsNullOrEmpty(sectionFrom) || string.Compare(targetSec, sectionFrom.Trim()) >= 0;
                bool matchTo = string.IsNullOrEmpty(sectionTo) || string.Compare(targetSec, sectionTo.Trim()) <= 0;

                // Filter Status
                bool matchStatus = string.IsNullOrEmpty(Status) ||
                                   (x.status != null && x.status.Equals(Status, StringComparison.OrdinalIgnoreCase));

                return matchFrom && matchTo && matchStatus;
            }).ToList();

            return View(data);
        }
    }
}