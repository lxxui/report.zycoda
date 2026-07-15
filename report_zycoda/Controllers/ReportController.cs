using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using report_zycoda.Data;
using report_zycoda.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

public class ReportController(ApiService apiService) : Controller
{
    private readonly ApiService _apiService = apiService;

    [HttpGet]
    public async Task<IActionResult> PrintReport()
    {
        // 💡 1. ดักจับ Query String ทุกรูปแบบ ไม่ว่าหน้าจอจะส่งมาเป็นพิมพ์เล็กหรือพิมพ์ใหญ่ ป้องกันปัญหาระหว่างหน้าเว็บกับหน้าปริ้น
        var start = Request.Query.FirstOrDefault(q => q.Key.Equals("start", StringComparison.OrdinalIgnoreCase)).Value.ToString();
        var end = Request.Query.FirstOrDefault(q => q.Key.Equals("end", StringComparison.OrdinalIgnoreCase)).Value.ToString();
        var status = Request.Query.FirstOrDefault(q => q.Key.Equals("status", StringComparison.OrdinalIgnoreCase)).Value.ToString();
        var sectionFrom = Request.Query.FirstOrDefault(q => q.Key.Equals("sectionFrom", StringComparison.OrdinalIgnoreCase)).Value.ToString();
        var sectionTo = Request.Query.FirstOrDefault(q => q.Key.Equals("sectionTo", StringComparison.OrdinalIgnoreCase)).Value.ToString();
        var jobtype = Request.Query.FirstOrDefault(q => q.Key.Equals("jobtype", StringComparison.OrdinalIgnoreCase)).Value.ToString();
        var view = Request.Query.FirstOrDefault(q => q.Key.Equals("view", StringComparison.OrdinalIgnoreCase)).Value.ToString();
        var rptType = Request.Query.FirstOrDefault(q => q.Key.Equals("rptType", StringComparison.OrdinalIgnoreCase)).Value.ToString();

        // ตรวจสอบสิทธิ์
        if (User.Identity == null || !User.Identity.IsAuthenticated)
            return RedirectToAction("Login", "Home");

        var sessionUser = User.Identity.Name;
        var sessionPass = User.Claims.FirstOrDefault(c => c.Type == "ApiPassword")?.Value;

        // 2. จัดการตัวแปรวันที่
        var sDate = string.IsNullOrEmpty(start) ? DateTime.Now.ToString("yyyy-MM-dd") : start;
        var eDate = string.IsNullOrEmpty(end) ? DateTime.Now.ToString("yyyy-MM-dd") : end;
        var jType = string.IsNullOrEmpty(jobtype) ? "EM,CM" : jobtype;
        var vMode = string.IsNullOrEmpty(view) ? "followup" : view;

        DateTime dStart = DateTime.Parse(sDate).Date;
        DateTime dEnd = DateTime.Parse(eDate).Date;

        // 3. เตรียมข้อมูล Master
        var statusMaster = await _apiService.GetStatusesFromApiAsync();
        var sectionMaster = await _apiService.GetSectionsFromApiAsync();
        var sectionDict = sectionMaster?.Where(s => !string.IsNullOrEmpty(s.Sections))
            .ToDictionary(s => s.Sections.Trim(), s => s.Name ?? s.Sections) ?? new Dictionary<string, string>();

        // 4. เรียก API Main Endpoint
        // 🚀 แก้ไข: แยกเส้นทางเรียก API ตามประเภทรายงานอย่างเด็ดขาด
        //    - ถ้าเป็นรายงาน PM (rptType == "PM") ให้ยิงไปที่ get_PM_order เท่านั้น
        //    - ถ้าเป็นรายงานอื่นๆ ให้ยิงไปที่ get_job_order ตามปกติ
        //    (เดิมโค้ดยิงทั้ง 2 เส้นเสมอ แล้วให้ผลลัพธ์ตัวหลังทับตัวแรก ทำให้ PM ไม่เคยเข้าเส้นทางจริงของตัวเอง
        //     และมี Bug อ่าน response ผิดตัวด้วย)
        bool isPmReport = string.Equals(rptType, "PM", StringComparison.OrdinalIgnoreCase);

        var queryParams = new Dictionary<string, string?> {
            { "plant", "FARMHOUSE" }, { "jobtype", jType }, { "start", sDate }, { "end", eDate }, { "view", vMode }
        };

        // 🔧 แก้ URL ที่พังจากอักขระ "  '" หลุดเข้ามาก่อน https:// (ทำให้ยิง PM ไม่เคยสำเร็จมาก่อน)
        var apiUrl = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString("https://farmhouse.zycoda.com/apimpros/get_job_order", queryParams);
        var apiUrlPM = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString("https://farmhouse.zycoda.com/apimpros/get_PM_order", queryParams);

        // เลือกยิงแค่เส้นทางที่ตรงกับประเภทรายงานที่ร้องขอ (ไม่ยิงทั้งคู่แล้วทับกันแบบเดิม)
        var targetUrl = isPmReport ? apiUrlPM : apiUrl;

        List<MaintenanceApiModels> rawData = new();
        using (HttpClient client = new HttpClient())
        {
            client.DefaultRequestHeaders.Add("accept", "application/json");
            client.DefaultRequestHeaders.Add("username", sessionUser);
            client.DefaultRequestHeaders.Add("password", sessionPass);

            var response = await client.GetAsync(targetUrl);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                if (json.Trim().StartsWith("{")) json = "[" + json + "]";
                var allData = JsonConvert.DeserializeObject<List<MaintenanceApiModels>>(json) ?? new();

                // 💡 กรองข้อมูลแบบดักจับช่องว่างและค่า Default "ทั้งหมด" อย่างเข้มงวด
                rawData = allData.Where((MaintenanceApiModels x) => {
                    var target = (x.sectioncreate ?? x.section ?? "").Trim().ToUpper();

                    bool mFrom = string.IsNullOrEmpty(sectionFrom) ||
                                 sectionFrom.Trim() == "" ||
                                 sectionFrom.Trim() == "ทั้งหมด" ||
                                 sectionFrom.Trim().ToUpper() == "ALL" ||
                                 string.Compare(target, sectionFrom.Trim().ToUpper()) >= 0;

                    bool mTo = string.IsNullOrEmpty(sectionTo) ||
                               sectionTo.Trim() == "" ||
                               sectionTo.Trim() == "ทั้งหมด" ||
                               sectionTo.Trim().ToUpper() == "ALL" ||
                               string.Compare(target, sectionTo.Trim().ToUpper()) <= 0;

                    bool mStatus = string.IsNullOrEmpty(status) ||
                                   status.Trim() == "" ||
                                   status.Trim() == "ทั้งหมด" ||
                                   status.Trim().ToUpper() == "ALL" ||
                                   x.status?.Trim().Equals(status.Trim(), StringComparison.OrdinalIgnoreCase) == true;

                    return mStatus && mFrom && mTo;
                }).ToList();
            }
        }

