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
    public async Task<IActionResult> PrintReport(string start, string end, string status, string sectionFrom, string sectionTo, string jobtype, string rptType)
    {
        // 1. ดึงข้อมูลจาก Identity (ให้ตรงกับหน้า Index)
        if (User.Identity == null || !User.Identity.IsAuthenticated)
            return RedirectToAction("Login", "Home");

        var sessionUser = User.Identity.Name;
        var sessionPass = User.Claims.FirstOrDefault(c => c.Type == "ApiPassword")?.Value;
        var sessionName = User.Claims.FirstOrDefault(c => c.Type == "FullName")?.Value ?? sessionUser;

        // 2. โหลด Master Section สำหรับแปลง รหัส -> ชื่อ
        var jsonsection = System.IO.File.ReadAllText("wwwroot/data/section.json");
        var sectionList = System.Text.Json.JsonSerializer.Deserialize<List<report_zycoda.Models.SectionApiModels>>(jsonsection)
                          ?? new List<report_zycoda.Models.SectionApiModels>();

        var sectionDict = sectionList
            .Where(s => !string.IsNullOrEmpty(s.section))
            .ToDictionary(s => s.section.Trim(), s => s.name ?? s.section);

        // 3. เตรียม API และดึงข้อมูล
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

        var baseUrl = "https://api.zycoda.com/apimpros/get_job_order";
        var apiUrl = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(baseUrl, queryParams);

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

                // --- 🔥 [Logic Update] Filter Section Range ---
                rawData = allData.Where(x =>
                {
                    var target = (x.sectioncreate ?? x.section ?? "").Trim();

                    // กรอง Status (ถ้าไม่ใช่ ALL)
                    bool matchStatus = string.IsNullOrEmpty(status) || status == "ทั้งหมด" || status == "ALL" ||
                                       x.status?.Trim().Equals(status.Trim(), StringComparison.OrdinalIgnoreCase) == true;

                    // กรอง Section From
                    bool matchFrom = string.IsNullOrEmpty(sectionFrom) || sectionFrom == "ทั้งหมด" ||
                                     string.Compare(target, sectionFrom.Trim()) >= 0;

                    // กรอง Section To
                    bool matchTo = string.IsNullOrEmpty(sectionTo) || sectionTo == "ทั้งหมด" ||
                                   string.Compare(target, sectionTo.Trim()) <= 0;

                    return matchStatus && matchFrom && matchTo;
                }).ToList();
            }
        }

        // 4. Summary Logic (Hotline / Monthly / PM)
        var atDate = DateTime.Now.Date;
        var summaryData = rawData
            .GroupBy(x => x.sectioncreate?.Trim() ?? "ไม่ระบุแผนก")
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




        // 5. ViewBag สำหรับ Header รายงาน
        ViewBag.SectionFrom = string.IsNullOrEmpty(sectionFrom) ? "ทั้งหมด" : sectionFrom;
        ViewBag.SectionTo = string.IsNullOrEmpty(sectionTo) ? "ทั้งหมด" : sectionTo;
        ViewBag.StartDate = start;
        ViewBag.EndDate = end;
        ViewBag.Status = string.IsNullOrEmpty(status) ? "ทั้งหมด" : status;
        ViewBag.ApiUser = sessionName;
        ViewBag.PrintDate = DateTime.Now.ToString("dd/MM/yyyy HH:mm");

        // --- 5. ViewBag สำหรับ Header รายงาน ---

        // แปลงรหัส Section From เป็นชื่อ
        ViewBag.SectionFrom = (string.IsNullOrEmpty(sectionFrom) || sectionFrom == "ทั้งหมด")
            ? "ทั้งหมด"
            : (sectionDict.TryGetValue(sectionFrom.Trim(), out var fName) ? fName : sectionFrom);

        // แปลงรหัส Section To เป็นชื่อ
        ViewBag.SectionTo = (string.IsNullOrEmpty(sectionTo) || sectionTo == "ทั้งหมด")
            ? "ทั้งหมด"
            : (sectionDict.TryGetValue(sectionTo.Trim(), out var tName) ? tName : sectionTo);
        // เพิ่มส่วนนี้เพื่อให้หน้า View มีข้อมูล Section ทั้งหมดไปทำ Lookup
        ViewBag.Section = sectionList;


        // 6. Report Type Mapping & View Path
        SetReportMetadata(rptType); // แยกไปเขียนเป็น Private Method เพื่อความสะอาด

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