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

        // 2. จัดการตัวแปรวันที่ (ป้องกัน Error ตัวแปรซ้ำ)
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

        // 4. เรียก API (ใช้ Endpoint หลัก)
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

                // กรองข้อมูลเบื้องต้นตาม Range แผนกที่เลือกหน้า UI
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

        // 5. 🛠️ Summary Logic (แก้ปัญหาข้อมูลปนกัน)
        var summaryData = rawData
            .GroupBy(x => (x.sectioncreate ?? x.section ?? "N/A").Trim())
            .Select(g => new ReportSummary
            {
                SectionName = sectionDict.TryGetValue(g.Key, out var name) ? name : g.Key,

                // ✅ ยอดยกมา: งานที่สร้างก่อนวันที่ Start และ (ยังไม่ปิด หรือ ปิดหลังจากเริ่ม Start)
                CarriedOver = g.Count(x => {
                    var cDate = ParseDate(x.timecreate);
                    var fDate = ParseDate(x.timeclose);
                    return cDate < dStart && (string.IsNullOrEmpty(x.timeclose) || fDate >= dStart);
                }),

                // ✅ เปิดเพิ่ม: เฉพาะงานที่สร้าง "ภายในช่วงวันที่เลือก" (ไม่เอาของเก่ามาปน)
                OpenedToday = g.Count(x => {
                    var d = ParseDate(x.timecreate);
                    return d >= dStart && d <= dEnd;
                }),

                AcceptJob = g.Count(x => x.status == "Accept"),
                Repair = g.Count(x => x.status == "Assign"),

                // เสร็จสิ้น: งานที่กด Finish/Confirm ในช่วงวันที่เลือก
                Finished = g.Count(x => (x.status == "Finish" || x.status == "Confirm") &&
                                         !string.IsNullOrEmpty(x.timeclose) &&
                                         ParseDate(x.timeclose) >= dStart && ParseDate(x.timeclose) <= dEnd),

                // ✅ เพิ่ม Deny เข้ามาในช่องยกเลิก
                RejectedToday = g.Count(x => x.status == "Reject" || x.status == "Deny"),

                WaitPart = g.Count(x => (x.status?.Contains("Spare") ?? false) || (x.statustext?.Contains("อะไหล่") ?? false))
            })
            .OrderBy(x => x.SectionName).ToList();

        // 6. ส่งค่าไป View
        ViewBag.ApiUser = User.Claims.FirstOrDefault(c => c.Type == "FullName")?.Value ?? sessionUser;
        ViewBag.RefNo = $"{DateTime.Now:yyMMdd}-{new Random().Next(100, 999)}";
        ViewBag.PrintDate = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
        ViewBag.StartDate = sDate;
        ViewBag.EndDate = eDate;
        ViewBag.SectionFrom = sectionFrom;
        ViewBag.SectionTo = sectionTo;
        SetReportMetadata(rptType);

        string viewName = rptType?.ToUpper() switch
        {
            "HOTLINE" => "PrintHotlineReport",
            "JTR_INTERNAL" => "PrintReport_Jobdaily_Checked",
            _ => "PrintReport_Receiver_Checked"
        };

        if (rptType == "JTR" || rptType == "JTR_INTERNAL") return View(viewName, rawData.OrderByDescending(x => x.job_no).ToList());
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