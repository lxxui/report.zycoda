using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using report_zycoda.Data;
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

        public async Task<IActionResult> Index(string jobtype,string start,string end,string sectionFrom,string sectionTo,string Status,string view = "all")
        {
            // 1. ตรวจสอบสิทธิ์การเข้าใช้งาน
            if (User.Identity == null || !User.Identity.IsAuthenticated)
                return RedirectToAction("Login", "Home");

            var sessionUser = User.Identity.Name;
            var sessionPass = User.Claims.FirstOrDefault(c => c.Type == "ApiPassword")?.Value;

            // 2. ตั้งค่า Default Parameters & Culture
            var culture = CultureInfo.InvariantCulture;

            if (string.IsNullOrEmpty(start))
            {
                start = DateTime.Now.AddYears(-2).ToString("yyyy-01-01", culture);
            }

            if (string.IsNullOrEmpty(end))
            {
                end = DateTime.Now.ToString("yyyy-MM-dd", culture);
            }

            if (string.IsNullOrEmpty(jobtype)) jobtype = "EM,CM";
            if (string.IsNullOrEmpty(view)) view = "all";

            // เก็บค่าส่งกลับไปหน้า View เพื่อรักษาค่าใน Form (สำคัญมาก: UI จะโชว์วันที่ตามนี้)
            ViewBag.CurrentJobType = jobtype;
            ViewBag.CurrentView = view;
            ViewBag.CurrentSectionFrom = sectionFrom;
            ViewBag.CurrentSectionTo = sectionTo;
            ViewBag.SelectedStatus = Status;
            // ตรวจสอบว่าถ้ามีค่า ให้แปลงเป็น format ที่ HTML date input รู้จัก
            ViewBag.CurrentStart = !string.IsNullOrEmpty(start)
                ? DateTime.Parse(start).ToString("yyyy-MM-dd", culture)
                : DateTime.Now.AddYears(-2).ToString("yyyy-01-01", culture);

            ViewBag.CurrentEnd = !string.IsNullOrEmpty(end)
                ? DateTime.Parse(end).ToString("yyyy-MM-dd", culture)
                : DateTime.Now.ToString("yyyy-MM-dd", culture);


            // 3. ดึง Master Data สำหรับ Dropdown
            try
            {
                var sectionMaster = await _apiService.GetSectionsFromApiAsync();
                ViewBag.SectionList = sectionMaster?.OrderBy(x => x.Sections).ToList() ?? new List<SectionApiModels>();
                var statusMaster = await _apiService.GetStatusesFromApiAsync();
                ViewBag.StatusList = statusMaster?.OrderBy(x => x.StatusId).ToList() ?? new List<StatusApiModels>();
            }
            catch { }

            // 4. การดึงข้อมูลจาก API
            List<MaintenanceApiModels> combinedData = new();
            var targetViews = (view == "all") ? new List<string> { "followup", "myjob" } : new List<string> { view };

            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("username", sessionUser);
                client.DefaultRequestHeaders.Add("password", sessionPass);

                foreach (var vTarget in targetViews)
                {
                    var queryParams = new Dictionary<string, string?>
            {
                { "plant", "FARMHOUSE" },
                { "jobtype", jobtype },
                { "start", start },
                { "end", end },
                { "view", vTarget },
                { "v", "all" }
            };

                    var baseUrl = "https://farmhouse.zycoda.com/apimpros/get_job_order";
                    var apiUrl = QueryHelpers.AddQueryString(baseUrl, queryParams);

                    try
                    {
                        var response = await client.GetAsync(apiUrl);
                        if (response.IsSuccessStatusCode)
                        {
                            var json = await response.Content.ReadAsStringAsync();
                            if (!string.IsNullOrEmpty(json) && json.Trim().StartsWith("["))
                            {
                                var data = JsonConvert.DeserializeObject<List<MaintenanceApiModels>>(json);
                                if (data != null) combinedData.AddRange(data);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ViewBag.ApiError = $"API Error ({vTarget}): {ex.Message}";
                    }
                }
            }

            // 5. จัดการข้อมูลก่อนแสดงผล (In-Memory Filtering)
            var finalData = combinedData;

            // กรองตามหน่วยงาน (Section)
            if (!string.IsNullOrEmpty(sectionFrom) || !string.IsNullOrEmpty(sectionTo))
            {
                finalData = finalData.Where(x =>
                {
                    var target = (x?.sectioncreate ?? x?.section ?? "").Trim();
                    if (string.IsNullOrEmpty(target)) return true;

                    bool matchFrom = string.IsNullOrEmpty(sectionFrom) || string.Compare(target, sectionFrom.Trim()) >= 0;
                    bool matchTo = string.IsNullOrEmpty(sectionTo) || string.Compare(target, sectionTo.Trim()) <= 0;
                    return matchFrom && matchTo;
                }).ToList();
            }

            // กรองตามสถานะ (Status)
            if (!string.IsNullOrEmpty(Status))
            {
                finalData = finalData.Where(x =>
                    x.status != null &&
                    x.status.Trim().Equals(Status.Trim(), StringComparison.OrdinalIgnoreCase)
                ).ToList();
            }

            // 6. ส่งข้อมูลไปที่ View โดยเรียงตาม Job Number ล่าสุด
            // แนะนำ: ถ้าข้อมูลเยอะมาก ฝ้ายสามารถใช้ .Take(500) ต่อท้ายเพื่อป้องกัน UI ค้างได้นะจ๊ะ
            return View(finalData.OrderByDescending(x => x.job_no).ToList());
        }

    }
}