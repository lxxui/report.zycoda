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
            // 🚩 ดึงข้อมูลจาก Identity/Claims (ไม่หายแม้ Session จะ Timeout)
            var sessionUser = User.Identity.Name;
            var sessionPass = User.Claims.FirstOrDefault(c => c.Type == "ApiPassword")?.Value;

            // เช็คว่า Login หรือยัง (ใช้ Identity เช็คจะชัวร์กว่า Session)
            if (!User.Identity.IsAuthenticated) return RedirectToAction("Login", "Home");

            // --- ส่วน Logic เดิมของคุณ ---
            if (string.IsNullOrEmpty(jobtype)) jobtype = "EM,CM";

            // 1. ตั้งค่าวันที่ปัจจุบันเป็น ค.ศ. (yyyy-MM-dd)
            if (string.IsNullOrEmpty(start))
            {
                start = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            if (string.IsNullOrEmpty(end))
            {
                end = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            ViewBag.Start = start;
            ViewBag.End = end;



            // 3. เริ่มสร้าง URL พื้นฐาน
            var apiUrl = $"https://api.zycoda.com/apimpros/get_job_order?plant=FARMHOUSE&jobtype={jobtype}";

            // ✅ เพิ่ม Parameter วันที่ เฉพาะเมื่อมีการเลือกมาเท่านั้น (ถ้ามาหน้าแรก สองบรรทัดนี้จะถูกข้ามไป)
            if (!string.IsNullOrEmpty(start)) apiUrl += $"&start={start}";
            if (!string.IsNullOrEmpty(end)) apiUrl += $"&end={end}";

            // ✅ เพิ่ม Filter อื่นๆ ถ้ามีการเลือก
            if (!string.IsNullOrEmpty(Sectioncreate)) apiUrl += $"&sectioncreate={Sectioncreate}";
            if (!string.IsNullOrEmpty(Status)) apiUrl += $"&status={Status}";

            // อ่านไฟล์ JSON สำหรับ Dropdown
            var jsonsectioncreate = System.IO.File.ReadAllText("wwwroot/data/section.json");
            ViewBag.Section = JsonSerializer.Deserialize<List<Models.SectionApiModels>>(jsonsectioncreate);

            var jsonstatus = System.IO.File.ReadAllText("wwwroot/data/status.json");
            ViewBag.Status = JsonSerializer.Deserialize<List<Models.StatusApiModels>>(jsonstatus);

            List<MaintenanceApiModels> data = new List<MaintenanceApiModels>();

            using (HttpClient client = new HttpClient())
            {
                // 🚩 3. ตรวจสอบว่าพาสเวิร์ดใน Session ยังอยู่ไหม (ถ้าหายอาจต้องเก็บใน Claims ตอน Login ด้วยจะดีกว่า)
                if (string.IsNullOrEmpty(sessionPass))
                {
                    // ถ้า Session Pass หาย ให้ลองดึงจากตั๋ว หรือถ้าไม่มีจริงๆ ค่อยดีดกลับ
                    return RedirectToAction("Login", "Home");
                }

                client.DefaultRequestHeaders.Add("username", sessionUser);
                client.DefaultRequestHeaders.Add("password", sessionPass);


                // ดึงข้อมูลจาก API
                var response = await client.GetAsync(apiUrl);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();

                    // ใช้ Newtonsoft.Json เพราะรองรับ JSON ก้อนใหญ่และยืดหยุ่นกว่า
                    var allData = Newtonsoft.Json.JsonConvert.DeserializeObject<List<MaintenanceApiModels>>(json) ?? new List<MaintenanceApiModels>();

                    // 🚩 กรองข้อมูลในเครื่อง (In-memory Filter) 
                    // เผื่อกรณี API คืนมาทั้งหมด แต่เราอยากกรองเพิ่มตาม Dropdown ที่เลือก
                    var filteredData = allData.AsEnumerable();

                    if (!string.IsNullOrEmpty(Status))
                    {
                        filteredData = filteredData.Where(x => string.Equals(x.status, Status, StringComparison.OrdinalIgnoreCase));
                    }

                    if (!string.IsNullOrEmpty(Sectioncreate))
                    {
                        filteredData = filteredData.Where(x => string.Equals(x.sectioncreate, Sectioncreate, StringComparison.OrdinalIgnoreCase));
                    }

                    data = filteredData.ToList();
                }
            }

            return View(data);
        }

        [Authorize] // 🚩 1. เพิ่มเพื่อให้ระบบเช็คตั๋ว (Cookie) ก่อนเข้า
        public async Task<IActionResult> ReportReceiver(string jobtype, string start, string end, string Sectioncreate, string Status)
        {
            // 🚩 2. เปลี่ยนมาใช้ User.Identity.Name แทนการดึง Session โดยตรง (ดึงจากตั๋วที่ Login มาแล้ว)
            // 🚩 ดึงข้อมูลจาก Identity/Claims (ไม่หายแม้ Session จะ Timeout)
            var sessionUser = User.Identity.Name;
            var sessionPass = User.Claims.FirstOrDefault(c => c.Type == "ApiPassword")?.Value;

            // เช็คว่า Login หรือยัง (ใช้ Identity เช็คจะชัวร์กว่า Session)
            if (!User.Identity.IsAuthenticated) return RedirectToAction("Login", "Home");

            // --- ส่วน Logic เดิมของคุณ ---
            if (string.IsNullOrEmpty(jobtype)) jobtype = "EM,CM";

            // 1. ตั้งค่าวันที่ปัจจุบันเป็น ค.ศ. (yyyy-MM-dd)
            if (string.IsNullOrEmpty(start))
            {
                start = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            if (string.IsNullOrEmpty(end))
            {
                end = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            ViewBag.Start = start;
            ViewBag.End = end;



            // 3. เริ่มสร้าง URL พื้นฐาน
            var apiUrl = $"https://api.zycoda.com/apimpros/get_job_order?plant=FARMHOUSE&jobtype={jobtype}";

            // ✅ เพิ่ม Parameter วันที่ เฉพาะเมื่อมีการเลือกมาเท่านั้น (ถ้ามาหน้าแรก สองบรรทัดนี้จะถูกข้ามไป)
            if (!string.IsNullOrEmpty(start)) apiUrl += $"&start={start}";
            if (!string.IsNullOrEmpty(end)) apiUrl += $"&end={end}";

            // ✅ เพิ่ม Filter อื่นๆ ถ้ามีการเลือก
            if (!string.IsNullOrEmpty(Sectioncreate)) apiUrl += $"&sectioncreate={Sectioncreate}";
            if (!string.IsNullOrEmpty(Status)) apiUrl += $"&status={Status}";

            // อ่านไฟล์ JSON สำหรับ Dropdown
            var jsonsectioncreate = System.IO.File.ReadAllText("wwwroot/data/section.json");
            ViewBag.Section = JsonSerializer.Deserialize<List<Models.SectionApiModels>>(jsonsectioncreate);

            var jsonstatus = System.IO.File.ReadAllText("wwwroot/data/status.json");
            ViewBag.Status = JsonSerializer.Deserialize<List<Models.StatusApiModels>>(jsonstatus);

            List<MaintenanceApiModels> data = new List<MaintenanceApiModels>();

            using (HttpClient client = new HttpClient())
            {
                // 🚩 3. ตรวจสอบว่าพาสเวิร์ดใน Session ยังอยู่ไหม (ถ้าหายอาจต้องเก็บใน Claims ตอน Login ด้วยจะดีกว่า)
                if (string.IsNullOrEmpty(sessionPass))
                {
                    // ถ้า Session Pass หาย ให้ลองดึงจากตั๋ว หรือถ้าไม่มีจริงๆ ค่อยดีดกลับ
                    return RedirectToAction("Login", "Home");
                }

                client.DefaultRequestHeaders.Add("username", sessionUser);
                client.DefaultRequestHeaders.Add("password", sessionPass);


                // ดึงข้อมูลจาก API
                var response = await client.GetAsync(apiUrl);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();

                    // ใช้ Newtonsoft.Json เพราะรองรับ JSON ก้อนใหญ่และยืดหยุ่นกว่า
                    var allData = Newtonsoft.Json.JsonConvert.DeserializeObject<List<MaintenanceApiModels>>(json) ?? new List<MaintenanceApiModels>();

                    // 🚩 กรองข้อมูลในเครื่อง (In-memory Filter) 
                    // เผื่อกรณี API คืนมาทั้งหมด แต่เราอยากกรองเพิ่มตาม Dropdown ที่เลือก
                    var filteredData = allData.AsEnumerable();

                    if (!string.IsNullOrEmpty(Status))
                    {
                        filteredData = filteredData.Where(x => string.Equals(x.status, Status, StringComparison.OrdinalIgnoreCase));
                    }

                    if (!string.IsNullOrEmpty(Sectioncreate))
                    {
                        filteredData = filteredData.Where(x => string.Equals(x.sectioncreate, Sectioncreate, StringComparison.OrdinalIgnoreCase));
                    }

                    data = filteredData.ToList();
                }
            }

            return View(data);
        }


        public async Task<IActionResult> ReportSender(string start, string end, string Status)
        {

            // 🚩 ดึงข้อมูลจาก Identity/Claims (ไม่หายแม้ Session จะ Timeout)
            var sessionUser = User.Identity.Name;
            var sessionPass = User.Claims.FirstOrDefault(c => c.Type == "ApiPassword")?.Value;

            // 1. ตั้งค่าวันที่ปัจจุบันเป็น ค.ศ. (yyyy-MM-dd)
            if (string.IsNullOrEmpty(start))
            {
                start = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            if (string.IsNullOrEmpty(end))
            {
                end = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            ViewBag.Start = start;
            ViewBag.End = end;



            // 3. เริ่มสร้าง URL พื้นฐาน
            var apiUrl = $"https://api.zycoda.com/apimpros/get_job_order?plant=FARMHOUSE&";

            var section = HttpContext.Session.GetString("Section");

            if (!string.IsNullOrEmpty(start)) apiUrl += $"&start={start}";
            if (!string.IsNullOrEmpty(end)) apiUrl += $"&end={end}";
            if (!string.IsNullOrEmpty(section)) apiUrl += $"&section={section}";
            if (!string.IsNullOrEmpty(Status)) apiUrl += $"&status={Status}";

            // อ่านไฟล์ JSON สำหรับ Dropdown
            var jsonsection = System.IO.File.ReadAllText("wwwroot/data/section.json");
            ViewBag.Section = JsonSerializer.Deserialize<List<Models.SectionApiModels>>(jsonsection);

            var jsonstatus = System.IO.File.ReadAllText("wwwroot/data/status.json");
            ViewBag.Status = JsonSerializer.Deserialize<List<Models.StatusApiModels>>(jsonstatus);

            List<MaintenanceApiModels> data = new List<MaintenanceApiModels>();

            using (HttpClient client = new HttpClient())
            {
                // 🚩 3. ตรวจสอบว่าพาสเวิร์ดใน Session ยังอยู่ไหม (ถ้าหายอาจต้องเก็บใน Claims ตอน Login ด้วยจะดีกว่า)
                if (string.IsNullOrEmpty(sessionPass))
                {
                    // ถ้า Session Pass หาย ให้ลองดึงจากตั๋ว หรือถ้าไม่มีจริงๆ ค่อยดีดกลับ
                    return RedirectToAction("Login", "Home");
                }

                client.DefaultRequestHeaders.Add("username", sessionUser);
                client.DefaultRequestHeaders.Add("password", sessionPass);


                // ดึงข้อมูลจาก API
                var response = await client.GetAsync(apiUrl);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();

                    // ใช้ Newtonsoft.Json เพราะรองรับ JSON ก้อนใหญ่และยืดหยุ่นกว่า
                    var allData = Newtonsoft.Json.JsonConvert.DeserializeObject<List<MaintenanceApiModels>>(json) ?? new List<MaintenanceApiModels>();

                    // 🚩 กรองข้อมูลในเครื่อง (In-memory Filter) 
                    // เผื่อกรณี API คืนมาทั้งหมด แต่เราอยากกรองเพิ่มตาม Dropdown ที่เลือก
                    var filteredData = allData.AsEnumerable();

                    if (!string.IsNullOrEmpty(Status))
                    {
                        filteredData = filteredData.Where(x => string.Equals(x.status, Status, StringComparison.OrdinalIgnoreCase));
                    }
                    data = filteredData.ToList();
                }
            }

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