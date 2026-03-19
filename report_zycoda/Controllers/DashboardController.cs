using Microsoft.AspNetCore.Mvc;
using report_zycoda.Models;

namespace report_zycoda.Controllers
{
    public class DashboardController : Controller
    {
        public async Task<IActionResult> Index(string jobtype, string start, string end, string v = "myjob")
        {
            // 1. ส่งค่ากลับไปให้ View รู้ว่าตอนนี้อยู่โหมดไหน
            ViewBag.CurrentMode = v;
            if (string.IsNullOrEmpty(jobtype)) jobtype = "EM,CM";

            List<MaintenanceApiModels> data = new List<MaintenanceApiModels>();

            using (HttpClient client = new HttpClient())
            {
                // 🚩 สำคัญ: ดึง Token มาใส่ (ถ้าไม่มี Token ข้อมูลจะเป็น 0 เพราะ API ไม่ตอบกลับ)
                var token = HttpContext.Session.GetString("ApiToken");
                if (!string.IsNullOrEmpty(token))
                {
                    client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
                }

                // ใส่ Header username/password เพิ่มเติมตามที่ API ต้องการ
                client.DefaultRequestHeaders.Add("username", HttpContext.Session.GetString("ApiUser"));

                // 2. ใช้เส้นทาง farmhouse และยิงแบบ POST เสมอ
                string apiUrl = $"https://farmhouse.zycoda.com/joborderapi/gets?jobtype={jobtype}";
                if (!string.IsNullOrEmpty(start)) apiUrl += $"&start={start}";
                if (!string.IsNullOrEmpty(end)) apiUrl += $"&end={end}";

                var content = new StringContent("", System.Text.Encoding.UTF8, "application/json");
                var response = await client.PostAsync(apiUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    // ป้องกัน Error < โดยการเช็คก่อน Deserialize
                    if (!json.Trim().StartsWith("<"))
                    {
                        data = Newtonsoft.Json.JsonConvert.DeserializeObject<List<MaintenanceApiModels>>(json) ?? new List<MaintenanceApiModels>();

                        // ถ้าเป็นหน้า "ติดตามงาน" ให้กรองเพิ่ม
                        if (v == "followup")
                        {
                            data = data.Where(x => x.status == "Accept" || x.status == "Assigned").ToList();
                        }
                    }
                }
            }

            return View(data); // ส่ง data ไปที่หน้า Index.cshtml
        }
    }
}
