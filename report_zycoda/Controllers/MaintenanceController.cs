using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Newtonsoft.Json;
using report_zycoda.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

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
        // HELPER: ดึงข้อมูลรอบเดียวหมดเลย ไม่แบ่ง Chunk 7 วัน
        // ==================================================
        private async Task<List<MaintenanceApiModels>> FetchJobsFromApi(
            string sessionUser,
            string sessionPass,
            string jobtype,
            string start,
            string end,
            string view = "followup")
        {
            var combined = new List<MaintenanceApiModels>();
            var culture = CultureInfo.InvariantCulture;

            // ตรวจสอบสตริงวันที่และแปลงเป็น DateTime (เพื่อทำความสะอาด Format ให้แน่ใจว่าเป็น yyyy-MM-dd)
            if (!DateTime.TryParseExact(start, "yyyy-MM-dd", culture, DateTimeStyles.None, out var globalStart))
            {
                if (!DateTime.TryParse(start, culture, DateTimeStyles.None, out globalStart))
                {
                    globalStart = new DateTime(2026, 01, 01);
                }
            }
            if (!DateTime.TryParseExact(end, "yyyy-MM-dd", culture, DateTimeStyles.None, out var globalEnd))
            {
                if (!DateTime.TryParse(end, culture, DateTimeStyles.None, out globalEnd))
                {
                    globalEnd = DateTime.Now;
                }
            }

            string finalStartStr = globalStart.ToString("yyyy-MM-dd", culture);
            string finalEndStr = globalEnd.ToString("yyyy-MM-dd", culture);
            var finalJobType = string.IsNullOrWhiteSpace(jobtype) ? "EM,CM" : jobtype;

            // ปรับจากลูปเปลี่ยนเป็นเรียกตามค่า view ที่ส่งมาตรงๆ (หรือถ้า 'all' ก็ให้เรียกดึงทั้ง 2 view มารวมกัน)
            var viewsToCall = (view == "all") ? new List<string> { "followup", "myjob" } : new List<string> { view };

            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            };

            using var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromMinutes(5); // ตั้งไว้ 5 นาทีเผื่อกรณีข้อมูลก้อนใหญ่มาก
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("accept", "application/json");
            client.DefaultRequestHeaders.Add("username", sessionUser);
            client.DefaultRequestHeaders.Add("password", sessionPass);

            foreach (var v in viewsToCall)
            {
                var queryParams = new Dictionary<string, string?>
                {
                    ["plant"] = "FARMHOUSE",
                    ["jobtype"] = finalJobType,
                    ["start"] = finalStartStr,
                    ["end"] = finalEndStr,
                    ["view"] = v
                };

                var url = QueryHelpers.AddQueryString("https://farmhouse.zycoda.com/apimpros/get_job_order", queryParams);
                System.Diagnostics.Debug.WriteLine($"[SINGLE API CALL] 📨 Range: {finalStartStr} to {finalEndStr} | View: {v} | URL: {url}");

                try
                {
                    var response = await client.GetAsync(url);
                    var json = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(json))
                        continue;

                    var trimmedJson = json.Trim();

                    if (trimmedJson.StartsWith("["))
                    {
                        var data = JsonConvert.DeserializeObject<List<MaintenanceApiModels>>(trimmedJson);
                        if (data != null) combined.AddRange(data);
                    }
                    else if (trimmedJson.StartsWith("{"))
                    {
                        var singleData = JsonConvert.DeserializeObject<MaintenanceApiModels>(trimmedJson);
                        if (singleData != null) combined.Add(singleData);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"   ❌ API ERROR [{v}]: {ex.Message}");
                }
            }

            // ตัดตัวซ้ำออก เผื่อกรณีดึง 'all' (followup + myjob) แล้วได้งาน ID ซ้ำกันมา
            combined = combined.GroupBy(x => x.id).Select(g => g.First()).ToList();
            System.Diagnostics.Debug.WriteLine($"[FETCH COMPLETE] 📊 ยอดรวมข้อมูลดิบทั้งหมดที่ดึงได้: {combined.Count} รายการ");

            return combined;
        }

        // ==================================================
        // INDEX: หน้าหลัก
        // ==================================================
        public async Task<IActionResult> Index()
        {
            if (User.Identity == null || !User.Identity.IsAuthenticated)
                return RedirectToAction("Login", "Home");

            var sessionUser = User.Identity.Name;
            var sessionPass = User.Claims.FirstOrDefault(c => c.Type == "ApiPassword")?.Value;
            var culture = CultureInfo.InvariantCulture;

            string jobtype = Request.Query.FirstOrDefault(q => q.Key.Equals("jobtype", StringComparison.OrdinalIgnoreCase)).Value.ToString();
            string start = Request.Query.FirstOrDefault(q => q.Key.Equals("start", StringComparison.OrdinalIgnoreCase)).Value.ToString();
            string end = Request.Query.FirstOrDefault(q => q.Key.Equals("end", StringComparison.OrdinalIgnoreCase)).Value.ToString();
            string sectionFrom = Request.Query.FirstOrDefault(q => q.Key.Equals("sectionFrom", StringComparison.OrdinalIgnoreCase)).Value.ToString();
            string sectionTo = Request.Query.FirstOrDefault(q => q.Key.Equals("sectionTo", StringComparison.OrdinalIgnoreCase)).Value.ToString();
            string status = Request.Query.FirstOrDefault(q => q.Key.Equals("status", StringComparison.OrdinalIgnoreCase)).Value.ToString();
            string view = Request.Query.FirstOrDefault(q => q.Key.Equals("view", StringComparison.OrdinalIgnoreCase)).Value.ToString();

            var todayStr = DateTime.Now.ToString("yyyy-MM-dd", culture);
            var defaultStartStr = DateTime.Now.AddDays(-30).ToString("yyyy-MM-dd", culture);

            string finalStart = string.IsNullOrWhiteSpace(start) ? defaultStartStr : start.Trim();
            string finalEnd = string.IsNullOrWhiteSpace(end) ? todayStr : end.Trim();

            ViewBag.CurrentStart = finalStart;
            ViewBag.CurrentEnd = finalEnd;
            ViewBag.SelectedStatus = string.IsNullOrWhiteSpace(status) ? "all" : status.Trim();
            ViewBag.CurrentJobType = string.IsNullOrWhiteSpace(jobtype) ? "EM,CM" : jobtype.Trim();
            ViewBag.CurrentView = string.IsNullOrWhiteSpace(view) ? "followup" : view.Trim();
            ViewBag.CurrentSectionFrom = sectionFrom?.Trim();
            ViewBag.CurrentSectionTo = sectionTo?.Trim();

            await LoadMasterDataAsync();

            var combined = await FetchJobsFromApi(sessionUser, sessionPass, ViewBag.CurrentJobType, finalStart, finalEnd, ViewBag.CurrentView);
            var final = FilterAndOrderJobs(combined, ViewBag.SelectedStatus, ViewBag.CurrentSectionFrom, ViewBag.CurrentSectionTo);

            ViewBag.RawData = JsonConvert.SerializeObject(final);

            return View(final);
        }

        // ==================================================
        // TODAY JOB ENTRY
        // ==================================================
        public async Task<IActionResult> TodayJobEntry()
        {
            if (User.Identity == null || !User.Identity.IsAuthenticated)
                return RedirectToAction("Login", "Home");

            var sessionUser = User.Identity.Name;
            var sessionPass = User.Claims.FirstOrDefault(c => c.Type == "ApiPassword")?.Value;
            var culture = CultureInfo.InvariantCulture;
            var todayStr = DateTime.Now.ToString("yyyy-MM-dd", culture);

            string jobtype = Request.Query["jobtype"].ToString();
            string start = Request.Query["start"].ToString();
            if (string.IsNullOrEmpty(start)) start = Request.Query["Start"].ToString();
            string end = Request.Query["end"].ToString();
            if (string.IsNullOrEmpty(end)) end = Request.Query["End"].ToString();
            string sectionFrom = Request.Query["sectionFrom"].ToString();
            string sectionTo = Request.Query["sectionTo"].ToString();
            string status = Request.Query["status"].ToString();
            if (string.IsNullOrEmpty(status)) status = Request.Query["Status"].ToString();
            string view = Request.Query["view"].ToString();

            string finalStart = string.IsNullOrEmpty(start) ? todayStr : start;
            string finalEnd = string.IsNullOrEmpty(end) ? todayStr : end;

            ViewBag.CurrentStart = finalStart;
            ViewBag.CurrentEnd = finalEnd;
            ViewBag.SelectedStatus = status;
            ViewBag.CurrentJobType = string.IsNullOrEmpty(jobtype) ? "EM,CM" : jobtype;
            ViewBag.CurrentView = string.IsNullOrEmpty(view) ? "all" : view;
            ViewBag.CurrentSectionFrom = sectionFrom;
            ViewBag.CurrentSectionTo = sectionTo;

            await LoadMasterDataAsync();

            var combined = await FetchJobsFromApi(sessionUser, sessionPass, ViewBag.CurrentJobType, finalStart, finalEnd, ViewBag.CurrentView);
            var final = FilterAndOrderJobs(combined, status, sectionFrom, sectionTo);

            return View(final);
        }

        public async Task<IActionResult> GetStatusUpdate()
        {
            if (User.Identity == null || !User.Identity.IsAuthenticated) return Unauthorized();
            var sessionUser = User.Identity.Name;
            var sessionPass = User.Claims.FirstOrDefault(c => c.Type == "ApiPassword")?.Value;
            var culture = CultureInfo.InvariantCulture;

            var todayStr = DateTime.Now.ToString("yyyy-MM-dd", culture);
            var startStr = DateTime.Now.AddDays(-2).ToString("yyyy-MM-dd", culture);

            var combined = await FetchJobsFromApi(sessionUser, sessionPass, "EM,CM", startStr, todayStr, "followup");
            var result = combined.OrderByDescending(x => x.id).Take(1).Select(x => new { jobNo = x.id, status = x.status }).ToList();
            return Json(result);
        }

        private async Task LoadMasterDataAsync()
        {
            try
            {
                var sections = await _apiService.GetSectionsFromApiAsync();
                ViewBag.SectionList = sections?.OrderBy(x => x.Sections).ToList() ?? new List<SectionApiModels>();
                var statuses = await _apiService.GetStatusesFromApiAsync();
                ViewBag.StatusList = statuses?.OrderBy(x => x.StatusId).ToList() ?? new List<StatusApiModels>();
            }
            catch
            {
                ViewBag.SectionList = new List<SectionApiModels>();
                ViewBag.StatusList = new List<StatusApiModels>();
            }
        }

        // ==================================================
        // PRIVATE FILTER LOGIC
        // ==================================================
        private List<MaintenanceApiModels> FilterAndOrderJobs(List<MaintenanceApiModels> combined, string status, string sectionFrom, string sectionTo)
        {
            string searchStatus = (status ?? "").Trim().ToLower();
            string searchSecFrom = (sectionFrom ?? "").Trim().ToLower();
            string searchSecTo = (sectionTo ?? "").Trim().ToLower();

            return combined.Where(x => {
                bool statusMatch = true;
                if (!string.IsNullOrEmpty(searchStatus) &&
                    !searchStatus.Equals("all") &&
                    !searchStatus.Equals("ทั้งหมด") &&
                    !searchStatus.Equals("เลือกสถานะ"))
                {
                    string currentStatus = (x.status ?? "").Trim().ToLower();
                    string currentStatusText = (x.statustext ?? "").Trim().ToLower();

                    statusMatch = currentStatus.Contains(searchStatus) ||
                                  currentStatusText.Contains(searchStatus) ||
                                  searchStatus.Contains(currentStatus);
                }

                bool sectionMatch = true;
                if (!string.IsNullOrWhiteSpace(searchSecFrom) || !string.IsNullOrWhiteSpace(searchSecTo))
                {
                    var sec = (x.sectioncreate ?? x.section ?? "").Trim().ToLower();

                    if (string.IsNullOrEmpty(sec))
                    {
                        sectionMatch = false;
                    }
                    else
                    {
                        bool matchFrom = true;
                        if (!string.IsNullOrWhiteSpace(searchSecFrom))
                        {
                            matchFrom = sec.Contains(searchSecFrom) || string.Compare(sec, searchSecFrom, StringComparison.OrdinalIgnoreCase) >= 0;
                        }

                        bool matchTo = true;
                        if (!string.IsNullOrWhiteSpace(searchSecTo))
                        {
                            matchTo = sec.Contains(searchSecTo) || string.Compare(sec, searchSecTo, StringComparison.OrdinalIgnoreCase) <= 0;
                        }

                        sectionMatch = matchFrom && matchTo;
                    }
                }

                return statusMatch && sectionMatch;
            })
            .OrderByDescending(x => x.id)
            .ToList();
        }


        [HttpGet]
        public IActionResult Machine()
        {
            // 🟢 คอมเมนต์หรือลบการเช็คสิทธิ์ Class 9 ออกชั่วคราว เพื่อบังคับให้เข้าหน้าเว็บได้ทันที
            /*
            var userClass = User.Claims.FirstOrDefault(c => c.Type == "UserClass")?.Value;

            if (userClass != "9")
            {
                return RedirectToAction("Index", "Maintenance");
            }
            */

            // ✅ ปล่อยให้ระบบเปิดไฟล์ Views/Maintenance/Machine.cshtml ได้เลยทันที
            return View();
        }
    }
}