using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using report_zycoda.Data;
using report_zycoda.Models;

public class ReportController(ApiService apiService) : Controller
{
    private readonly ApiService _apiService = apiService;

    [HttpGet]
    public async Task<IActionResult> PrintReport(
        string start,
        string end,
        string status,
        string sectionFrom,
        string sectionTo,
        string jobtype,
        string view , // รับค่าจาก Radio Button (followup / myjob)
        string rptType)
    {
        // 1. Authentication & Session
        if (User.Identity == null || !User.Identity.IsAuthenticated)
            return RedirectToAction("Login", "Home");

        var sessionUser = User.Identity.Name;
        var sessionPass = User.Claims.FirstOrDefault(c => c.Type == "ApiPassword")?.Value;

        // ดึงค่า FullName และ LastName ออกมา
        var firstName = User.Claims.FirstOrDefault(c => c.Type == "FullName")?.Value ?? "";
        var lastName = User.Claims.FirstOrDefault(c => c.Type == "LastName")?.Value ?? "";

        // นำมาต่อกัน (ใช้ interpolation $"{...}") 
        // ถ้า lastName ว่าง ก็จะแสดงแค่ firstName
        var sessionName = string.IsNullOrEmpty(lastName)
            ? firstName
            : $"{firstName} {lastName}";

        // ถ้า firstName ยังว่างอีก ให้ใช้ sessionUser (Username) เป็นค่าสำรอง
        if (string.IsNullOrEmpty(sessionName))
        {
            sessionName = User.Identity?.Name ?? "Unknown User";
        }

        ViewBag.ApiUser = sessionName;


        // 2. Load Master Data
        var statusMaster = await _apiService.GetStatusesFromApiAsync();
        ViewBag.StatusList = statusMaster.OrderBy(x => x.StatusId).ToList();

        var sectionMaster = await _apiService.GetSectionsFromApiAsync();
        ViewBag.SectionList = sectionMaster.OrderBy(x => x.Id_Zy).ToList();

        var sectionDict = sectionMaster
            .Where(s => !string.IsNullOrEmpty(s.Sections))
            .ToDictionary(s => s.Sections.Trim(), s => s.Name ?? s.Sections);

        // 3. API Data Fetching Setup
        if (string.IsNullOrEmpty(start)) start = DateTime.Now.ToString("yyyy-MM-dd");
        if (string.IsNullOrEmpty(end)) end = DateTime.Now.ToString("yyyy-MM-dd");
        if (string.IsNullOrEmpty(jobtype)) jobtype = "EM,CM";
        if (string.IsNullOrEmpty(view)) jobtype = "myjob,followup";

        var queryParams = new Dictionary<string, string?>
        {
            { "plant", "FARMHOUSE" },
            { "jobtype", jobtype },
            { "start", start },
            { "end", end },
            { "view", view },
            { "status", status }
        };

        var apiUrl = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString("https://api.zycoda.com/apimpros/get_job_order", queryParams);
        List<MaintenanceApiModels> rawData = new();

        using (HttpClient client = new HttpClient())
        {
            client.DefaultRequestHeaders.Add("username", sessionUser);
            client.DefaultRequestHeaders.Add("password", sessionPass);

            var response = await client.GetAsync(apiUrl);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var allData = JsonConvert.DeserializeObject<List<MaintenanceApiModels>>(json) ?? new();

                // --- 🔥 Logic Filter ตามพฤติกรรมหน้า Maintenance ---
                rawData = allData.Where(x =>
                {
                    // กรองตามหน่วยงาน (Section Range)
                    var target = (x.sectioncreate ?? x.section ?? "").Trim().ToUpper();
                    var sFrom = (sectionFrom ?? "").Trim().ToUpper();
                    var sTo = (sectionTo ?? "").Trim().ToUpper();

                    bool matchFrom = string.IsNullOrEmpty(sectionFrom) || sectionFrom == "ทั้งหมด" || string.Compare(target, sFrom) >= 0;
                    bool matchTo = string.IsNullOrEmpty(sectionTo) || sectionTo == "ทั้งหมด" || string.Compare(target, sTo) <= 0;

                    // กรองตามสถานะ (Status)
                    bool matchStatus = string.IsNullOrEmpty(status) || status == "ทั้งหมด" || status == "ALL" ||
                                       x.status?.Trim().Equals(status.Trim(), StringComparison.OrdinalIgnoreCase) == true;

                    // กรองตามมุมมองข้อมูล (View Mode) *** หัวใจสำคัญ ***
                    bool matchView = true;
                    if (view == "myjob")
                    {
                        // My Job: แสดงเฉพาะงานที่เราเกี่ยวข้อง (เป็นคนแจ้ง หรือ เป็นช่างที่รับงาน)
                        matchView = (x.workby?.Trim() == sessionUser) ||
                                    (x.createby?.Trim() == sessionUser) ||
                                    (x.assign_to?.Contains(sessionUser) ?? false);
                    }
                    else // view == "followup"
                    {
                        // Follow Up: แสดงงานทั้งหมดของหน่วยงานเรา หรือที่เราต้องติดตาม (ตามสิทธิ์ปกติ)
                        // โดยทั่วไปคือไม่ต้องกรอง user ทิ้ง แสดงตาม filter section/status ด้านบนได้เลย
                        matchView = true;
                    }

                    return matchStatus && matchFrom && matchTo && matchView;
                }).ToList();
            }
        }

