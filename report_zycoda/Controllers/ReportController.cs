using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using report_zycoda.Data;
using report_zycoda.Models;

public class ReportController(ApiService apiService) : Controller
{
    private readonly ApiService _apiService = apiService;

    [HttpGet]
    public async Task<IActionResult> PrintReport(string start, string end, string status, string sectionFrom, string sectionTo, string jobtype, string view, string rptType)
    {
        // 1. ตรวจสอบสิทธิ์
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
        var queryParams = new Dictionary<string, string?> {
            { "plant", "FARMHOUSE" }, { "jobtype", jType }, { "start", sDate }, { "end", eDate }, { "view", vMode }
        };
        var apiUrl = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString("https://farmhouse.zycoda.com/apimpros/get_job_order", queryParams);

        List<MaintenanceApiModels> rawData = new();
        using (HttpClient client = new HttpClient())
        {
            client.DefaultRequestHeaders.Add("username", sessionUser);
            client.DefaultRequestHeaders.Add("password", sessionPass);
            var response = await client.GetAsync(apiUrl);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                if (json.Trim().StartsWith("{")) json = "[" + json + "]";
                var allData = JsonConvert.DeserializeObject<List<MaintenanceApiModels>>(json) ?? new();

                // กรองข้อมูลตามเงื่อนไขที่เลือกจากหน้าเว็บ
                rawData = allData.Where((MaintenanceApiModels x) => {
                    var target = (x.sectioncreate ?? x.section ?? "").Trim().ToUpper();
                    bool mFrom = string.IsNullOrEmpty(sectionFrom) || sectionFrom == "ทั้งหมด" || string.Compare(target, sectionFrom.Trim().ToUpper()) >= 0;
                    bool mTo = string.IsNullOrEmpty(sectionTo) || sectionTo == "ทั้งหมด" || string.Compare(target, sectionTo.Trim().ToUpper()) <= 0;
                    bool mStatus = string.IsNullOrEmpty(status) || status == "ทั้งหมด" || status == "ALL" ||
                                   x.status?.Trim().Equals(status.Trim(), StringComparison.OrdinalIgnoreCase) == true;
                    return mStatus && mFrom && mTo;
                }).ToList();
            }
        }

        // 5. Summary Logic (สำหรับกลุ่มรายงานสถิติ)
        var summaryData = rawData
            .GroupBy(x => (x.sectioncreate ?? x.section ?? "N/A").Trim())
            .Select(g => new ReportSummary
            {
                SectionName = sectionDict.TryGetValue(g.Key, out var name) ? name : g.Key,
                CarriedOver = g.Count(x => {
                    var cDate = ParseDate(x.timecreate);
                    var fDate = ParseDate(x.timeclose);
                    return cDate < dStart && (string.IsNullOrEmpty(x.timeclose) || fDate >= dStart);
                }),
                OpenedToday = g.Count(x => {
                    var d = ParseDate(x.timecreate);
                    return d >= dStart && d <= dEnd;
                }),
                AcceptJob = g.Count(x => x.status == "Accept"),
                Repair = g.Count(x => x.status == "Assign"),
                Finished = g.Count(x => (x.status == "Finish" || x.status == "Confirm") &&
                                         !string.IsNullOrEmpty(x.timeclose) &&
                                         ParseDate(x.timeclose) >= dStart && ParseDate(x.timeclose) <= dEnd),
                RejectedToday = g.Count(x => x.status == "Reject" || x.status == "Deny"),
                WaitPart = g.Count(x => (x.status?.Contains("Spare") ?? false) || (x.statustext?.Contains("อะไหล่") ?? false))
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

        // ==========================================================
        // 🛠️ คัดแยกประเภทรายงานแยก View เด็ดขาด (แก้ไขจุดบั๊กหลัก)
        // ==========================================================
        string normalizedRptType = rptType?.ToUpper() ?? "DEFAULT";
        string viewName = "";

        // ตั้งค่าหัวกระดาษฟอร์มหลักจากฟังก์ชัน Metadata
        SetReportMetadata(normalizedRptType);

        switch (normalizedRptType)
        {
            case "JTR":
                viewName = "PrintReport_Jobdaily_Checked"; // หน้าสิทธิ์ผู้แจ้งงาน
                ViewBag.DepartmentName = "ผู้แจ้งงานตรวจสอบ";
                break;

            case "JTR_INTERNAL":
                viewName = "PrintReport_Receiver_Checked"; // หน้าสิทธิ์ช่าง/ผู้รับแจ้ง
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

        // 7. ทำการส่ง Model แยกตามสถาปัตยกรรมข้อมูล
        if (normalizedRptType == "JTR" || normalizedRptType == "JTR_INTERNAL")
        {
            var sortedRawData = rawData.OrderByDescending(x => x.job_no ?? x.id).ToList();
            return View(viewName, sortedRawData); // ส่งข้อมูลดิบลิสต์งาน
        }
        else
        {
            return View(viewName, summaryData); // ส่งข้อมูลกลุ่มคำนวณสรุปยอดสถิติ
        }
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