using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

        [Authorize(Roles = "Administrator")]
        public async Task<IActionResult> Index(string jobtype, string start, string end, string Sectioncreate, string Status, string v = "myjob")
        {
            var sessionUser = User.Identity.Name;
            var sessionPass = User.Claims.FirstOrDefault(c => c.Type == "ApiPassword")?.Value;

            // 🚩 1. ดึงสิทธิ์ User มาเตรียมไว้
            var userClass = User.Claims.FirstOrDefault(c => c.Type == "Class")?.Value;
            var sectionOption = User.Claims.FirstOrDefault(c => c.Type == "SectionOption")?.Value;
            var mySection = HttpContext.Session.GetString("Section");

            if (!User.Identity.IsAuthenticated) return RedirectToAction("Login", "Home");

            if (string.IsNullOrEmpty(jobtype)) jobtype = "EM,CM";
            if (string.IsNullOrEmpty(start)) start = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(end)) end = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            ViewBag.Start = start;
            ViewBag.End = end;

            // 🚩 2. รายการหน่วยงานฝ่ายผลิต (Hardcode ตามรูป)
            var productionSections = new List<string> {
                "310012", "310013", "310014", "311010", "311020", "311090",
                "312010", "312020", "312030", "312090", "313010", "313020",
                "313030", "313040", "313090", "314010", "314020", "314030",
                "314040", "314041", "314042", "314090"
            };
            if (!string.IsNullOrEmpty(sectionOption))
            {
                productionSections.AddRange(sectionOption.Split(',').Select(s => s.Trim()));
                productionSections = productionSections.Distinct().ToList();
            }

            // 🚩 3. สร้าง URL (ถ้า Class 3 ไม่ส่ง Sectioncreate เพื่อให้ได้ข้อมูลลูกน้องมาทั้งหมด)
            var apiUrl = $"https://api.zycoda.com/apimpros/get_job_order?plant=FARMHOUSE&jobtype={jobtype}&start={start}&end={end}";

            if (userClass != "3" && !string.IsNullOrEmpty(Sectioncreate))
            {
                apiUrl += $"&sectioncreate={Sectioncreate}";
            }

            // อ่าน JSON Master
            var jsonsection = System.IO.File.ReadAllText("wwwroot/data/section.json");
            ViewBag.Section = JsonSerializer.Deserialize<List<Models.SectionApiModels>>(jsonsection);

            var jsonstatus = System.IO.File.ReadAllText("wwwroot/data/status.json");
            var statusMaster = JsonSerializer.Deserialize<List<Models.StatusApiModels>>(jsonstatus);
            ViewBag.Status = statusMaster;

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

                    if (userClass == "3")
                    {
                        data = allData.Where(x => {
                            // 🚩 ตรวจสอบว่า x.usercreate เป็น Object (JObject) หรือไม่
                            if (x.usercreate is Newtonsoft.Json.Linq.JObject jo)
                            {
                                var sectionOfUser = jo["section"]?.ToString();
                                // ถ้ามีหน่วยงานในก้อน User ให้เช็คว่าอยู่ในกลุ่มที่อนุญาตไหม
                                if (!string.IsNullOrEmpty(sectionOfUser) && productionSections.Contains(sectionOfUser))
                                    return true;
                            }

                            // เช็คจาก section หลักของใบงาน (อันนี้ชัวร์สุด)
                            if (!string.IsNullOrEmpty(x.section) && productionSections.Contains(x.section))
                                return true;

                            return false;
                        }).ToList();
                    }
                    else
                    {
                        data = allData;
                    }
                }
            }

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
        public async Task<IActionResult> ReportSender(string start, string end, string Status)
        {
            var sessionUser = User.Identity.Name;
            var sessionPass = User.Claims.FirstOrDefault(c => c.Type == "ApiPassword")?.Value;

            var userClass = User.Claims.FirstOrDefault(c => c.Type == "Class")?.Value;
            var sectionOption = User.Claims.FirstOrDefault(c => c.Type == "SectionOption")?.Value;
            var mySection = HttpContext.Session.GetString("Section");

            if (string.IsNullOrEmpty(start)) start = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(end)) end = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            // 1. 🚩 รวมร่าง Section ทั้งหมด (ทั้ง Hardcode และจาก Profile ของคุณเยาวภา)
            var allowedList = new List<string>
    {
        "310012", "310013", "310014", "311010", "311020", "311090",
        "312010", "312020", "312030", "312090", "313010", "313020",
        "313030", "313040", "313090", "314010", "314020", "314030",
        "314040", "314041", "314042", "314090"
    };

            if (!string.IsNullOrEmpty(sectionOption))
            {
                var options = sectionOption.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s));
                allowedList.AddRange(options);
            }

            // ทำเป็น String ยาวๆ คั่นด้วย comma
            var finalSectionsCsv = string.Join(",", allowedList.Distinct());

            // 2. 🚩 สร้าง URL API (เน้นส่ง Parameter ให้ครบตาม Swagger)
            // เพิ่ม jobtype=EM,CM เพื่อให้ดึงงานซ่อมออกมาให้หมด
            var apiUrl = $"https://api.zycoda.com/apimpros/get_job_order?plant=FARMHOUSE&jobtype=EM,CM&start={start}&end={end}";

            if (!string.IsNullOrEmpty(Status)) apiUrl += $"&status={Status}";

            // 🚩 จุดตาย: ถ้าเป็น Class 3 ต้องส่ง 'sectioncreate' ยาวๆ ไปเลย
            if (userClass == "3")
            {
                apiUrl += $"&sectioncreate={finalSectionsCsv}";
            }
            else if (!string.IsNullOrEmpty(mySection))
            {
                apiUrl += $"&section={mySection}";
            }

            // --- ส่วนดึงข้อมูล API ---
            var jsonstatus = System.IO.File.ReadAllText("wwwroot/data/status.json");
            ViewBag.Status = System.Text.Json.JsonSerializer.Deserialize<List<Models.StatusApiModels>>(jsonstatus);

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
                    // ✅ ดึงมา "ทั้งหมด" ที่ API ส่งมาให้ โดยไม่ต้อง .Where ซ้ำในนี้แล้ว
                    // เพราะถ้า .Where ในนี้ แล้ว Logic JObject ผิด ข้อมูลจะหายเหลือแค่ 4 รายการแบบที่เจอครับ
                    data = Newtonsoft.Json.JsonConvert.DeserializeObject<List<MaintenanceApiModels>>(json) ?? new List<MaintenanceApiModels>();
                }
            }

            ViewBag.Start = start;
            ViewBag.End = end;
            ViewBag.CurrentStatus = Status;
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