        // 4. Summary Logic for Summary Reports
        var atDate = DateTime.Now.Date;
        var summaryData = rawData
            .GroupBy(x => (x.sectioncreate ?? x.section ?? "N/A").Trim())
            .Select(g => new ReportSummary
            {
                SectionName = sectionDict.TryGetValue(g.Key, out var name) ? name : g.Key,
                //ยกมาจากวันก่อนหน้าปัจจุบัน
                CarriedOver = g.Count(x => ParseDate(x.timecreate) < atDate && (string.IsNullOrEmpty(x.timeclose) || ParseDate(x.timeclose) >= atDate) && (x.status == "Create" || x.status == "Accept" || x.status == "Assign")),
                OpenedToday = g.Count(x => ParseDate(x.timecreate) == atDate && x.status == "Create"),
                // รับงาน
                AcceptJob = g.Count(x => x.status == "Accept" && string.IsNullOrEmpty(x.timeclose)),
                // กำลังทำ
                Repair = g.Count(x => x.status == "Assign" && string.IsNullOrEmpty(x.timeclose)),
                // เสร็จสิ้น
                Finished = g.Count(x => x.status == "Finish" || x.status == "Confirm" && !string.IsNullOrEmpty(x.timeclose) && ParseDate(x.timeclose) == atDate),
                //ตีกลับ ยกเลิก
                RejectedToday = g.Count(x => x.status == "Reject"),
                //รออะไหล่
                WaitPart = g.Count(x => (x.status != null && x.status.Contains("Spare")) || (x.statustext != null && x.statustext.Contains("อะไหล่")))
            })
            .OrderBy(x => x.SectionName).ToList();

        // 5. ViewBag Setup
        ViewBag.SectionFrom = (string.IsNullOrEmpty(sectionFrom) || sectionFrom == "ทั้งหมด") ? "ทั้งหมด" : (sectionDict.TryGetValue(sectionFrom.Trim(), out var fName) ? fName : sectionFrom);
        ViewBag.SectionTo = (string.IsNullOrEmpty(sectionTo) || sectionTo == "ทั้งหมด") ? "ทั้งหมด" : (sectionDict.TryGetValue(sectionTo.Trim(), out var tName) ? tName : sectionTo);

        ViewBag.StartDate = start;
        ViewBag.EndDate = end;
        ViewBag.Status = string.IsNullOrEmpty(status) ? "ทั้งหมด" : status;
        ViewBag.JobType = jobtype;
        ViewBag.ApiUser = sessionName;
        ViewBag.PrintDate = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
        ViewBag.RefNo = $"{DateTime.Now:yyMMdd}-{new Random().Next(100, 999)}";
        ViewBag.Section = sectionMaster;
        ViewBag.view = view;

        SetReportMetadata(rptType);

        // 6. View Selection
        string viewName = rptType?.ToUpper() switch
        {
            "JTR" => "PrintReport_Receiver_Checked",
            "JTR_INTERNAL" => "PrintReport_Jobdaily_Checked",
            "HOTLINE" => "PrintHotlineReport",
            "PM" => "PrintPMReport",
            "MONTHLY" => "PrintMonthlyReport",
            _ => "PrintReport_Receiver_Checked"
        };

        if (rptType == "JTR" || rptType == "JTR_INTERNAL")
            return View(viewName, rawData);

        return View(viewName, summaryData);
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