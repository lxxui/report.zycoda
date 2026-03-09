using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using report_zycoda.Models;
using static System.Collections.Specialized.BitVector32;

namespace report_zycoda.Controllers
{
    public class MaintenanceController : Controller
    {
        public async Task<IActionResult> Index(string jobtype, string start, string end ,string section,string statustext)
        {
            if (string.IsNullOrEmpty(jobtype))jobtype = "EM,CM";

            if (string.IsNullOrEmpty(start)) start = DateTime.Today.ToString("yyyy-MM-dd");
            if (string.IsNullOrEmpty(end)) end = DateTime.Today.ToString("yyyy-MM-dd");

            // เพิ่ม parameter statustext และ section เข้าไปใน URL
            var apiUrl = $"https://api.zycoda.com/apimpros/get_job_order?plant=FARMHOUSE&jobtype={jobtype}&start={start}&end={end}";

            if (!string.IsNullOrEmpty(section))
            {
                apiUrl += $"&section={section}";
            }

            if (!string.IsNullOrEmpty(statustext))
            {
                apiUrl += $"&statustext={statustext}";
            }


            // ✅ อ่าน sections.json
            var jsonSection = System.IO.File.ReadAllText("wwwroot/data/sections.json");

            var sections = JsonSerializer.Deserialize<List<Models.Section>>(jsonSection);

            ViewBag.Sections = sections;

            List<MaintenanceReport> data = new List<MaintenanceReport>();

            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("username", "723582");
                client.DefaultRequestHeaders.Add("password", "3582");

                var response = await client.GetAsync(apiUrl);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();

                    data = JsonSerializer.Deserialize<List<MaintenanceReport>>(json)
                           ?? new List<MaintenanceReport>();
                }
            }

            return View(data);
        }
    }
}