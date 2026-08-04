using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Newtonsoft.Json;
using report_zycoda.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using System.Data;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace report_zycoda.Controllers
{
    public class MaintenanceController : Controller
    {
        private readonly ApiService _apiService;
        private readonly string _connectionString;
        private readonly IHttpClientFactory _httpClientFactory;

        public MaintenanceController(ApiService apiService, IConfiguration configuration, IHttpClientFactory httpClientFactory)
        {
            _apiService = apiService;
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
            _httpClientFactory = httpClientFactory;
        }

        // 🛠️ ตัวอย่างฟังก์ชันสำหรับดึงรายการหน่วยงานมาผูกกับพารามิเตอร์ของระบบ Layout กลาง
        private async Task PopulateNavbarDataAsync()
        {
            List<SectionApiModels> sections = await _apiService.GetSectionsAsync();
            ViewBag.SectionList = sections;
        }

        // ==================================================
        // HELPER: ดึงข้อมูลขนานกัน
        // ==================================================

        private async Task<List<MaintenanceApiModels>> FetchJobsFromApi(string sessionUser, string sessionPass, string jobtype, string start, string end, string sessionClass, string sessionSection,
            string view = "followup")
        {
            var culture = CultureInfo.InvariantCulture;

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

            var viewsToCall = (view == "all") ? new List<string> { "followup", "myjob" } : new List<string> { view };

            var uniqueJobs = new ConcurrentDictionary<string, MaintenanceApiModels>();
            var client = _httpClientFactory.CreateClient("ZycodaApiClient");

            var tasks = viewsToCall.Select(async v =>
            {
                var queryParams = new Dictionary<string, string?>
                {
                    ["plant"] = "FARMHOUSE",
                    ["jobtype"] = finalJobType,
                    ["start"] = finalStartStr,
                    ["end"] = finalEndStr,
                    ["view"] = v
                };

                var url = QueryHelpers.AddQueryString("apimpros/get_job_order", queryParams);

                // 🛰️ LOG จุดที่ 1: ดู URL ที่ระบบฝั่งเรากำลังจะยิงออกไปจริงๆ
                Console.WriteLine($"\n🚀 [API REQUEST] BaseUrl: {client.BaseAddress}{url}");
                Console.WriteLine($"🔑 [API AUTH] User: {sessionUser} | Pass Length: {sessionPass?.Length ?? 0}");

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("username", sessionUser);
                request.Headers.Add("password", sessionPass);

                try
                {
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                    // 🛰️ LOG จุดที่ 2: เช็กว่าเซิร์ฟเวอร์ปลายทางตอบกลับมาด้วยสถานะอะไร
                    Console.WriteLine($"📥 [API RESPONSE] View: {v} -> Status: {response.StatusCode} ({(int)response.StatusCode})");

                    if (response.IsSuccessStatusCode)
                    {
                        var jsonString = await response.Content.ReadAsStringAsync();

                        Console.WriteLine($"📦 [API DATA RAW] Length: {jsonString.Length} chars");
                        if (jsonString.Length > 0)
                        {
                            var preview = jsonString.Length > 500 ? jsonString.Substring(0, 500) + "..." : jsonString;
                            Console.WriteLine($"🔍 [API DATA PREVIEW]: {preview}");
                        }

                        if (!string.IsNullOrWhiteSpace(jsonString))
                        {
                            try
                            {
                                // 🎯 ปรับใหม่: ลองถอดรหัสออกมาเป็นแบบ List (Array) ดูก่อนเป็นอันดับแรก
                                var data = JsonConvert.DeserializeObject<List<MaintenanceApiModels>>(jsonString);
                                if (data != null)
                                {
                                    foreach (var item in data)
                                    {
                                        var idKey = item.id?.ToString();
                                        if (!string.IsNullOrEmpty(idKey)) uniqueJobs.TryAdd(idKey, item);
                                    }
                                }
                            }
                            catch (JsonSerializationException)
                            {
                                // 🎯 ถ้าถอดแบบ List ไม่สำเร็จ (เพราะเป็น Object เดี่ยว คีย์ `{}`) ให้สลับมาแกะแบบชิ้นเดียวทันที
                                try
                                {
                                    var singleData = JsonConvert.DeserializeObject<MaintenanceApiModels>(jsonString);
                                    if (singleData != null && !string.IsNullOrEmpty(singleData.id?.ToString()))
                                    {
                                        uniqueJobs.TryAdd(singleData.id.ToString(), singleData);
                                    }
                                }
                                catch (Exception innerEx)
                                {
                                    Console.WriteLine($"❌ [JSON PARSE ERROR]: แกะโครงสร้างไม่ได้ -> {innerEx.Message}");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ [API CRASHED] View: {v} -> Exception: {ex.Message}");
                    Console.WriteLine($"📌 [API CRASHED STACK]: {ex.StackTrace}");
                }
            });

            await Task.WhenAll(tasks);

            // 🛰️ LOG จุดที่ 4: สรุปยอดรวมใบงานสุดท้ายที่กรองและแปลงลง Model สำเร็จก่อนโยนไปหน้า UI
            Console.WriteLine($"📊 [API SUMMARY] Total unique jobs mapped successfully: {uniqueJobs.Count} rows.\n");

            return uniqueJobs.Values.ToList();
        }

        // ==================================================
        // INDEX: หน้าหลัก
        // ==================================================
        public async Task<IActionResult> Index()
        {

            List<string> sectionFromList = Request.Query["sectionFrom"]
        .Where(s => !string.IsNullOrEmpty(s))
        .ToList();

            List<string> sectionToList = Request.Query["sectionTo"]
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            if (User.Identity == null || !User.Identity.IsAuthenticated)
                return RedirectToAction("Login", "Home");

            var sessionUser = User.Identity.Name;
            var sessionPass = User.Claims.FirstOrDefault(c => c.Type == "ApiPassword")?.Value;

            // 🚀 ดึงค่า Class และ Section ที่เราดักไว้ออกมาจาก Claims Principal เพื่อนำไปส่งต่อ
            var sessionClass = User.Claims.FirstOrDefault(c => c.Type == "UserClass")?.Value ?? "0";
            var sessionSection = User.Claims.FirstOrDefault(c => c.Type == "Section")?.Value ?? "";

            var culture = CultureInfo.InvariantCulture;

            string jobtype = Request.Query["jobtype"].ToString();
            string start = Request.Query["start"].ToString();
            string end = Request.Query["end"].ToString();
            string sectionFrom = Request.Query["sectionFrom"].ToString();
            string sectionTo = Request.Query["sectionTo"].ToString();
            string status = Request.Query["status"].ToString();
            string view = Request.Query["view"].ToString();

            var todayStr = DateTime.Now.ToString("yyyy-MM-dd", culture);
            var defaultStartStr = DateTime.Now.AddDays(-7).ToString("yyyy-MM-dd", culture);

            string finalStart = string.IsNullOrWhiteSpace(start) ? defaultStartStr : start.Trim();
            string finalEnd = string.IsNullOrWhiteSpace(end) ? todayStr : end.Trim();

            ViewBag.CurrentStart = finalStart;
            ViewBag.CurrentEnd = finalEnd;
            ViewBag.SelectedStatus = string.IsNullOrWhiteSpace(status) ? "all" : status.Trim();
            ViewBag.CurrentJobType = string.IsNullOrWhiteSpace(jobtype) ? "EM,CM" : jobtype.Trim();
            ViewBag.CurrentView = string.IsNullOrWhiteSpace(view) ? "followup" : view.Trim();
            ViewBag.CurrentSectionFrom = sectionFrom?.Trim();
            ViewBag.CurrentSectionTo = sectionTo?.Trim();

            var masterDataTask = LoadMasterDataAsync();

            // 🚀 ส่งต่อ sessionClass และ sessionSection เข้าไปทำงานขนานในท่อ API
            var apiJobsTask = FetchJobsFromApi(sessionUser, sessionPass, ViewBag.CurrentJobType, finalStart, finalEnd, sessionClass, sessionSection, ViewBag.CurrentView);

            await Task.WhenAll(masterDataTask, apiJobsTask);

            //  สลับกลับมาดึงข้อมูลตรงๆ จาก API Task ที่รันเสร็จแล้วตรงนี้ครับ
            var combined = apiJobsTask.Result;

            var final = FilterAndOrderJobs(combined, ViewBag.SelectedStatus, sectionFromList, sectionToList);

            if (ViewBag.SectionList is List<report_zycoda.Models.SectionApiModels> sectionList)
            {
                ViewBag.EmSectionList = sectionList.Where(x => x.Sections == "EM").ToList();
            }

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

            var sessionClass = User.Claims.FirstOrDefault(c => c.Type == "UserClass")?.Value ?? "0";
            var sessionSection = User.Claims.FirstOrDefault(c => c.Type == "Section")?.Value ?? "";

            var culture = CultureInfo.InvariantCulture;
            var todayStr = DateTime.Now.ToString("yyyy-MM-dd", culture);

            string jobtype = Request.Query["jobtype"].ToString();
            string start = Request.Query["start"].ToString();
            if (string.IsNullOrEmpty(start)) start = Request.Query["Start"].ToString();
            string end = Request.Query["end"].ToString();
            if (string.IsNullOrEmpty(end)) end = Request.Query["End"].ToString();

            // 🔽 เปลี่ยนตรงนี้: อ่านเป็น List<string> แทน string เดี่ยว
            List<string> sectionFrom = Request.Query["sectionFrom"].Where(s => !string.IsNullOrEmpty(s)).ToList();
            List<string> sectionTo = Request.Query["sectionTo"].Where(s => !string.IsNullOrEmpty(s)).ToList();

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

            // 🔽 View ฝั่ง cshtml รอ string คั่น comma อยู่ (currentFromRaw.Split(',')) เลย join กลับก่อนส่ง
            ViewBag.CurrentSectionFrom = string.Join(",", sectionFrom);
            ViewBag.CurrentSectionTo = string.Join(",", sectionTo);

            var masterDataTask = LoadMasterDataAsync();

            var apiJobsTask = FetchJobsFromApi(sessionUser, sessionPass, ViewBag.CurrentJobType, finalStart, finalEnd, sessionClass, sessionSection, ViewBag.CurrentView);

            await Task.WhenAll(masterDataTask, apiJobsTask);

            var final = FilterAndOrderJobs(apiJobsTask.Result, status, sectionFrom, sectionTo);
            return View(final);
        }

        public async Task<IActionResult> GetStatusUpdate()
        {
            if (User.Identity == null || !User.Identity.IsAuthenticated) return Unauthorized();
            var sessionUser = User.Identity.Name;
            var sessionPass = User.Claims.FirstOrDefault(c => c.Type == "ApiPassword")?.Value;
            var sessionClass = User.Claims.FirstOrDefault(c => c.Type == "UserClass")?.Value ?? "0";
            var sessionSection = User.Claims.FirstOrDefault(c => c.Type == "Section")?.Value ?? "";
            var culture = CultureInfo.InvariantCulture;

            var todayStr = DateTime.Now.ToString("yyyy-MM-dd", culture);
            var startStr = DateTime.Now.AddDays(-2).ToString("yyyy-MM-dd", culture);

            // 🚀 ส่งค่า Class และ Section เข้าฟังก์ชันดึงของ Real-time Update
            var combined = await FetchJobsFromApi(sessionUser, sessionPass, "EM,CM", startStr, todayStr, sessionClass, sessionSection, "followup");
            var result = combined.OrderByDescending(x => x.id).Take(1).Select(x => new { jobNo = x.id, status = x.status }).ToList();
            return Json(result);
        }

        private async Task LoadMasterDataAsync()
        {
            try
            {
                var sections = await _apiService.GetSectionsFromApiAsync();
                var statuses = await _apiService.GetStatusesFromApiAsync();

                ViewBag.SectionList = sections?.OrderBy(x => x.Sections).ToList() ?? new List<SectionApiModels>();
                ViewBag.StatusList = statuses?.OrderBy(x => x.StatusId).ToList() ?? new List<StatusApiModels>();
            }
            catch
            {
                ViewBag.SectionList = new List<SectionApiModels>();
                ViewBag.StatusList = new List<StatusApiModels>();
            }
        }

        //private List<MaintenanceApiModels> FilterAndOrderJobs(List<MaintenanceApiModels> combined, string status, string sectionFrom, string sectionTo)
        //{
        //    if (combined == null) return new List<MaintenanceApiModels>();

        //    string searchStatus = (status ?? "").Trim().ToLower();
        //    string searchSecFrom = (sectionFrom ?? "").Trim().ToLower();
        //    string searchSecTo = (sectionTo ?? "").Trim().ToLower();

        //    var query = combined.AsEnumerable();

        //    if (!string.IsNullOrEmpty(searchStatus) &&
        //        !searchStatus.Equals("all") &&
        //        !searchStatus.Equals("ทั้งหมด") &&
        //        !searchStatus.Equals("เลือกสถานะ") &&
        //        !searchStatus.Equals("เลือกข้อมูล") &&
        //        !searchStatus.Equals(""))
        //    {
        //        query = query.Where(x =>
        //            (x.status ?? "").ToLower().Contains(searchStatus) ||
        //            (x.statustext ?? "").ToLower().Contains(searchStatus)
        //        );
        //    }

        //    if (!string.IsNullOrWhiteSpace(searchSecFrom) || !string.IsNullOrWhiteSpace(searchSecTo))
        //    {
        //        query = query.Where(x => {
        //            var sec = (x.sectioncreate ?? x.section ?? "").Trim().ToLower();
        //            if (string.IsNullOrEmpty(sec)) return false;

        //            if (!string.IsNullOrWhiteSpace(searchSecFrom) && searchSecFrom.Equals(searchSecTo))
        //            {
        //                return sec.Contains(searchSecFrom);
        //            }

        //            bool matchFrom = string.IsNullOrWhiteSpace(searchSecFrom) || string.Compare(sec, searchSecFrom, StringComparison.Ordinal) >= 0;
        //            bool matchTo = string.IsNullOrWhiteSpace(searchSecTo) || string.Compare(sec, searchSecTo, StringComparison.Ordinal) <= 0;

        //            return matchFrom && matchTo;
        //        });
        //    }

        //    return query.OrderByDescending(x => x.id).ToList();
        //}

        private List<MaintenanceApiModels> FilterAndOrderJobs(
            List<MaintenanceApiModels> combined, string status, List<string> sectionFrom, List<string> sectionTo)
        {
            if (combined == null) return new List<MaintenanceApiModels>();

            string searchStatus = (status ?? "").Trim().ToLower();

            var secFromSet = (sectionFrom ?? new List<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim().ToLower())
                .ToHashSet();

            var secToSet = (sectionTo ?? new List<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim().ToLower())
                .ToHashSet();

            var query = combined.AsEnumerable();

            if (!string.IsNullOrEmpty(searchStatus) &&
                !searchStatus.Equals("all") &&
                !searchStatus.Equals("ทั้งหมด") &&
                !searchStatus.Equals("เลือกสถานะ") &&
                !searchStatus.Equals("เลือกข้อมูล") &&
                !searchStatus.Equals(""))
            {
                query = query.Where(x =>
                    (x.status ?? "").ToLower().Contains(searchStatus) ||
                    (x.statustext ?? "").ToLower().Contains(searchStatus)
                );
            }

            if (secFromSet.Count > 0 || secToSet.Count > 0)
            {
                query = query.Where(x =>
                {
                    var sec = (x.sectioncreate ?? x.section ?? "").Trim().ToLower();
                    if (string.IsNullOrEmpty(sec)) return false;

                    bool matchFrom = secFromSet.Count == 0 || secFromSet.Contains(sec);
                    bool matchTo = secToSet.Count == 0 || secToSet.Contains(sec);

                    if (secFromSet.Count > 0 && secToSet.Count > 0)
                        return matchFrom || matchTo;

                    return matchFrom && matchTo;
                });
            }

            return query.OrderByDescending(x => x.id).ToList();
        }

        [HttpGet]
        public async Task<IActionResult> Machine()
        {
            var machineList = new DataTable();
            try
            {
                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    string query = "SELECT * FROM [dbo].[Machine] WHERE [IsActive] = 1 ORDER BY [Id] DESC";

                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                        {
                            machineList.Load(reader);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ SQL Query Error: {ex.Message}");
            }

            ViewBag.MachineTable = machineList;
            await PopulateNavbarDataAsync(); // 👈 ใส่ไว้ตรงนี้เพื่อไม่ให้ข้อมูล Navbar หลุด
            return View();
        }
    }
}