using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using report_zycoda.Models;
using static System.Collections.Specialized.BitVector32;

namespace report_zycoda.Controllers
{
    public class MaintenanceController : Controller
    {

        public async Task<IActionResult> Index(string jobtype, string start, string end ,string Sectioncreate, string Status)
        {
            if (string.IsNullOrEmpty(jobtype))jobtype = "EM,CM";

            // ถ้าไม่มีการส่งค่าวันที่มา (เปิดหน้าแรก) ให้ใช้ค่าวันที่ปัจจุบัน
            var startDate = string.IsNullOrEmpty(start) ? DateTime.Now.ToString("dd/MM/yyyy") : start;
            var endDate = string.IsNullOrEmpty(end) ? DateTime.Now.ToString("dd/MM/yyyy") : end;

            ViewBag.Start = startDate;
            ViewBag.End = endDate;

            // เพิ่ม parameter statustext และ sectioncreate เข้าไปใน URL
            var apiUrl = $"https://api.zycoda.com/apimpros/get_job_order?plant=FARMHOUSE&jobtype={jobtype}&start={start}&end={end}";

            if (!string.IsNullOrEmpty(Sectioncreate))
            {
                apiUrl += $"&sectioncreate={Sectioncreate}";
            }

            if (!string.IsNullOrEmpty(Status))
            {
                apiUrl += $"&status={Status}";
            }


            // ✅ อ่าน sectioncreates.json
            var jsonsectioncreate = System.IO.File.ReadAllText("wwwroot/data/section.json");
            var sectioncreates = JsonSerializer.Deserialize<List<Models.SectionApiModels>>(jsonsectioncreate);
            ViewBag.Section = sectioncreates;

            // ✅ อ่าน status.json
            var jsonstatus = System.IO.File.ReadAllText("wwwroot/data/status.json");
            var status = JsonSerializer.Deserialize<List<Models.StatusApiModels>>(jsonstatus);
            ViewBag.Status = status;



            List<MaintenanceApiModels> data = new List<MaintenanceApiModels>();

            using (HttpClient client = new HttpClient())
            {
                // 🚩 1. ดึงค่าจาก Session ที่เราเก็บไว้ตอน Login (สะกด Key ให้ตรงกับ HomeController)
                var sessionUser = HttpContext.Session.GetString("ApiUser");
                var sessionPass = HttpContext.Session.GetString("ApiPass");

                // 🚩 2. เช็คกันเหนียว: ถ้าไม่มี Session (อาจจะแอบเข้าหน้า Index ตรงๆ) ให้ไล่กลับไปหน้า Login
                if (string.IsNullOrEmpty(sessionUser))
                {
                    return RedirectToAction("Login", "Home");
                }

                // 🚩 3. เอาค่าที่ดึงได้มาใส่ใน Header แทนเลขที่ Fixed ไว้
                client.DefaultRequestHeaders.Add("username", sessionUser);
                client.DefaultRequestHeaders.Add("password", sessionPass);

                var response = await client.GetAsync(apiUrl);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var allData = JsonSerializer.Deserialize<List<MaintenanceApiModels>>(json) ?? new List<MaintenanceApiModels>();

                    // เริ่มต้นด้วยข้อมูลทั้งหมด
                    var filteredData = allData.AsEnumerable();

                    // กรอง Status
                    if (!string.IsNullOrEmpty(Status))
                    {
                        filteredData = filteredData.Where(x => string.Equals(x.status, Status, StringComparison.OrdinalIgnoreCase));
                    }

                    // กรอง Section (กรองเพิ่มจากตัวบน)
                    if (!string.IsNullOrEmpty(Sectioncreate))
                    {
                        // สมมติว่าใน Model มี property ชื่อ sectioncreate
                        filteredData = filteredData.Where(x => string.Equals(x.sectioncreate, Sectioncreate, StringComparison.OrdinalIgnoreCase));
                    }

                    data = filteredData.ToList();
                }
            }

            return View(data);
        }
    }
}