        // ==========================================================
        // 🛠️ 5. Summary Logic
        // ==========================================================
        var summaryData = rawData
            .GroupBy(x => (x.sectioncreate ?? x.section ?? "N/A").Trim())
            .Select(g => new ReportSummary
            {
                SectionName = sectionDict.TryGetValue(g.Key, out var name) ? name : g.Key,

                CarriedOver = g.Count(x => {
                    var cDate = ParseDate(x.timecreate);
                    var fDate = ParseDate(x.timeclose ?? x.timefinish);
                    return cDate < dStart && (string.IsNullOrEmpty(x.timeclose ?? x.timefinish) || fDate >= dStart);
                }),

                OpenedToday = g.Count(x => {
                    var d = ParseDate(x.timecreate);
                    return d >= dStart && d <= dEnd;
                }),

                NotApproved = g.Count(x => (x.status ?? "").Trim().ToLower() == "pending"),
                NotAccepted = g.Count(x => (x.status ?? "").Trim().ToLower() == "create"),
                AcceptJob = g.Count(x => (x.status ?? "").Trim().ToLower() == "accept"),
                Repair = g.Count(x => (x.status ?? "").Trim().ToLower() == "assign" || (x.status ?? "").Trim().ToLower() == "repair"),

                Finished = g.Count(x => {
                    string currentStatus = (x.status ?? "").Trim().ToLower();
                    return currentStatus == "finish" || currentStatus == "confirm";
                }),

                RejectedToday = g.Count(x => {
                    string currentStatus = (x.status ?? "").Trim().ToLower();
                    return currentStatus == "reject" || currentStatus == "deny";
                }),

                WaitPart = g.Count(x => (x.status?.Contains("Spare", StringComparison.OrdinalIgnoreCase) ?? false) ||
                                        (x.statustext?.Contains("อะไหล่") ?? false))
            })
            .OrderBy(x => x.SectionName).ToList();

