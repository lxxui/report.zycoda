using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using report_zycoda.Models;
using static System.Collections.Specialized.BitVector32;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace report_zycoda.Controllers
{
    public class MaintenanceController : Controller
    {

        public async Task<IActionResult> Index(string jobtype, string start, string end, string Sectioncreate, string Status)
        {
            // 1. ตั้งค่าพื้นฐาน (ถ้ามาหน้าแรก jobtype จะเป็น EM,CM)
            if (string.IsNullOrEmpty(jobtype)) jobtype = "EM,CM";

            // 2. ส่งค่ากลับไปที่ View (เพื่อให้หน้าเว็บจำค่าที่ User เลือก แต่ถ้าไม่ได้เลือกจะเป็นช่องว่าง)
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
                var sessionUser = HttpContext.Session.GetString("ApiUser");
                var sessionPass = HttpContext.Session.GetString("ApiPass");

                // เช็ค Session กันเหนียว
                if (string.IsNullOrEmpty(sessionUser)) return RedirectToAction("Login", "Home");

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

        // เพิ่มตัวนี้ต่อจาก Index เดิมได้เลยครับ

        public async Task<IActionResult> ReportReceiver(string jobtype, string start, string end, string Sectioncreate, string Status)
        {
            // 1. ตั้งค่าพื้นฐาน (ถ้ามาหน้าแรก jobtype จะเป็น EM,CM)
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
                var sessionUser = HttpContext.Session.GetString("ApiUser");
                var sessionPass = HttpContext.Session.GetString("ApiPass");

                // เช็ค Session กันเหนียว
                if (string.IsNullOrEmpty(sessionUser)) return RedirectToAction("Login", "Home");

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



        //public async Task<IActionResult> ReportReceiver(string sectionFrom, string sectionTo, string start, string end, string status)
        //{
        //    // 1. ดึง Session
        //    var sessionUser = HttpContext.Session.GetString("ApiUser");
        //    var sessionPass = HttpContext.Session.GetString("ApiPass");

        //    if (string.IsNullOrEmpty(sessionUser)) return RedirectToAction("Login", "Home");
        //    if (string.IsNullOrEmpty(start))
        //    {
        //        start = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        //    }
        //    if (string.IsNullOrEmpty(end))
        //    {
        //        end = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        //    }
        //    // 2. อ่านไฟล์ JSON สำหรับ Dropdown (โหลดครั้งเดียวพอครับ)
        //    try
        //    {
        //        var jsonSection = System.IO.File.ReadAllText("wwwroot/data/section.json");
        //        ViewBag.Section = JsonSerializer.Deserialize<List<Models.SectionApiModels>>(jsonSection);

        //        var jsonStatus = System.IO.File.ReadAllText("wwwroot/data/status.json");
        //        ViewBag.Status = JsonSerializer.Deserialize<List<Models.StatusApiModels>>(jsonStatus);
        //    }
        //    catch { /* กันไฟล์หาย */ }

        //    // 3. เก็บค่า Filter ไว้ส่งกลับหน้า View เพื่อให้ Dropdown/Input ค้างค่าเดิมไว้
        //    ViewBag.SectionFrom = sectionFrom;
        //    ViewBag.SectionTo = sectionTo;
        //    ViewBag.Start = start;
        //    ViewBag.End = end;
        //    ViewBag.CurrentStatus = status;

        //    // 4. สร้าง URL (ใช้ EM,CM เหมือน Dashboard)
        //    var apiUrl = $"https://api.zycoda.com/apimpros/get_job_order?plant=FARMHOUSE&jobtype=EM,CM";

        //    if (!string.IsNullOrEmpty(start)) apiUrl += $"&start={start}";
        //    if (!string.IsNullOrEmpty(end)) apiUrl += $"&end={end}";
        //    // หมายเหตุ: section จาก API ปกติจะใช้ sectioncreate แต่ถ้าเราจะกรองเป็นช่วง (From-To) 
        //    // เราจะดึงข้อมูลมาก่อนแล้วค่อยกรองในเครื่อง (In-memory) ตาม logic ด้านล่างครับ

        //    List<MaintenanceApiModels> data = new List<MaintenanceApiModels>();

        //    using (HttpClient client = new HttpClient())
        //    {
        //        client.DefaultRequestHeaders.Add("username", sessionUser);
        //        client.DefaultRequestHeaders.Add("password", sessionPass);

        //        try
        //        {
        //            var response = await client.GetAsync(apiUrl);
        //            if (response.IsSuccessStatusCode)
        //            {
        //                var json = await response.Content.ReadAsStringAsync();

        //                // ✅ เช็คว่าเป็น JSON List จริงไหม (กัน Error "invalid password")
        //                if (json.Trim().StartsWith("["))
        //                {
        //                    var allData = Newtonsoft.Json.JsonConvert.DeserializeObject<List<MaintenanceApiModels>>(json) ?? new List<MaintenanceApiModels>();

        //                    // 🚩 กรองข้อมูลในเครื่อง (In-memory Filter)
        //                    var filteredData = allData.AsEnumerable();

        //                    // กรองตาม Status (ถ้าเลือก)
        //                    if (!string.IsNullOrEmpty(status))
        //                    {
        //                        filteredData = filteredData.Where(x => string.Equals(x.status, status, StringComparison.OrdinalIgnoreCase));
        //                    }

        //                    // 🚩 กรองช่วงแผนก (Section From - To)
        //                    if (!string.IsNullOrEmpty(sectionFrom) && !string.IsNullOrEmpty(sectionTo))
        //                    {
        //                        filteredData = filteredData.Where(x =>
        //                            string.Compare(x.sectioncreate, sectionFrom) >= 0 &&
        //                            string.Compare(x.sectioncreate, sectionTo) <= 0);
        //                    }
        //                    // ถ้าเลือกแค่ From อย่างเดียว
        //                    else if (!string.IsNullOrEmpty(sectionFrom))
        //                    {
        //                        filteredData = filteredData.Where(x => x.sectioncreate == sectionFrom);
        //                    }

        //                    data = filteredData.ToList();
        //                }
        //            }
        //        }
        //        catch (Exception ex)
        //        {
        //            // พ่น Error ลง Console ไว้ดูเวลาติดปัญหา
        //            System.Diagnostics.Debug.WriteLine("Report Error: " + ex.Message);
        //        }
        //    }

        //    return View(data);
        //}

        public async Task<IActionResult> ReportSender(string start, string end, string Status)
        {

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
                var sessionUser = HttpContext.Session.GetString("ApiUser");
                var sessionPass = HttpContext.Session.GetString("ApiPass");

                // เช็ค Session กันเหนียว
                if (string.IsNullOrEmpty(sessionUser)) return RedirectToAction("Login", "Home");

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