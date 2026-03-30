using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using report_zycoda.Models;
using System.Globalization;
using System.Text;
using System.Text.Json;
using static System.Collections.Specialized.BitVector32;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace report_zycoda.Controllers
{
    public class MaintenanceController : Controller
    {

        //[Authorize(Roles = "Administrator")]
        public async Task<IActionResult> Index(string jobtype, string start, string end, string Sectioncreate, string Status, string v = "all")
        {
            if (User.Identity == null || !User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Login", "Home");
            }

            var sessionUser = User.Identity.Name;

            var sessionPass = User.Claims.FirstOrDefault(c => c.Type == "ApiPassword")?.Value;

            if (!User.Identity.IsAuthenticated) return RedirectToAction("Login", "Home");

            if (string.IsNullOrEmpty(jobtype)) jobtype = "EM,CM";
            if (string.IsNullOrEmpty(start)) start = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(end)) end = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            ViewBag.Start = start;
            ViewBag.End = end;

            // -------------------------
            // 🔥 1. โหลด Section ตาม Group (จาก JSON)
            // -------------------------
            List<string> selectedSections = new();

            if (!string.IsNullOrEmpty(Sectioncreate))
            {
                string? fileName = Sectioncreate switch
                {
                    "engineer_lkb" => "section_engi_lkb.json",
                    "engineer_bc" => "section_engi_bc.json",
                    "product_lkb" => "section_product_lkb.json",
                    "product_bc" => "section_product_bc.json",
                    "other" => "section.json",
                    _ => null
                };

                if (!string.IsNullOrEmpty(fileName))
                {
                    var path = Path.Combine("wwwroot", "data", fileName);

                    if (System.IO.File.Exists(path))
                    {
                        var json = System.IO.File.ReadAllText(path);

                        var list = JsonSerializer.Deserialize<List<Models.SectionApiModels>>(json)
                                   ?? new List<Models.SectionApiModels>();

                        selectedSections = list
                            .Where(x => !string.IsNullOrEmpty(x.section))
                            .Select(x => x.section!) // 🔥 บอก compiler ว่าไม่ null แล้ว
                            .ToList();
                    }
                }
                else
                {
                    // กรณีเลือก section เดี่ยว
                    selectedSections.Add(Sectioncreate);
                }
            }

            // -------------------------
            // 🔹 Query Params (ยิงกว้างสุด)
            // -------------------------
            var queryParams = new Dictionary<string, string>
    {
        { "plant", "FARMHOUSE" },
        { "jobtype", jobtype },
        { "start", start },
        { "end", end },
        { "v", v },
        { "status", "ALL" } // 🔥 ดึงทั้งหมดก่อน
    };

            var baseUrl = "https://api.zycoda.com/apimpros/get_job_order";
            var apiUrl = QueryHelpers.AddQueryString(baseUrl, queryParams);

            // -------------------------
            // 🔹 โหลด master
            // -------------------------
            var jsonsection = System.IO.File.ReadAllText("wwwroot/data/section.json");
            ViewBag.Section = JsonSerializer.Deserialize<List<Models.SectionApiModels>>(jsonsection);

            var jsonstatus = System.IO.File.ReadAllText("wwwroot/data/status.json");
            ViewBag.Status = JsonSerializer.Deserialize<List<Models.StatusApiModels>>(jsonstatus);

            List<MaintenanceApiModels> data = new();

            using (HttpClient client = new HttpClient())
            {
                if (string.IsNullOrEmpty(sessionPass))
                    return RedirectToAction("Login", "Home");

                client.DefaultRequestHeaders.Add("username", sessionUser);
                client.DefaultRequestHeaders.Add("password", sessionPass);

                var response = await client.GetAsync(apiUrl);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    data = Newtonsoft.Json.JsonConvert.DeserializeObject<List<MaintenanceApiModels>>(json) ?? new();
                }
            }

            // -------------------------
            // 🔥 2. Filter Section (ฝั่งเรา)
            // -------------------------
            if (selectedSections.Any())
            {
                data = data
                    .Where(x => !string.IsNullOrEmpty(x.section) &&
                                selectedSections.Contains(x.section))
                    .ToList();
            }

            // -------------------------
            // 🔥 3. Filter Status (ฝั่งเรา)
            // -------------------------
            if (!string.IsNullOrEmpty(Status))
            {
                data = data
                    .Where(x => !string.IsNullOrEmpty(x.status) &&
                                x.status.Equals(Status, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            // -------------------------
            // 🧪 Debug
            // -------------------------
            ViewBag.DebugUrl = apiUrl;
            ViewBag.CurrentStatus = Status;
            ViewBag.CurrentSection = Sectioncreate;

            Console.WriteLine($"API URL: {apiUrl}");
            Console.WriteLine($"Section Group: {Sectioncreate}");
            Console.WriteLine($"Section Count: {selectedSections.Count}");
            Console.WriteLine($"Final Data Count: {data.Count}");

            return View(data);
        }

        [Authorize] // 🚩 1. เพิ่มเพื่อให้ระบบเช็คตั๋ว (Cookie) ก่อนเข้า
        public async Task<IActionResult> ReportReceiver(string jobtype, string start, string end, string Sectioncreate, string Status)
        {
            // 1. ตรวจสอบการ Login
            if (!User.Identity.IsAuthenticated) return RedirectToAction("Login", "Home");

            // 2. ดึงข้อมูลจาก Claims
            var userSection = User.Claims.FirstOrDefault(c => c.Type == "Section")?.Value;
            var userClass = User.Claims.FirstOrDefault(c => c.Type == "UserClass")?.Value;
            var sessionUser = User.Identity.Name;
            var sessionPass = User.Claims.FirstOrDefault(c => c.Type == "ApiPassword")?.Value;

            // 🚩 3. Fixed รายการ Section ที่มีสิทธิ์ "รับแจ้ง" (อ้างอิงจาก JSON ที่คุณให้มา)
            var allowedSections = new List<string>
                {
                    "310089", "310098", "310099", "317020", "317030", "317040", "317050",
                    "317110", "317120", "317130", "317140", "317150", "317210", "317220",
                    "317230", "317240", "317310", "317320", "317330", "317340", "317350",
                    "317360", "317371", "317372", "317374", "317375", "317380", "317381",
                    "318050", "318060", "318070", "320100", "ITRECEIVE"
                };

            // 🚩 4. Check สิทธิ์: ต้องเป็น Class 3 และอยู่ในหน่วยงานที่กำหนดเท่านั้น
            if (userClass != "3" || string.IsNullOrEmpty(userSection) || !allowedSections.Contains(userSection))
            {
                // ถ้าไม่เข้าเงื่อนไข ให้ดีดออกไปหน้า Login หรือแสดงข้อความเตือน
                return Content("ขออภัย คุณไม่มีสิทธิ์เข้าใช้งานหน้ารับแจ้ง (เฉพาะเจ้าหน้าที่เทคนิค Class 3 เท่านั้น)");
            }

            // --- ส่วน Logic การดึงข้อมูล (คงเดิม) ---
            if (string.IsNullOrEmpty(jobtype)) jobtype = "EM,CM";
            if (string.IsNullOrEmpty(start)) start = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(end)) end = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            ViewBag.Start = start;
            ViewBag.End = end;

            var apiUrl = $"https://api.zycoda.com/apimpros/get_job_order?plant=FARMHOUSE&jobtype={jobtype}&start={start}&end={end}";

            // โหลด Dropdown
            var jsonsectioncreate = System.IO.File.ReadAllText("wwwroot/data/section.json");
            ViewBag.Section = JsonSerializer.Deserialize<List<Models.SectionApiModels>>(jsonsectioncreate);
            var jsonstatus = System.IO.File.ReadAllText("wwwroot/data/status.json");
            ViewBag.Status = JsonSerializer.Deserialize<List<Models.StatusApiModels>>(jsonstatus);

            List<MaintenanceApiModels> data = new List<MaintenanceApiModels>();

            using (HttpClient client = new HttpClient())
            {
                if (string.IsNullOrEmpty(sessionPass)) return RedirectToAction("Login", "Home");
                client.DefaultRequestHeaders.Add("username", sessionUser);
                client.DefaultRequestHeaders.Add("password", sessionPass);

                var response = await client.GetAsync(apiUrl);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var allData = Newtonsoft.Json.JsonConvert.DeserializeObject<List<MaintenanceApiModels>>(json) ?? new List<MaintenanceApiModels>();

                    // 🚩 5. กรองให้เห็นเฉพาะงานที่ "ส่งมาที่แผนกของฉัน"
                    // เช่น ถ้าฉันอยู่ 'ITRECEIVE' ก็จะเห็นเฉพาะงานที่ x.section == 'ITRECEIVE'
                    data = allData.Where(x => string.Equals(x.section, userSection, StringComparison.OrdinalIgnoreCase)).ToList();

                    // กรองเพิ่มเติมตามตัวเลือกหน้าเว็บ (ถ้ามีการกดค้นหา)
                    if (!string.IsNullOrEmpty(Status))
                        data = data.Where(x => string.Equals(x.status, Status, StringComparison.OrdinalIgnoreCase)).ToList();
                }
            }

            return View(data);
        }


        public async Task<IActionResult> ReportSender(string start, string end, string Status, string section)
        {
            var sessionUser = User.Identity.Name;
            var sessionPass = User.Claims.FirstOrDefault(c => c.Type == "ApiPassword")?.Value;
            var userClass = User.Claims.FirstOrDefault(c => c.Type == "Class")?.Value;
            var sectionOption = User.Claims.FirstOrDefault(c => c.Type == "SectionOption")?.Value;
            var mySection = User.Claims.FirstOrDefault(c => c.Type == "UserSectionCode")?.Value;

            if (string.IsNullOrEmpty(sessionPass))
                return RedirectToAction("Login", "Home");

            start ??= DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            end ??= DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            // -------------------------
            // 1. รวม Section
            // -------------------------
            var allowedList = new List<string>();

            if (!string.IsNullOrEmpty(section))
            {
                allowedList.Add(section);
            }
            else
            {
                if (!string.IsNullOrEmpty(mySection))
                    allowedList.Add(mySection);

                if (!string.IsNullOrEmpty(sectionOption))
                {
                    allowedList.AddRange(sectionOption.Split(',').Select(s => s.Trim()));
                }

                if (userClass == "3")
                {
                    allowedList.AddRange(new[]
                    {
                "310012","310013","310014","311010","311020","311090",
                "312010","312020","312030","312090","313010","313020",
                "313030","313040","313090","314010","314020","314030",
                "314040","314041","314042","314090"
            });
                }
            }

            var finalSectionsCsv = string.Join(",", allowedList.Distinct());

            // -------------------------
            // 2. Base Params
            // -------------------------
            var baseParams = new Dictionary<string, string>
    {
        { "plant", "FARMHOUSE" },
        { "start", start },
        { "end", end },
        { "status", string.IsNullOrEmpty(Status) ? "ALL" : Status }
    };

            if (!string.IsNullOrEmpty(finalSectionsCsv))
                baseParams.Add("section", finalSectionsCsv);

            var baseUrl = "https://api.zycoda.com/apimpros/get_job_order";

            // -------------------------
            // 🔥 ยิงหลาย mode
            // -------------------------
            var urlList = new List<string>
    {
        QueryHelpers.AddQueryString(baseUrl, new Dictionary<string,string>(baseParams) { { "v", "all" } }),
        QueryHelpers.AddQueryString(baseUrl, new Dictionary<string,string>(baseParams) { { "v", "myjob" } }),
        QueryHelpers.AddQueryString(baseUrl, new Dictionary<string,string>(baseParams) { { "v", "followup" } })
    };

            // -------------------------
            // โหลด master
            // -------------------------
            var statusPath = Path.Combine("wwwroot", "data", "status.json");
            var sectionPath = Path.Combine("wwwroot", "data", "section.json");

            var statusTask = System.IO.File.ReadAllTextAsync(statusPath);
            var sectionTask = System.IO.File.ReadAllTextAsync(sectionPath);

            List<MaintenanceApiModels> data = new();

            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("username", sessionUser);
                client.DefaultRequestHeaders.Add("password", sessionPass);

                var tasks = urlList.Select(url => client.GetAsync(url)).ToList();

                // ✅ รอ API
                await Task.WhenAll(tasks);

                // ✅ รอ file
                await Task.WhenAll(statusTask, sectionTask);

                var options = new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                ViewBag.Status = System.Text.Json.JsonSerializer.Deserialize<List<Models.StatusApiModels>>(statusTask.Result, options);
                ViewBag.Section = System.Text.Json.JsonSerializer.Deserialize<List<Models.SectionApiModels>>(sectionTask.Result, options);

                var allLists = new List<MaintenanceApiModels>();

                for (int i = 0; i < tasks.Count; i++)
                {
                    if (tasks[i].Result.IsSuccessStatusCode)
                    {
                        var json = await tasks[i].Result.Content.ReadAsStringAsync();
                        var list = Newtonsoft.Json.JsonConvert.DeserializeObject<List<MaintenanceApiModels>>(json) ?? new();

                        Console.WriteLine($"Mode {i} Count: {list.Count}");

                        allLists.AddRange(list);
                    }
                }

                // 🔥 รวม + กันซ้ำ
                data = allLists
                    .GroupBy(x => x.id ?? "")
                    .Select(g => g.First())
                    .ToList();

                Console.WriteLine($"FINAL COUNT: {data.Count}");
            }
            // -------------------------
            // View
            // -------------------------
            ViewBag.DebugUrl = string.Join("\n", urlList);
            ViewBag.Start = start;
            ViewBag.End = end;
            ViewBag.CurrentStatus = Status;
            ViewBag.CurrentSection = section;

            return View(data);
        }
        public async Task<IActionResult> PrintReport(int type, string sectionFrom, string sectionTo, string start, string end, string status)
        {
            // 1. ดึงข้อมูลจาก API โดยใช้ Filter ที่ส่งมา (Logic เดียวกับใน ReportReceiver)
            var sessionUser = HttpContext.Session.GetString("ApiUser");
            var sessionPass = HttpContext.Session.GetString("ApiPass");

            var apiUrl = $"https://api.zycoda.com/apimpros/get_job_order?plant=FARMHOUSE&jobtype=EM,CM";
            if (!string.IsNullOrEmpty(start)) apiUrl += $"&start={start}";
            if (!string.IsNullOrEmpty(end)) apiUrl += $"&end={end}";
            if (!string.IsNullOrEmpty(status)) apiUrl += $"&status={status}";

            List<MaintenanceApiModels> data = new List<MaintenanceApiModels>();

            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("username", sessionUser);
                client.DefaultRequestHeaders.Add("password", sessionPass);

                var response = await client.GetAsync(apiUrl);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    if (json.Trim().StartsWith("["))
                    {
                        var allData = Newtonsoft.Json.JsonConvert.DeserializeObject<List<MaintenanceApiModels>>(json) ?? new List<MaintenanceApiModels>();

                        // กรอง Section From - To ตามที่ User เลือก
                        if (!string.IsNullOrEmpty(sectionFrom) && !string.IsNullOrEmpty(sectionTo))
                        {
                            data = allData.Where(x =>
                                string.Compare(x.sectioncreate, sectionFrom) >= 0 &&
                                string.Compare(x.sectioncreate, sectionTo) <= 0).ToList();
                        }
                        else { data = allData; }
                    }
                }
            }

            // 2. ส่งค่าหัวรายงานไปที่ ViewBag
            ViewBag.SectionFrom = sectionFrom;
            ViewBag.SectionTo = sectionTo;
            ViewBag.StartDate = start;
            ViewBag.EndDate = end;
            ViewBag.Status = status ?? "ALL";

            return View("~/Views/report/PrintReport.cshtml", data);
        }
    }
}