        // 6. ส่งค่าพื้นฐานไปยัง View
        ViewBag.ApiUser = User.Claims.FirstOrDefault(c => c.Type == "FullName")?.Value ?? sessionUser;
        ViewBag.RefNo = $"{DateTime.Now:yyMMdd}-{new Random().Next(100, 999)}";
        ViewBag.PrintDate = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
        ViewBag.StartDate = sDate;
        ViewBag.EndDate = eDate;
        ViewBag.SectionFrom = sectionFrom;
        ViewBag.SectionTo = sectionTo;
        ViewBag.SectionDict = sectionDict;

        // ==========================================================
        // 7. คัดแยกประเภทรายงานแยก View เด็ดขาด 
        // ==========================================================
        string normalizedRptType = rptType?.ToUpper() ?? "DEFAULT";
        string viewName = "";

        SetReportMetadata(normalizedRptType);

        switch (normalizedRptType)
        {
            case "JTR":
                viewName = "PrintReport_Jobdaily_Checked";
                ViewBag.DepartmentName = "ผู้แจ้งงานตรวจสอบ";
                break;
            case "JTR_INTERNAL":
                viewName = "PrintReport_Receiver_Checked";
                ViewBag.DepartmentName = "ตรวจสอบ (ผู้รับแจ้ง/ช่าง)";
                break;
            case "HOTLINE":
                viewName = "PrintHotlineReport";
                ViewBag.DepartmentName = "ฝ่ายซ่อมบำรุง (Hotline)";
                break;
            case "PM":
                viewName = "PrintPMReport";
                ViewBag.DepartmentName = "ฝ่ายวิศวกรรม (PM)";
                break;
            case "MONTHLY":
                viewName = "PrintMonthlyReport";
                ViewBag.DepartmentName = "ฝ่ายบริหาร/สรุปประจำเดือน";
                break;
            default:
                viewName = "PrintReport_Jobdaily_Checked";
                ViewBag.DepartmentName = "ผู้แจ้งงานตรวจสอบ";
                break;
        }

