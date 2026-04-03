using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using report_zycoda.Models;
using System.Reflection.Metadata;
using System.Text.Json;
using static System.Collections.Specialized.BitVector32;

public class ReportController : Controller
{
    [HttpGet] // 👈 บังคับให้เป็น Get
    [HttpGet]
    public async Task<IActionResult> PrintReport(string start, string end, string status, string sectionFrom, string sectionTo, string jobtype, string rptType)
    {
        // 1. Authentication & Session
        if (User.Identity == null || !User.Identity.IsAuthenticated)
            return RedirectToAction("Login", "Home");

        var sessionUser = User.Identity.Name;
        var sessionPass = User.Claims.FirstOrDefault(c => c.Type == "ApiPassword")?.Value;
        var sessionName = User.Claims.FirstOrDefault(c => c.Type == "FullName")?.Value ?? sessionUser;

        // 2. Load Master Section (Map: ID -> Name)
        var jsonsection = System.IO.File.ReadAllText("wwwroot/data/section.json");
        var sectionList = System.Text.Json.JsonSerializer.Deserialize<List<report_zycoda.Models.SectionApiModels>>(jsonsection)
                          ?? new List<report_zycoda.Models.SectionApiModels>();

        var sectionDict = sectionList
            .Where(s => !string.IsNullOrEmpty(s.section))
            .ToDictionary(s => s.section.Trim(), s => s.name ?? s.section);

        // 3. API Data Fetching
        if (string.IsNullOrEmpty(start)) start = DateTime.Now.ToString("yyyy-MM-dd");
        if (string.IsNullOrEmpty(end)) end = DateTime.Now.ToString("yyyy-MM-dd");

        var queryParams = new Dictionary<string, string?>
    {
        { "plant", "FARMHOUSE" },
        { "jobtype", "EM,CM" },
        { "start", start },
        { "end", end },
        { "status", "ALL" }
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

                // --- 🔥 Logic Filter Section (ปรับให้เสถียรขึ้น) ---
                rawData = allData.Where(x =>
                {
                    var target = (x.sectioncreate ?? x.section ?? "").Trim().ToUpper();
                    var sFrom = (sectionFrom ?? "").Trim().ToUpper();
                    var sTo = (sectionTo ?? "").Trim().ToUpper();

                    // กรอง Status
                    bool matchStatus = string.IsNullOrEmpty(status) || status == "ทั้งหมด" || status == "ALL" ||
                                       x.status?.Trim().Equals(status.Trim(), StringComparison.OrdinalIgnoreCase) == true;

                    // กรอง Range ของ Section
                    bool matchFrom = string.IsNullOrEmpty(sectionFrom) || sectionFrom == "ทั้งหมด" ||
                                     string.Compare(target, sFrom) >= 0;

                    bool matchTo = string.IsNullOrEmpty(sectionTo) || sectionTo == "ทั้งหมด" ||
                                   string.Compare(target, sTo) <= 0;

                    return matchStatus && matchFrom && matchTo;
                }).ToList();
            }
        }

        // 4. Summary Logic for HOTLINE/PM/MONTHLY
        var atDate = DateTime.Now.Date;
        var summaryData = rawData
            .GroupBy(x => (x.sectioncreate ?? x.section ?? "N/A").Trim())
            .Select(g => new ReportSummary
            {
                SectionName = sectionDict.TryGetValue(g.Key, out var name) ? name : g.Key,
                CarriedOver = g.Count(x => ParseDate(x.timecreate) < atDate && (string.IsNullOrEmpty(x.timeclose) || ParseDate(x.timeclose) >= atDate) && (x.status == "Create" || x.status == "Accept" || x.status == "Assign")),
                OpenedToday = g.Count(x => ParseDate(x.timecreate) == atDate && x.status == "Create"),
                AcceptJob = g.Count(x => x.status == "Accept" && string.IsNullOrEmpty(x.timeclose)),
                Repair = g.Count(x => x.status == "Assign" && string.IsNullOrEmpty(x.timeclose)),
                Finished = g.Count(x => x.status == "Finish" && !string.IsNullOrEmpty(x.timeclose) && ParseDate(x.timeclose) == atDate),
                RejectedToday = g.Count(x => x.status == "Reject"),
                WaitPart = g.Count(x => (x.status != null && x.status.Contains("Spare")) || (x.statustext != null && x.statustext.Contains("อะไหล่")))
            })
            .OrderBy(x => x.SectionName).ToList();

        // 5. ViewBag Setup (ทำทีเดียวให้ครบ)
        ViewBag.SectionFrom = (string.IsNullOrEmpty(sectionFrom) || sectionFrom == "ทั้งหมด") ? "ทั้งหมด" : (sectionDict.TryGetValue(sectionFrom.Trim(), out var fName) ? fName : sectionFrom);
        ViewBag.SectionTo = (string.IsNullOrEmpty(sectionTo) || sectionTo == "ทั้งหมด") ? "ทั้งหมด" : (sectionDict.TryGetValue(sectionTo.Trim(), out var tName) ? tName : sectionTo);
        ViewBag.StartDate = start;
        ViewBag.EndDate = end;
        ViewBag.Status = string.IsNullOrEmpty(status) ? "ทั้งหมด" : status;
        ViewBag.ApiUser = sessionName;
        ViewBag.PrintDate = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
        ViewBag.RefNo = $"{DateTime.Now:yyMMdd}-{new Random().Next(100, 999)}";
        ViewBag.Section = sectionList;

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
    // --- 🛠️ Helper Methods ---

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