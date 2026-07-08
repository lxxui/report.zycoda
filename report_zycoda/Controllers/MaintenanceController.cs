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
            // สมมติว่านี่คือชุดคำสั่งดึง API เดิมของคุณฝ้าย
            // นำไปเรียกใช้ในทุกๆ Action เช่น Index() และ Machine() ก่อนส่งคืน View
            List<SectionApiModels> sections = await _apiService.GetSectionsAsync();
            ViewBag.SectionList = sections;
        }

        // ==================================================
        // HELPER: ดึงข้อมูลขนานกัน
        // ==================================================

        private async Task<List<MaintenanceApiModels>> FetchJobsFromApi(
            string sessionUser,
            string sessionPass,
            string jobtype,
            string start,
            string end,
            string sessionClass,
            string sessionSection,
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

        // === ดึงข้อมูลมาใส่ SSMS 
        private async Task SyncJobsToLocalDatabase(List<MaintenanceApiModels> apiJobs)
        {
            if (apiJobs == null || !apiJobs.Any()) return;

            try
            {
                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    // ใช้ชื่อตาราง [ZycodaApiByPB].[dbo].[Job_order] ของคุณฝ้ายได้โดยตรง
                    string mergeSql = @"
                MERGE INTO [ZycodaApiByPB].[dbo].[Job_order] AS Target
                USING (SELECT @id AS id) AS Source
                ON (Target.[id] = Source.[id])
                WHEN MATCHED THEN
                    UPDATE SET 
                        Target.[refid] = @refid, Target.[reforder] = @reforder, Target.[MN] = @MN, Target.[MO] = @MO,
                        Target.[detail] = @detail, Target.[safety] = @safety, Target.[code] = @code, Target.[fl] = @fl,
                        Target.[downtime] = @downtime, Target.[difftime] = @difftime, Target.[timerepair] = @timerepair, Target.[timerepair_ot] = @timerepair_ot,
                        Target.[tag_abnormal] = @tag_abnormal, Target.[tag] = @tag, Target.[tags] = @tags, Target.[jobtype] = @jobtype,
                        Target.[section] = @section, Target.[productionstop] = @productionstop, Target.[planner] = @planner, Target.[statustext] = @statustext,
                        Target.[statussection] = @statussection, Target.[opt_5s] = @opt_5s, Target.[opt_5s_comment] = @opt_5s_comment, Target.[opt_protect] = @opt_protect,
                        Target.[opt_protect_comment] = @opt_protect_comment, Target.[rates] = @rates, Target.[timecreate] = @timecreate, Target.[timeassign] = @timeassign,
                        Target.[timestart] = @timestart, Target.[timeend] = @timeend, Target.[timefinish] = @timefinish, Target.[timeaccept] = @timeaccept,
                        Target.[timestartrepair] = @timestartrepair, Target.[timeendrepair] = @timeendrepair, Target.[timeclose] = @timeclose, Target.[timerunning] = @timerunning,
                        Target.[timeday] = @timeday, Target.[fldetail] = @fldetail, Target.[flrank] = @flrank, Target.[flpngrp] = @flpngrp,
                        Target.[priority] = @priority, Target.[status] = @status, Target.[usercreate] = @usercreate, Target.[sectioncreate] = @sectioncreate,
                        Target.[useraccept] = @useraccept, Target.[userfinish] = @userfinish, Target.[comment] = @comment, Target.[solution] = @solution,
                        Target.[problem] = @problem, Target.[causes] = @causes, Target.[preventive] = @preventive, Target.[ordertype] = @ordertype,
                        Target.[submiss] = @submiss, Target.[bdfac] = @bdfac, Target.[id_db] = @id_db
                WHEN NOT MATCHED THEN
                    INSERT ([id],[refid],[reforder],[MN],[MO],[detail],[safety],[code],[fl],[downtime],[difftime],[timerepair],[timerepair_ot],[tag_abnormal],[tag],[tags],[jobtype],[section],[productionstop],[planner],[statustext],[statussection],[opt_5s],[opt_5s_comment],[opt_protect],[opt_protect_comment],[rates],[timecreate],[timeassign],[timestart],[timeend],[timefinish],[timeaccept],[timestartrepair],[timeendrepair],[timeclose],[timerunning],[timeday],[fldetail],[flrank],[flpngrp],[priority],[status],[usercreate],[sectioncreate],[useraccept],[userfinish],[comment],[solution],[problem],[causes],[preventive],[ordertype],[submiss],[bdfac],[id_db])
                    VALUES (@id,@refid,@reforder,@MN,@MO,@detail,@safety,@code,@fl,@downtime,@difftime,@timerepair,@timerepair_ot,@tag_abnormal,@tag,@tags,@jobtype,@section,@productionstop,@planner,@statustext,@statussection,@opt_5s,@opt_5s_comment,@opt_protect,@opt_protect_comment,@rates,@timecreate,@timeassign,@timestart,@timeend,@timefinish,@timeaccept,@timestartrepair,@timeendrepair,@timeclose,@timerunning,@timeday,@fldetail,@flrank,@flpngrp,@priority,@status,@usercreate,@sectioncreate,@useraccept,@userfinish,@comment,@solution,@problem,@causes,@preventive,@ordertype,@submiss,@bdfac,@id_db);";

                    foreach (var job in apiJobs)
                    {
                        if (string.IsNullOrEmpty(job.id?.ToString())) continue;

                        using (SqlCommand cmd = new SqlCommand(mergeSql, conn))
                        {
                            cmd.Parameters.AddWithValue("@id", job.id.ToString());
                            cmd.Parameters.AddWithValue("@refid", job.refid ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@reforder", job.reforder ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@MN", job.machineno ?? job.MN ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@MO", job.MO ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@detail", job.detail ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@safety", job.safety ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@code", job.code ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@fl", job.fl ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@downtime", job.downtime ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@difftime", job.difftime ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@timerepair", job.timerepair ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@timerepair_ot", job.timerepair_ot ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@tag_abnormal", job.tag_abnormal ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@tag", job.tag ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@tags", job.tags ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@jobtype", job.jobtype ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@section", job.section ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@productionstop", job.productionstop ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@planner", job.planner ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@statustext", job.statustext ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@statussection", job.statussection ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@opt_5s", job.opt_5s ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@opt_5s_comment", job.opt_5s_comment ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@opt_protect", job.opt_protect ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@opt_protect_comment", job.opt_protect_comment ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@rates", job.rates ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@timerunning", job.timerunning ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@timeday", job.timeday ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@fldetail", job.fldetail ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@flrank", job.flrank ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@flpngrp", job.flpngrp ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@priority", job.priority ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@status", job.status ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@usercreate", job.usercreate ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@sectioncreate", job.sectioncreate ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@useraccept", job.useraccept ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@userfinish", job.userfinish ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@comment", job.comment ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@solution", job.solution ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@problem", job.problem ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@causes", job.causes ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@preventive", job.preventive ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@ordertype", job.ordertype ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@submiss", job.submiss ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@bdfac", job.bdfac ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@id_db", job.id_db ?? (object)DBNull.Value);

                            // ใช้ Helper จัดการเรื่องแปลงฟอร์แมตวันเวลา
                            cmd.Parameters.AddWithValue("@timecreate", ParseDateHelper(job.timecreate));
                            cmd.Parameters.AddWithValue("@timeassign", ParseDateHelper(job.timeassign));
                            cmd.Parameters.AddWithValue("@timestart", ParseDateHelper(job.timestart));
                            cmd.Parameters.AddWithValue("@timeend", ParseDateHelper(job.timeend));
                            cmd.Parameters.AddWithValue("@timefinish", ParseDateHelper(job.timefinish));
                            cmd.Parameters.AddWithValue("@timeaccept", ParseDateHelper(job.timeaccept));
                            cmd.Parameters.AddWithValue("@timestartrepair", ParseDateHelper(job.timestartrepair));
                            cmd.Parameters.AddWithValue("@timeendrepair", ParseDateHelper(job.timeendrepair));
                            cmd.Parameters.AddWithValue("@timeclose", ParseDateHelper(job.timeclose));

                            await cmd.ExecuteNonQueryAsync();
                        }
                    }
                }
                Console.WriteLine($"🔄 [Sync Database Success] อัปเดตข้อมูลเข้าตารางเรียบร้อย จำนวน {apiJobs.Count} รายการ");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [Merge Error]: {ex.Message}");
            }
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

            // 🎯 3. จุดเรียกใช้งาน: พอเราดึงของจาก API ได้ปุ๊บ สั่งซิงค์ลงตารางตัวเองทันทีตรงนี้ครับ
            var freshApiJobs = await FetchJobsFromApi(sessionUser, sessionPass, ViewBag.CurrentJobType, finalStart, finalEnd, ViewBag.CurrentView);

            // เรียกฟังก์ชันที่เราเพิ่งแปะด้านบนมาทำงาน
            await SyncJobsToLocalDatabase(freshApiJobs);

            // 4. หลังจากซิงค์เสร็จ ค่อยไป Query จากตารางของตัวเองออกมาโชว์หน้าเว็บ
            var combined = await FetchJobsFromLocalDb(finalStart, finalEnd, ViewBag.CurrentJobType);

            //var combined = apiJobsTask.Result;

            var final = FilterAndOrderJobs(combined, ViewBag.SelectedStatus, ViewBag.CurrentSectionFrom, ViewBag.CurrentSectionTo);

            if (ViewBag.SectionList is List<report_zycoda.Models.SectionApiModels> sectionList)
            {
                ViewBag.EmSectionList = sectionList.Where(x => x.Sections == "EM").ToList();
            }

            await PopulateNavbarDataAsync(); // 👈 ใส่ไว้ตรงนี้เพื่อไม่ให้ข้อมูล Navbar หลุด
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

            // 🚀 ดึงค่า Class และ Section ออกมาจาก Claims Principal ในหน้านี้ด้วยเช่นกัน
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

            string finalStart = string.IsNullOrEmpty(start) ? todayStr : start;
            string finalEnd = string.IsNullOrEmpty(end) ? todayStr : end;

            ViewBag.CurrentStart = finalStart;
            ViewBag.CurrentEnd = finalEnd;
            ViewBag.SelectedStatus = status;
            ViewBag.CurrentJobType = string.IsNullOrEmpty(jobtype) ? "EM,CM" : jobtype;
            ViewBag.CurrentView = string.IsNullOrEmpty(view) ? "all" : view;
            ViewBag.CurrentSectionFrom = sectionFrom;
            ViewBag.CurrentSectionTo = sectionTo;

            var masterDataTask = LoadMasterDataAsync();

            // 🚀 ส่งต่อข้อมูลสิทธิ์เข้าไปดึงจาก API
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