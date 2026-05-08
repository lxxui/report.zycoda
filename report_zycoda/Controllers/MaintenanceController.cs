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


        public async Task<IActionResult> Index(string jobtype, string start, string end, string sectionFrom, string sectionTo, string Status, string view = "followup")
        {
            if (User.Identity == null || !User.Identity.IsAuthenticated)
                return RedirectToAction("Login", "Home");

            var sessionUser = User.Identity.Name;
            var sessionPass = User.Claims.FirstOrDefault(c => c.Type == "ApiPassword")?.Value;
            var culture = CultureInfo.InvariantCulture;

            // --- 1. SET DEFAULT DATE (1/1/2024 - TODAY) ---
            var todayStr = DateTime.Now.ToString("yyyy-MM-dd", culture);
            var defaultStart = "2024-01-01";

            start = string.IsNullOrEmpty(start) ? defaultStart : start;
            end = string.IsNullOrEmpty(end) ? todayStr : end;

            // Parse วันที่สำหรับใช้ใน Logic (ต้องเป๊ะ)
            DateTime.TryParseExact(start, "yyyy-MM-dd", culture, DateTimeStyles.None, out var startDate);
            DateTime.TryParseExact(end, "yyyy-MM-dd", culture, DateTimeStyles.None, out var endDate);

            // --- 2. VIEW STATE (ค้างค่าหน้าจอ) ---
            ViewBag.CurrentStart = start;
            ViewBag.CurrentEnd = end;
            ViewBag.SelectedStatus = Status?.Trim();
            ViewBag.CurrentJobType = jobtype ?? "EM,CM";

            ViewBag.CurrentView = view ?? "followup";
            ViewBag.CurrentSectionFrom = sectionFrom;
            ViewBag.CurrentSectionTo = sectionTo;


            // --- 3. MASTER DATA (โหลดแยกกันเพื่อความชัวร์) ---

            // 1. โหลดข้อมูล Section
            try
            {
                var sections = await _apiService.GetSectionsFromApiAsync();
                ViewBag.SectionList = sections?.OrderBy(x => x.Sections).ToList() ?? new List<SectionApiModels>();
            }
            catch (Exception ex)
            {
                ViewBag.SectionList = new List<SectionApiModels>();
                System.Diagnostics.Debug.WriteLine($"Section API Error: {ex.Message}");
            }

            // 2. โหลดข้อมูล Status
            try
            {
                var statuses = await _apiService.GetStatusesFromApiAsync();
                ViewBag.StatusList = statuses?.OrderBy(x => x.StatusId).ToList() ?? new List<StatusApiModels>();
            }
            catch (Exception ex)
            {
                ViewBag.StatusList = new List<StatusApiModels>();
                System.Diagnostics.Debug.WriteLine($"Status API Error: {ex.Message}");
            }


            // --- 4. API CALL ---
            var combined = new List<MaintenanceApiModels>();
            var viewsToCall = view == "all" ? new List<string> { "followup", "myjob" } : new List<string> { view };

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("username", sessionUser);
                client.DefaultRequestHeaders.Add("password", sessionPass);
                string jobTypeToQuery = string.IsNullOrEmpty(jobtype) ? "EM,CM" : jobtype;
                foreach (var v in viewsToCall)
                {
                    var url = QueryHelpers.AddQueryString("https://farmhouse.zycoda.com/apimpros/get_job_order", new Dictionary<string, string?>
                    {
                        ["plant"] = "FARMHOUSE",
                        ["jobtype"] = jobTypeToQuery,
                        ["start"] = start,
                        ["end"] = end,
                        ["view"] = v,
                        ["v"] = "all"
                    });
                    try
                    {
                        var res = await client.GetAsync(url);
                        var json = await res.Content.ReadAsStringAsync();
                        if (!string.IsNullOrWhiteSpace(json) && json.StartsWith("["))
                        {
                            var data = JsonConvert.DeserializeObject<List<MaintenanceApiModels>>(json);
                            if (data != null) combined.AddRange(data);
                        }
                    }
                    catch (Exception ex) { Console.WriteLine($"API Error: {ex.Message}"); }
                }
            }

            // --- 5. FILTER SAFE (THE ACCURACY CORE) ---
            // กรองซ้ำเพื่อให้แน่ใจว่าได้ข้อมูลตรงตามเงื่อนไขเป๊ะๆ
            // --- 5. FILTER SAFE (ปรับปรุงใหม่ให้ตรงกับ API) ---
            var final = combined.Where(x => {
                // 1. กรองวันที่: ถ้า API ส่งมาและ Parse ได้ ให้กรองตามวันที่
                bool dateMatch = true;
                if (!string.IsNullOrEmpty(x.timecreate))
                {
                    if (DateTime.TryParse(x.timecreate, out var dt))
                    {
                        dateMatch = dt.Date >= startDate.Date && dt.Date <= endDate.Date;
                    }
                    // ถ้า Parse วันที่ไม่ได้ เราจะไม่เตะทิ้ง (ให้ dateMatch = true ไว้ก่อน) เพื่อให้ข้อมูลแสดงออกมา
                }

                // 2. กรอง Status: รองรับทั้งค่า Null และ "All"
                bool statusMatch = string.IsNullOrEmpty(Status) ||
                                   Status.Equals("All", StringComparison.OrdinalIgnoreCase) ||
                                   (x.status != null && x.status.Trim().Equals(Status.Trim(), StringComparison.OrdinalIgnoreCase));

                // 3. กรอง Section: ปรับให้ยืดหยุ่นขึ้น
                bool sectionMatch = true;
                var sec = (x.sectioncreate ?? x.section ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(sectionFrom) || !string.IsNullOrWhiteSpace(sectionTo))
                {
                    bool okFrom = string.IsNullOrWhiteSpace(sectionFrom) || string.Compare(sec, sectionFrom.Trim(), true) >= 0;
                    bool okTo = string.IsNullOrWhiteSpace(sectionTo) || string.Compare(sec, sectionTo.Trim(), true) <= 0;
                    sectionMatch = okFrom && okTo;
                }

                return dateMatch && statusMatch && sectionMatch;
            }).ToList();

            // เรียงลำดับตามเลขงานล่าสุดเสมอ
            return View(final.OrderByDescending(x => x.job_no).ToList());
        }
    }
}