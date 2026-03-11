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

            if (string.IsNullOrEmpty(start)) start = DateTime.Today.ToString("yyyy-MM-dd");
            if (string.IsNullOrEmpty(end)) end = DateTime.Today.ToString("yyyy-MM-dd");

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
            var sectioncreates = JsonSerializer.Deserialize<List<Models.Sectioncreate>>(jsonsectioncreate);
            ViewBag.Section = sectioncreates;

            // ✅ อ่าน status.json
            var jsonstatus = System.IO.File.ReadAllText("wwwroot/data/status.json");
            var status = JsonSerializer.Deserialize<List<Models.Status>>(jsonstatus);
            ViewBag.Status = status;



            List<MaintenanceReport> data = new List<MaintenanceReport>();

            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("username", "723582");
                client.DefaultRequestHeaders.Add("password", "3582");

                var response = await client.GetAsync(apiUrl);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var allData = JsonSerializer.Deserialize<List<MaintenanceReport>>(json) ?? new List<MaintenanceReport>();

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