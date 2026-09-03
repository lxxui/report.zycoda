using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using report_zycoda.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

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

        private async Task<List<MaintenanceApiModels>> FetchJobsFromApi(string sessionUser, string sessionPass, string jobtype, string start, string end,
            string sessionClass, string sessionSection, string view = "followup")
        {
            var culture = CultureInfo.InvariantCulture;

            if (!DateTime.TryParseExact(start, "yyyy-MM-dd", culture, DateTimeStyles.None, out var globalStart))
            {
                if (!DateTime.TryParse(start, culture, DateTimeStyles.None, out globalStart))
                {
                    globalStart = DateTime.Now.AddYears(-2);
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

            var viewsToCall = (view == "all") ? new List<string> { "followup", "myjob" } : new List<string> { view };
            var uniqueJobs = new ConcurrentDictionary<string, MaintenanceApiModels>();
            var client = _httpClientFactory.CreateClient("ZycodaApiClient");

            // Setting ป้องกัน Crash เมื่อ เจอ Type ไม่ตรง หรือ Null
            var jsonSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                Error = (sender, args) =>
                {
                    Console.WriteLine($"⚠️ [JSON FIELD MISMATCH WARNING]: {args.ErrorContext.Error.Message}");
                    args.ErrorContext.Handled = true; // สั่งข้ามฟิลด์ที่มีปัญหาแล้วแปลงฟิลด์อื่นต่อ
                }
            };

            var tasks = viewsToCall.Select(async v =>
            {
                var queryParams = new Dictionary<string, string?>
                {
                    ["plant"] = "FARMHOUSE",
                    ["start"] = finalStartStr,
                    ["end"] = finalEndStr,
                    ["view"] = v
                };

                if (!string.IsNullOrWhiteSpace(jobtype) && jobtype != "all")
                    queryParams["jobtype"] = jobtype;

                var url = QueryHelpers.AddQueryString("apimpros/get_job_order", queryParams);

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("accept", "application/json");
                request.Headers.TryAddWithoutValidation("username", sessionUser);
                request.Headers.TryAddWithoutValidation("password", sessionPass);

                try
                {
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                    var rawJson = await response.Content.ReadAsStringAsync();

                    Console.WriteLine("--------------------------------------------------");
                    Console.WriteLine($"📥 [API VIEW]: {v} | STATUS: {response.StatusCode}");
                    Console.WriteLine($"🔍 [API RAW DATA SAMPLE]: {(rawJson.Length > 500 ? rawJson.Substring(0, 500) : rawJson)}");
                    Console.WriteLine("--------------------------------------------------");

                    if (response.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(rawJson))
                    {
                        List<MaintenanceApiModels>? rawDataList = null;

                        try
                        {
                            // 1. แปลงเป็น JToken ก่อนเพื่อดูว่า response เป็น Array หรือ Object
                            var token = JToken.Parse(rawJson);

                            if (token is JArray)
                            {
                                rawDataList = JsonConvert.DeserializeObject<List<MaintenanceApiModels>>(rawJson, jsonSettings);
                            }
                            else if (token is JObject jObj)
                            {
                                var dataToken = jObj["data"] ?? jObj["result"] ?? jObj["jobs"];
                                if (dataToken != null)
                                {
                                    rawDataList = dataToken.ToObject<List<MaintenanceApiModels>>(JsonSerializer.Create(jsonSettings));
                                }
                                else
                                {
                                    var single = jObj.ToObject<MaintenanceApiModels>(JsonSerializer.Create(jsonSettings));
                                    if (single != null) rawDataList = new List<MaintenanceApiModels> { single };
                                }
                            }
                        }
                        catch (Exception jsonEx)
                        {
                            Console.WriteLine($"❌ [JSON PARSE FAILED]: {jsonEx.Message}");
                        }

                        if (rawDataList != null)
                        {
                            foreach (var item in rawDataList)
                            {
                                var key = item?.id?.ToString();
                                if (!string.IsNullOrEmpty(key))
                                {
                                    uniqueJobs.TryAdd(key, item);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ [API CRASHED]: {ex.Message}");
                }
            });

            await Task.WhenAll(tasks);
            Console.WriteLine($"📊 [API SUMMARY] Total unique jobs mapped: {uniqueJobs.Count} rows.\n");

            return uniqueJobs.Values.ToList();
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

            // 🚀 ดึงค่า Class และ Section ออกมาจาก Claims Principal
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
            var defaultStartStr = DateTime.Now.AddYears(-2).ToString("yyyy-MM-dd", culture);

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

            // 🚀 ยิง Task ดึงข้อมูลจาก API
            var apiJobsTask = FetchJobsFromApi(sessionUser, sessionPass, ViewBag.CurrentJobType, finalStart, finalEnd, sessionClass, sessionSection, ViewBag.CurrentView);

            await Task.WhenAll(masterDataTask, apiJobsTask);

            // 🎯 ดึง Result อย่างปลอดภัยหลังจาก Task เสร็จสมบูรณ์
            var combined = await apiJobsTask;

            var final = FilterAndOrderJobs(combined, ViewBag.SelectedStatus, ViewBag.CurrentSectionFrom, ViewBag.CurrentSectionTo);

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

            string sectionFrom = Request.Query["sectionFrom"].ToString();
            string sectionTo = Request.Query["sectionTo"].ToString();

            string status = Request.Query["status"].ToString();
            if (string.IsNullOrEmpty(status)) status = Request.Query["Status"].ToString();

            string view = Request.Query["view"].ToString();

            string finalStart = string.IsNullOrEmpty(start) ? todayStr : start.Trim();
            string finalEnd = string.IsNullOrEmpty(end) ? todayStr : end.Trim();

            ViewBag.CurrentStart = finalStart;
            ViewBag.CurrentEnd = finalEnd;
            ViewBag.SelectedStatus = string.IsNullOrWhiteSpace(status) ? "all" : status.Trim();
            ViewBag.CurrentJobType = string.IsNullOrWhiteSpace(jobtype) ? "EM,CM" : jobtype.Trim();
            ViewBag.CurrentView = string.IsNullOrWhiteSpace(view) ? "all" : view.Trim();
            ViewBag.CurrentSectionFrom = sectionFrom?.Trim();
            ViewBag.CurrentSectionTo = sectionTo?.Trim();

            var masterDataTask = LoadMasterDataAsync();

            // 🚀 ยิง Task ดึงข้อมูลจาก API
            var apiJobsTask = FetchJobsFromApi(sessionUser, sessionPass, ViewBag.CurrentJobType, finalStart, finalEnd, sessionClass, sessionSection, ViewBag.CurrentView);

            await Task.WhenAll(masterDataTask, apiJobsTask);

            var jobs = await apiJobsTask;
            var final = FilterAndOrderJobs(jobs, ViewBag.SelectedStatus, ViewBag.CurrentSectionFrom, ViewBag.CurrentSectionTo);

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

        private List<MaintenanceApiModels> FilterAndOrderJobs(List<MaintenanceApiModels> combined, string status, string sectionFrom, string sectionTo)
        {
            if (combined == null) return new List<MaintenanceApiModels>();

            string searchStatus = (status ?? "").Trim().ToLower();
            string searchSecFrom = (sectionFrom ?? "").Trim().ToLower();
            string searchSecTo = (sectionTo ?? "").Trim().ToLower();

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

            if (!string.IsNullOrWhiteSpace(searchSecFrom) || !string.IsNullOrWhiteSpace(searchSecTo))
            {
                query = query.Where(x => {
                    var sec = (x.sectioncreate ?? x.section ?? "").Trim().ToLower();
                    if (string.IsNullOrEmpty(sec)) return false;

                    if (!string.IsNullOrWhiteSpace(searchSecFrom) && searchSecFrom.Equals(searchSecTo))
                    {
                        return sec.Contains(searchSecFrom);
                    }

                    bool matchFrom = string.IsNullOrWhiteSpace(searchSecFrom) || string.Compare(sec, searchSecFrom, StringComparison.Ordinal) >= 0;
                    bool matchTo = string.IsNullOrWhiteSpace(searchSecTo) || string.Compare(sec, searchSecTo, StringComparison.Ordinal) <= 0;

                    return matchFrom && matchTo;
                });
            }

            return query.OrderByDescending(x => x.id).ToList();
        }

        // ==================================================
        // MACHINE
        // ==================================================
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