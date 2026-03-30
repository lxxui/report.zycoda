using Microsoft.AspNetCore.Mvc;
using report_zycoda.Models;

public class ReportController : Controller
{
    public async Task<IActionResult> PrintReport(string start, string end, string status, string sectionFrom, string sectionTo, string rptType)
    {
        // 1. ดึงข้อมูล Auth จาก Session (เหมือนเดิม)
        var sessionUser = HttpContext.Session.GetString("ApiUser");
        var sessionPass = HttpContext.Session.GetString("ApiPass");

        if (string.IsNullOrEmpty(sessionUser)) return Content("Session Expired. Please login again.");

        // 2. เตรียม Query Params (เหมือนเดิม)
        var queryParams = new List<string> { "plant=FARMHOUSE", "jobtype=EM,CM" };
        if (!string.IsNullOrEmpty(start)) queryParams.Add($"start={start}");
        if (!string.IsNullOrEmpty(end)) queryParams.Add($"end={end}");

        var apiUrl = $"https://api.zycoda.com/apimpros/get_job_order?{string.Join("&", queryParams)}";
        List<MaintenanceApiModels> data = new List<MaintenanceApiModels>();

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
                    var allData = Newtonsoft.Json.JsonConvert.DeserializeObject<List<MaintenanceApiModels>>(json) ?? new List<MaintenanceApiModels>();

                    // --- 🚩 กรอง Status ---
                    if (!string.IsNullOrEmpty(status) && status.ToUpper() != "ALL" && status != "ทั้งหมด")
                    {
                        allData = allData.Where(x => x.status?.Trim().Equals(status.Trim(), StringComparison.OrdinalIgnoreCase) == true).ToList();
                    }

                    // --- 🚩 กรอง Section ---
                    if (!string.IsNullOrEmpty(sectionFrom) && sectionFrom != "ทั้งหมด" && !string.IsNullOrEmpty(sectionTo) && sectionTo != "ทั้งหมด")
                    {
                        data = allData.Where(x => !string.IsNullOrEmpty(x.sectioncreate) &&
                            string.Compare(x.sectioncreate.Trim(), sectionFrom.Trim()) >= 0 &&
                            string.Compare(x.sectioncreate.Trim(), sectionTo.Trim()) <= 0).ToList();
                    }
                    else { data = allData; }
                }
            }
            catch (Exception ex) { ViewBag.ErrorMessage = "API Error: " + ex.Message; }
        }

        // 3. ส่งค่าไปแสดงที่หัวรายงาน (ViewBag)
        ViewBag.SectionFrom = string.IsNullOrEmpty(sectionFrom) ? "ทั้งหมด" : sectionFrom;
        ViewBag.SectionTo = string.IsNullOrEmpty(sectionTo) ? "ทั้งหมด" : sectionTo;
        ViewBag.StartDate = start;
        ViewBag.EndDate = end;
        ViewBag.Status = string.IsNullOrEmpty(status) ? "ทั้งหมด" : status;
        ViewBag.ReportType = rptType;

        // กำหนดค่ากลางก่อน
        ViewBag.PrintDate = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
        ViewBag.RefNo = "RE-ZY-" + DateTime.Now.ToString("yyyyMMdd-HHmm");

        // แยกรายละเอียดตามประเภท (rptType)
        switch (rptType?.ToUpper())
        {
            case "HOTLINE":
                ViewBag.Title = "รายงานตรวจสอบงานแจ้งซ่อม (Hot Line)";
                ViewBag.CodeForm = "FM-ENG-014"; // เลขฟอร์มของ Hotline
                break;

            case "PM":
                ViewBag.Title = "รายงานแผนการบำรุงรักษาเชิงป้องกัน (PM)";
                ViewBag.CodeForm = "FM-ENG-015"; // เลขฟอร์มของ PM
                break;

            case "MONTHLY":
                ViewBag.Title = "รายงานสรุปงานซ่อมบำรุงประจำเดือน";
                ViewBag.CodeForm = "FM-ENG-020"; // เลขฟอร์มของ Monthly
                break;

            default:
                ViewBag.Title = "รายงานตรวจสอบของผู้รับแจ้งงาน";
                ViewBag.CodeForm = "FM-ENG-XXX";
                break;
        }

        // --- 🚩 จบส่วนแยกหัวข้อ ---

        // จากนั้นค่อยส่งไปที่ View ตามเดิม
        return View(rptType switch
        {
            "HOTLINE" => "PrintHotlineReport",
            "PM" => "PrintPMReport",
            "MONTHLY" => "PrintMonthlyReport",
            _ => "PrintReport_Receiver_Checked"
        }, data);
    }
}