        // 🚀 แก้ไข: PM ใช้ View "PrintPMReport" ซึ่งออกแบบมาแสดงข้อมูลระดับ Job
        // (timecreate, detail, timestart, timeendrepair, timedelay ฯลฯ) เหมือน JTR
        // ไม่ใช่ข้อมูลสรุปยอดแบบ ReportSummary ที่มีแค่ SectionName/CarriedOver/Finished
        // จึงต้องส่ง sortedRawData (List<MaintenanceApiModels>) ให้ PM เหมือนกับ JTR
        if (normalizedRptType == "JTR" || normalizedRptType == "JTR_INTERNAL" || normalizedRptType == "DEFAULT" || normalizedRptType == "PM")
        {
            var sortedRawData = rawData.OrderByDescending(x => x.id).ToList();
            return View(viewName, sortedRawData);
        }
        else
        {
            return View(viewName, summaryData);
        }
    }

    // ==========================================================
    // 🟢 🔥 ฟังก์ชันรายงาน KPI ตัวจริง แก้ไขการหลุดของระบบสรุปผล
    // ==========================================================
    [HttpGet]
    public async Task<IActionResult> KpiReport()
    {
        // 1. ดักจับ Query String สำหรับกรองข้อมูลในหน้า KPI ให้ครบเหมือนหน้าหลัก
        var start = Request.Query.FirstOrDefault(q => q.Key.Equals("start", StringComparison.OrdinalIgnoreCase)).Value.ToString();
        var end = Request.Query.FirstOrDefault(q => q.Key.Equals("end", StringComparison.OrdinalIgnoreCase)).Value.ToString();
        var status = Request.Query.FirstOrDefault(q => q.Key.Equals("status", StringComparison.OrdinalIgnoreCase)).Value.ToString();
        var sectionFrom = Request.Query.FirstOrDefault(q => q.Key.Equals("sectionFrom", StringComparison.OrdinalIgnoreCase)).Value.ToString();
        var sectionTo = Request.Query.FirstOrDefault(q => q.Key.Equals("sectionTo", StringComparison.OrdinalIgnoreCase)).Value.ToString();
        var jobtype = Request.Query.FirstOrDefault(q => q.Key.Equals("jobtype", StringComparison.OrdinalIgnoreCase)).Value.ToString();
        var view = Request.Query.FirstOrDefault(q => q.Key.Equals("view", StringComparison.OrdinalIgnoreCase)).Value.ToString();

        // ตรวจสอบสิทธิ์การใช้งานระบบ
        if (User.Identity == null || !User.Identity.IsAuthenticated)
            return RedirectToAction("Login", "Home");

        var sessionUser = User.Identity.Name;
        var sessionPass = User.Claims.FirstOrDefault(c => c.Type == "ApiPassword")?.Value;

        // ตั้งค่าตัวแปรวันที่ Default (ถ้าไม่ได้ส่งมาให้เอาวันปัจจุบัน)
        var sDate = string.IsNullOrEmpty(start) ? DateTime.Now.ToString("yyyy-MM-dd") : start;
        var eDate = string.IsNullOrEmpty(end) ? DateTime.Now.ToString("yyyy-MM-dd") : end;
        var jType = string.IsNullOrEmpty(jobtype) ? "EM,CM" : jobtype;
        var vMode = string.IsNullOrEmpty(view) ? "followup" : view;

        // 2. ดึง Master Data มาพ่วงเพื่อใช้แปลรหัสแผนกในหน้าจอสรุป (ถ้ามีเรียกใช้)
        var sectionMaster = await _apiService.GetSectionsFromApiAsync();
        var sectionDict = sectionMaster?.Where(s => !string.IsNullOrEmpty(s.Sections))
            .ToDictionary(s => s.Sections.Trim(), s => s.Name ?? s.Sections) ?? new Dictionary<string, string>();

        // 3. เรียกเอาข้อมูลดิบมาจาก endpoint ใบงานซ่อมบำรุงของเครือฟาร์มเฮ้าส์
        var queryParams = new Dictionary<string, string?> {
            { "plant", "FARMHOUSE" }, { "jobtype", jType }, { "start", sDate }, { "end", eDate }, { "view", vMode }
        };
        var apiUrl = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString("https://farmhouse.zycoda.com/apimpros/get_job_order", queryParams);

        List<MaintenanceApiModels> kpiRawData = new();
        using (HttpClient client = new HttpClient())
        {
            client.DefaultRequestHeaders.Add("accept", "application/json");
            client.DefaultRequestHeaders.Add("username", sessionUser);
            client.DefaultRequestHeaders.Add("password", sessionPass);

            var response = await client.GetAsync(apiUrl);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                if (json.Trim().StartsWith("{")) json = "[" + json + "]";
                var allData = JsonConvert.DeserializeObject<List<MaintenanceApiModels>>(json) ?? new();

                // 4. กรองตามเงื่อนไขที่เลือกบนหน้าจออย่างละเอียด
                kpiRawData = allData.Where(x => {
                    var target = (x.sectioncreate ?? x.section ?? "").Trim().ToUpper();

                    bool mFrom = string.IsNullOrEmpty(sectionFrom) || sectionFrom.Trim() == "" || sectionFrom.Trim() == "ทั้งหมด" || sectionFrom.Trim().ToUpper() == "ALL" || string.Compare(target, sectionFrom.Trim().ToUpper()) >= 0;
                    bool mTo = string.IsNullOrEmpty(sectionTo) || sectionTo.Trim() == "" || sectionTo.Trim() == "ทั้งหมด" || sectionTo.Trim().ToUpper() == "ALL" || string.Compare(target, sectionTo.Trim().ToUpper()) <= 0;
                    bool mStatus = string.IsNullOrEmpty(status) || status.Trim() == "" || status.Trim() == "ทั้งหมด" || status.Trim().ToUpper() == "ALL" || x.status?.Trim().Equals(status.Trim(), StringComparison.OrdinalIgnoreCase) == true;

                    return mStatus && mFrom && mTo;
                }).OrderByDescending(x => x.id).ToList();
            }
        }

        // --- [เพิ่มส่วนนี้เข้าไปในฟังก์ชัน KpiReport หลังจบส่วนดึงข้อมูล] ---

        // 5. คำนวณสรุปยอดจาก rawData ที่ได้มา
        DateTime dStart = DateTime.Parse(sDate).Date;
        DateTime dEnd = DateTime.Parse(eDate).Date;

        // ในฟังก์ชัน KpiReport() ปรับตรงส่วน summaryData เป็นแบบนี้ครับ
        var summaryData = kpiRawData
            .GroupBy(x => {
                // ดึงชื่อช่างจากฟิลด์ที่มี (ปรับชื่อฟิลด์ให้ตรงกับ Model ของฝ้ายนะครับ)
                var name = (x.uassign != null && x.uassign.Any()) ? string.Join(", ", x.uassign) : ("ยังไม่มอบหมาย");
                return name.Trim();
            })
            .Select(g => new ReportSummary
            {
                SectionName = g.Key, // ตอนนี้คือ "ชื่อช่าง"
                CarriedOver = g.Count(x => {
                    var cDate = ParseDate(x.timecreate);
                    var fDate = ParseDate(x.timeclose ?? x.timefinish);
                    return cDate < dStart && (string.IsNullOrEmpty(x.timeclose ?? x.timefinish) || fDate >= dStart);
                }),
                OpenedToday = g.Count(x => {
                    var d = ParseDate(x.timecreate);
                    return d >= dStart && d <= dEnd;
                }),
                Finished = g.Count(x => {
                    string currentStatus = (x.status ?? "").Trim().ToLower();
                    return currentStatus == "finish" || currentStatus == "confirm";
                }),
                Repair = g.Count(x => (x.status ?? "").Trim().ToLower() == "assign" || (x.status ?? "").Trim().ToLower() == "repair")
                // ... (ใส่ฟิลด์อื่นๆ ตามต้องการ)
            })
            .OrderBy(x => x.SectionName).ToList();

        return View("PrintMonthlyReport", summaryData);

    }

    private DateTime ParseDate(string? dateStr)
    {
        if (DateTime.TryParse(dateStr, out DateTime dt)) return dt.Date;
        return DateTime.MinValue;
    }

    private void SetReportMetadata(string? rptType)
    {
        var meta = rptType?.ToUpper() switch
        {
            "JTR" => ("รายงานติดตามสถานะงานซ่อม", "FM-ENG-001"),
            "JTR_INTERNAL" => ("รายงานรายละเอียดงานซ่อม (สำหรับช่าง)", "FM-ENG-002"),
            "HOTLINE" => ("รายงานภาพรวมงานประจำวัน (Hot Line)", "FM-ENG-003"),
            "PM" => ("รายงานแผนการบำรุงรักษาเชิงป้องกัน (PM)", "FM-ENG-004"),
            "MONTHLY" => ("รายงานประสิทธิภาพประจำเดือน", "FM-ENG-005"),
            _ => ("รายงานการซ่อมบำรุง", "FM-ENG-XXX")
        };
        ViewBag.Title = meta.Item1;
        ViewBag.CodeForm = meta.Item2;
    }
}
