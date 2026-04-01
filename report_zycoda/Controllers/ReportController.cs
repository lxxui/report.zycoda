using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using report_zycoda.Models;
using System.Text.Json;

public class ReportController : Controller
{
    public async Task<IActionResult> PrintReport(string start, string end, string status, string sectionFrom, string sectionTo, string rptType)
    {
        // 1. ตรวจสอบ Session
        var sessionUser = HttpContext.Session.GetString("ApiUser");
        var sessionPass = HttpContext.Session.GetString("ApiPass");
        var sessionName = HttpContext.Session.GetString("ApiName");

        if (string.IsNullOrEmpty(sessionUser))
            return Content("Session Expired. Please login again.");

        // 2. โหลด Master Section
        var jsonsection = System.IO.File.ReadAllText("wwwroot/data/section.json");
        var sectionList = System.Text.Json.JsonSerializer.Deserialize<List<SectionApiModels>>(jsonsection)
                          ?? new List<SectionApiModels>();

        var sectionDict = sectionList
            .Where(s => !string.IsNullOrEmpty(s.section))
            .ToDictionary(s => s.section.Trim(), s => s.name ?? s.section);

        // 3. เตรียม API
        var queryParams = new List<string> { "plant=FARMHOUSE", "jobtype=EM,CM" };
        if (!string.IsNullOrEmpty(start)) queryParams.Add($"start={start}");
        if (!string.IsNullOrEmpty(end)) queryParams.Add($"end={end}");

        var apiUrl = $"https://api.zycoda.com/apimpros/get_job_order?{string.Join("&", queryParams)}";
        List<MaintenanceApiModels> rawData = new();

        using (HttpClient client = new HttpClient())
        {
            client.DefaultRequestHeaders.Add("username", sessionUser);
            client.DefaultRequestHeaders.Add("password", sessionPass);

            try
            {
                var response = await client.GetAsync(apiUrl);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var allData = JsonConvert.DeserializeObject<List<MaintenanceApiModels>>(json)
                                  ?? new List<MaintenanceApiModels>();

                    // Filter status
                    if (!string.IsNullOrEmpty(status) && status.ToUpper() != "ALL" && status != "ทั้งหมด")
                    {
                        allData = allData.Where(x =>
                            x.status?.Trim().Equals(status.Trim(), StringComparison.OrdinalIgnoreCase) == true
                        ).ToList();
                    }

                    // Filter section range
                    if (!string.IsNullOrEmpty(sectionFrom) && sectionFrom != "ทั้งหมด" &&
                        !string.IsNullOrEmpty(sectionTo) && sectionTo != "ทั้งหมด")
                    {
                        rawData = allData.Where(x =>
                            !string.IsNullOrEmpty(x.sectioncreate) &&
                            string.Compare(x.sectioncreate.Trim(), sectionFrom.Trim()) >= 0 &&
                            string.Compare(x.sectioncreate.Trim(), sectionTo.Trim()) <= 0
                        ).ToList();
                    }
                    else
                    {
                        rawData = allData;
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = "API Error: " + ex.Message;
            }
        }

        // 4. Summary Logic (ปรับปรุงตาม Logic เก่าที่ฟายให้มา)
        var atDate = DateTime.Now.Date; // กำหนดค่า vdate_at

        var summaryData = rawData
        .GroupBy(x => x.sectioncreate?.Trim() ?? "ไม่ระบุแผนก")
        .Select(g => new ReportSummary
        {
            SectionName = sectionDict.TryGetValue(g.Key, out var name) ? name : g.Key,

            // 1. ยกมา: แจ้งก่อนวันนี้ และ (ยังไม่ปิดงาน หรือ ปิดงานตั้งแต่วันนี้เป็นต้นไป)
            // และมีสถานะเป็น Create หรือ Accept หรือ Assign
            CarriedOver = g.Count(x =>
                ParseDate(x.timecreate) < atDate &&
                (string.IsNullOrEmpty(x.timeclose) || ParseDate(x.timeclose) >= atDate) &&
                (x.status == "Create" || x.status == "Accept" || x.status == "Assign")
),

            // 2. เปิดเพิ่ม: ใบงานที่เปิด "วันนี้" และสถานะยังเป็น Create
            OpenedToday = g.Count(x =>
                ParseDate(x.timecreate) == atDate &&
                x.status == "Create"
            ),

            // 3. กำลังซ่อม: งานที่ถูก Assign หรือ Accept ไปแล้ว แต่ยังซ่อมไม่เสร็จ
            Repair = g.Count(x =>
                (x.status == "Assign") &&
                string.IsNullOrEmpty(x.timeclose)
            ),

            // 4. เสร็จสิ้น: งานที่สถานะเป็น Finish และปิดงานวันนี้
            Finished = g.Count(x =>
                x.status == "Finish" &&
                !string.IsNullOrEmpty(x.timeclose) &&
                ParseDate(x.timeclose) == atDate
            ),

            // 5. ยกเลิก: ใบงานที่โดน Reject
            RejectedToday = g.Count(x => x.status == "Reject"),

            // 6. รออะไหล่: ดักจากคำว่า Spare หรือ อะไหล่
            WaitPart = g.Count(x =>
                (x.status != null && x.status.Contains("Spare")) ||
                (x.statustext != null && x.statustext.Contains("อะไหล่"))
            )
        })
        .OrderBy(x => x.SectionName)
        .ToList();

        // 5. ViewBag Setup
        ViewBag.SectionFrom = string.IsNullOrEmpty(sectionFrom) ? "ทั้งหมด" : sectionFrom;
        ViewBag.SectionTo = string.IsNullOrEmpty(sectionTo) ? "ทั้งหมด" : sectionTo;
        ViewBag.StartDate = start;
        ViewBag.EndDate = end;
        ViewBag.Status = string.IsNullOrEmpty(status) ? "ทั้งหมด" : status;
        ViewBag.ApiUser = sessionName ?? sessionUser;
        ViewBag.PrintDate = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
        ViewBag.RefNo = "RE-ZY-" + DateTime.Now.ToString("yyyyMMdd-HHmm");

        // 6. Report Type Mapping
        switch (rptType?.ToUpper())
        {
            case "JTR":
                ViewBag.Title = "รายงานติดตามความคืบหน้า (สำหรับผู้รับงานตรวจสอบ)";
                ViewBag.CodeForm = "FM-ENG-001";
                break;
            case "JTR_INTERNAL":
                ViewBag.Title = "รายงานติดตามความคืบหน้า (สำหรับผู้แจ้งงานตรวจสอบ)";
                ViewBag.CodeForm = "FM-ENG-002";
                break;
            case "HOTLINE":
                ViewBag.Title = "รายงานตรวจสอบงานแจ้งซ่อม (Hot Line)";
                ViewBag.CodeForm = "FM-ENG-003";
                break;
            case "PM":
                ViewBag.Title = "รายงานแผนการบำรุงรักษาเชิงป้องกัน (PM)";
                ViewBag.CodeForm = "FM-ENG-004";
                break;
            case "MONTHLY":
                ViewBag.Title = "รายงานสรุปงานซ่อมบำรุงประจำเดือน";
                ViewBag.CodeForm = "FM-ENG-005";
                break;
            default:
                ViewBag.Title = "รายงานตรวจสอบของผู้รับแจ้งงาน";
                ViewBag.CodeForm = "FM-ENG-XXX";
                break;
        }

        // 7. View Dispatcher
        string viewPath = rptType?.ToUpper() switch
        {
            "JTR" => "PrintReport_Receiver_Checked",
            "JTR_INTERNAL" => "PrintReport_Jobdaily_Checked",
            "HOTLINE" => "PrintHotlineReport",
            "PM" => "PrintPMReport",
            "MONTHLY" => "PrintMonthlyReport",
            _ => "PrintReport_Receiver_Checked"
        };

        if (rptType == "JTR" || rptType == "JTR_INTERNAL")
        {
            return View(viewPath, rawData);
        }
        else
        {
            return View(viewPath, summaryData);
        }
    }

    // Helper Method สำหรับจัดการวันที่ใน JSON (ISO Format)
    private DateTime? ParseDate(string dateStr)
    {
        if (string.IsNullOrEmpty(dateStr)) return null;
        if (DateTime.TryParse(dateStr, out DateTime dt))
        {
            return dt.Date; // เอาเฉพาะวันที่มาเทียบ
        }
        return null;
    }
}