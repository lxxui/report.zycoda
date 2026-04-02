using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using report_zycoda.Models;
using System.Globalization;
using System.Text;
using System.Text.Json;
using static System.Collections.Specialized.BitVector32;


namespace report_zycoda.Controllers
{
    //private readonly IWebHostEnvironment _env;

    public class MaintenanceController : Controller
    {

        //[Authorize(Roles = "Administrator")]
        public async Task<IActionResult> Index(string jobtype, string start, string end, string sectionFrom, string sectionTo, string Status, string v = "all")
        {
            // 1. ตรวจสอบการ Login
            if (User.Identity == null || !User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Login", "Home");
            }

            var sessionUser = User.Identity.Name;
            var sessionPass = User.Claims.FirstOrDefault(c => c.Type == "ApiPassword")?.Value;

            if (string.IsNullOrEmpty(sessionPass)) return RedirectToAction("Login", "Home");

            // 2. จัดการค่าพื้นฐาน (Default Values)
            if (string.IsNullOrEmpty(jobtype)) jobtype = "EM,CM";
            if (string.IsNullOrEmpty(start)) start = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(end)) end = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            // 3. ส่งค่ากลับไปให้ View (ป้องกัน CS0103 และใช้แสดงผลใน Dropdown)
            ViewBag.Start = start;
            ViewBag.End = end;
            // ส่งค่ากลับไปที่ View เพื่อให้ Dropdown จำค่าได้
            ViewBag.CurrentStatus = Status;
            // อย่าลืมส่งค่าอื่นไปด้วยเพื่อให้ช่องอื่นไม่คืนค่า (ถ้ายังไม่ได้ทำ)
            ViewBag.CurrentStart = start;
            ViewBag.CurrentEnd = end;
            ViewBag.CurrentJobType = jobtype;
            ViewBag.CurrentSectionFrom = sectionFrom;
            ViewBag.CurrentSectionTo = sectionTo;
            ViewBag.SelectedStatus = Status;

            // 4. โหลด Master Data สำหรับ Dropdown
            var jsonRaw = System.IO.File.ReadAllText("wwwroot/data/section.json");
            var sectionMaster = JsonSerializer.Deserialize<List<Models.SectionApiModels>>(jsonRaw) ?? new();
            ViewBag.SectionList = sectionMaster.OrderBy(x => x.section).ToList();

            var jsonstatus = System.IO.File.ReadAllText("wwwroot/data/status.json");
            ViewBag.StatusList = JsonSerializer.Deserialize<List<Models.StatusApiModels>>(jsonstatus) ?? new();

            // 5. เตรียม API URL
            var queryParams = new Dictionary<string, string?> // เติม ? ตรงนี้
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

            // 6. เรียกใช้งาน API
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("username", sessionUser);
                client.DefaultRequestHeaders.Add("password", sessionPass);

                var response = await client.GetAsync(apiUrl);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    data = Newtonsoft.Json.JsonConvert.DeserializeObject<List<MaintenanceApiModels>>(json) ?? new();
                }
            }

            // 7. 🔥 Filter Section Range (ช่วงหน่วยงาน) - ป้องกัน NullReference
            if (!string.IsNullOrEmpty(sectionFrom) || !string.IsNullOrEmpty(sectionTo))
            {
                data = data.Where(x =>
                {
                    // ตรวจสอบฟิลด์หน่วยงาน (ใช้ทั้ง sectioncreate หรือ section เผื่อ API ส่งมาตัวใดตัวหนึ่ง)
                    var target = (x?.sectioncreate ?? x?.section ?? "").Trim();
                    if (string.IsNullOrEmpty(target)) return false;

                    bool matchFrom = string.IsNullOrEmpty(sectionFrom) || string.Compare(target, sectionFrom.Trim()) >= 0;
                    bool matchTo = string.IsNullOrEmpty(sectionTo) || string.Compare(target, sectionTo.Trim()) <= 0;

                    return matchFrom && matchTo;
                }).ToList();
            }

            // 8. Filter Status (ถ้ามีการเลือก)
            if (!string.IsNullOrEmpty(Status))
            {
                data = data.Where(x => !string.IsNullOrEmpty(x.status) &&
                                        x.status.Equals(Status, StringComparison.OrdinalIgnoreCase))
                            .ToList();
            }

            ViewBag.DebugUrl = apiUrl;
            return View(data);
        }
    }
}