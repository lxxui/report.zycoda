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

        // ==================================================
        // HELPER: เรียก API แล้วคืน List<MaintenanceApiModels>
        // ==================================================
        private async Task<List<MaintenanceApiModels>> FetchJobsFromApi(
            string sessionUser, string sessionPass,
            string jobtype, string start, string end,
            string view = "followup")
        {
            var combined = new List<MaintenanceApiModels>();
            var viewsToCall = (view == "all")
                ? new List<string> { "followup", "myjob" }
                : new List<string> { view };

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("username", sessionUser);
            client.DefaultRequestHeaders.Add("password", sessionPass);

            foreach (var v in viewsToCall)
            {
                var queryParams = new Dictionary<string, string?>
                {
                    ["plant"] = "FARMHOUSE",
                    ["jobtype"] = Uri.EscapeDataString(string.IsNullOrEmpty(jobtype) ? "EM,CM" : jobtype),
                    ["start"] = start,
                    ["end"] = end,
                    ["view"] = v
                };

                var url = QueryHelpers.AddQueryString("https://farmhouse.zycoda.com/apimpros/get_job_order", queryParams);
                System.Diagnostics.Debug.WriteLine($">>> [URL] {url}");

                try
                {
                    var res = await client.GetAsync(url);
                    var json = await res.Content.ReadAsStringAsync();

                    System.Diagnostics.Debug.WriteLine($">>> [STATUS] {v} = {res.StatusCode}");
                    System.Diagnostics.Debug.WriteLine($">>> [BODY] {json.Substring(0, Math.Min(500, json.Length))}");

                    if (res.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(json))
                    {
                        if (json.Trim().StartsWith("{")) json = "[" + json + "]";

                        var data = JsonConvert.DeserializeObject<List<MaintenanceApiModels>>(json);
                        if (data != null)
                        {
                            System.Diagnostics.Debug.WriteLine($">>> [DATA] {v} ได้ {data.Count} records");
                            combined.AddRange(data);
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"🔴 {v} API Error: {res.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ {v} Exception: {ex.Message}");
                }
            }

            return combined;
        }

        // ==================================================
        // INDEX: หน้าหลัก
        // ==================================================
        public async Task<IActionResult> Index(string jobtype, string start, string end, string sectionFrom, string sectionTo, string status, string view = "followup")
        {
            if (User.Identity == null || !User.Identity.IsAuthenticated)
                return RedirectToAction("Login", "Home");

            var sessionUser = User.Identity.Name;
            var sessionPass = User.Claims.FirstOrDefault(c => c.Type == "ApiPassword")?.Value;
            var culture = CultureInfo.InvariantCulture;

            System.Diagnostics.Debug.WriteLine($">>> [AUTH] sessionUser={sessionUser} | sessionPass={sessionPass}");

            // --- 1. SET DATE ---
            var todayStr = DateTime.Now.ToString("yyyy-MM-dd", culture);
            start = string.IsNullOrEmpty(start) ? "2024-01-01" : start;
            end = string.IsNullOrEmpty(end) ? todayStr : end;

            DateTime.TryParseExact(start, "yyyy-MM-dd", culture, DateTimeStyles.None, out var startDate);
            DateTime.TryParseExact(end, "yyyy-MM-dd", culture, DateTimeStyles.None, out var endDate);

            // --- 2. VIEW STATE ---
            ViewBag.CurrentStart = start;
            ViewBag.CurrentEnd = end;
            ViewBag.SelectedStatus = status;
            ViewBag.CurrentJobType = jobtype ?? "EM,CM";
            ViewBag.CurrentView = view ?? "followup";
            ViewBag.CurrentSectionFrom = sectionFrom;
            ViewBag.CurrentSectionTo = sectionTo;

            // --- 3. MASTER DATA ---
            try
            {
                var sections = await _apiService.GetSectionsFromApiAsync();
                ViewBag.SectionList = sections?.OrderBy(x => x.Sections).ToList() ?? new List<SectionApiModels>();
                var statuses = await _apiService.GetStatusesFromApiAsync();
                ViewBag.StatusList = statuses?.OrderBy(x => x.StatusId).ToList() ?? new List<StatusApiModels>();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Master Error: {ex.Message}"); }

            // --- 4. API CALL ---
            var combined = await FetchJobsFromApi(sessionUser, sessionPass, jobtype, start, end, view);

            System.Diagnostics.Debug.WriteLine($">>> [BEFORE FILTER] combined = {combined.Count} records");

            // --- 5. FILTER ---
            var final = combined.Where(x => {
                bool dateMatch = true;
                if (!string.IsNullOrEmpty(x.timecreate) && DateTime.TryParse(x.timecreate, out var dt))
                    dateMatch = dt.Date >= startDate.Date && dt.Date <= endDate.Date;

                bool statusMatch = string.IsNullOrEmpty(status) || status.Equals("All", StringComparison.OrdinalIgnoreCase) ||
                                   (x.status != null && x.status.Trim().Equals(status.Trim(), StringComparison.OrdinalIgnoreCase));

                var sec = (x.sectioncreate ?? x.section ?? "").Trim();
                bool sectionMatch = true;
                if (!string.IsNullOrWhiteSpace(sectionFrom) || !string.IsNullOrWhiteSpace(sectionTo))
                {
                    bool okFrom = string.IsNullOrWhiteSpace(sectionFrom) || string.Compare(sec, sectionFrom.Trim(), true) >= 0;
                    bool okTo = string.IsNullOrWhiteSpace(sectionTo) || string.Compare(sec, sectionTo.Trim(), true) <= 0;
                    sectionMatch = okFrom && okTo;
                }
                return dateMatch && statusMatch && sectionMatch;
            }).ToList();

            System.Diagnostics.Debug.WriteLine($">>> [AFTER FILTER] final = {final.Count} records");

            ViewBag.RawData = JsonConvert.SerializeObject(final);
            return View(final.OrderByDescending(x => x.job_no).ToList());
        }

        // ==================================================
        // TODAY JOB ENTRY: หน้างานประจำวัน (รับ parameter เหมือน Index)
        // ==================================================
        public async Task<IActionResult> TodayJobEntry(string jobtype, string start, string end, string sectionFrom, string sectionTo, string status, string view = "all")
        {
            if (User.Identity == null || !User.Identity.IsAuthenticated)
                return RedirectToAction("Login", "Home");

            var sessionUser = User.Identity.Name;
            var sessionPass = User.Claims.FirstOrDefault(c => c.Type == "ApiPassword")?.Value;
            var culture = CultureInfo.InvariantCulture;
            var todayStr = DateTime.Now.ToString("yyyy-MM-dd", culture);

            // default วันนี้วันเดียว
            start = string.IsNullOrEmpty(start) ? todayStr : start;
            end = string.IsNullOrEmpty(end) ? todayStr : end;

            DateTime.TryParseExact(start, "yyyy-MM-dd", culture, DateTimeStyles.None, out var startDate);
            DateTime.TryParseExact(end, "yyyy-MM-dd", culture, DateTimeStyles.None, out var endDate);

            // --- VIEW STATE ---
            ViewBag.CurrentStart = start;
            ViewBag.CurrentEnd = end;
            ViewBag.SelectedStatus = status;
            ViewBag.CurrentJobType = jobtype ?? "EM,CM";
            ViewBag.CurrentView = view;
            ViewBag.CurrentSectionFrom = sectionFrom;
            ViewBag.CurrentSectionTo = sectionTo;

            // --- MASTER DATA ---
            try
            {
                var sections = await _apiService.GetSectionsFromApiAsync();
                ViewBag.SectionList = sections?.OrderBy(x => x.Sections).ToList() ?? new List<SectionApiModels>();
                var statuses = await _apiService.GetStatusesFromApiAsync();
                ViewBag.StatusList = statuses?.OrderBy(x => x.StatusId).ToList() ?? new List<StatusApiModels>();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Master Error: {ex.Message}"); }

            // --- API CALL ---
            var combined = await FetchJobsFromApi(sessionUser, sessionPass, jobtype, start, end, view);

            // --- FILTER ---
            var final = combined.Where(x => {
                bool dateMatch = true;
                if (!string.IsNullOrEmpty(x.timecreate) && DateTime.TryParse(x.timecreate, out var dt))
                    dateMatch = dt.Date >= startDate.Date && dt.Date <= endDate.Date;

                bool statusMatch = string.IsNullOrEmpty(status) || status.Equals("All", StringComparison.OrdinalIgnoreCase) ||
                                   (x.status != null && x.status.Trim().Equals(status.Trim(), StringComparison.OrdinalIgnoreCase));

                var sec = (x.sectioncreate ?? x.section ?? "").Trim();
                bool sectionMatch = true;
                if (!string.IsNullOrWhiteSpace(sectionFrom) || !string.IsNullOrWhiteSpace(sectionTo))
                {
                    bool okFrom = string.IsNullOrWhiteSpace(sectionFrom) || string.Compare(sec, sectionFrom.Trim(), true) >= 0;
                    bool okTo = string.IsNullOrWhiteSpace(sectionTo) || string.Compare(sec, sectionTo.Trim(), true) <= 0;
                    sectionMatch = okFrom && okTo;
                }
                return dateMatch && statusMatch && sectionMatch;
            }).ToList();

            System.Diagnostics.Debug.WriteLine($">>> [TODAY JOB ENTRY] final = {final.Count} records");

            return View(final.OrderByDescending(x => x.job_no).ToList());
        }

        // ==================================================
        // GET STATUS UPDATE: สำหรับเช็คใบงานใหม่ทุก 30 วินาที
        // ==================================================
        public async Task<IActionResult> GetStatusUpdate()
        {
            if (User.Identity == null || !User.Identity.IsAuthenticated)
                return Unauthorized();

            var sessionUser = User.Identity.Name;
            var sessionPass = User.Claims.FirstOrDefault(c => c.Type == "ApiPassword")?.Value;
            var culture = CultureInfo.InvariantCulture;
            var todayStr = DateTime.Now.ToString("yyyy-MM-dd", culture);
            var startStr = DateTime.Now.AddDays(-30).ToString("yyyy-MM-dd", culture);

            var combined = await FetchJobsFromApi(sessionUser, sessionPass, "EM,CM", startStr, todayStr, "followup");

            var result = combined
                .OrderByDescending(x => x.job_no)
                .Take(1)
                .Select(x => new { jobNo = x.job_no, status = x.status })
                .ToList();

            return Json(result);
        }
    }
}
