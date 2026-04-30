using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using report_zycoda.Models;
using System.IO;

namespace report_zycoda.Controllers
{
    public class MasterController : Controller
    {
        // ใช้ Path.Combine เพื่อให้ระบบหาไฟล์เจอแน่นอน ไม่ว่าจะรันบนเครื่องไหน
        private readonly string _sectionPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "data", "section.json");
        private readonly string _userPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "data", "user.json");

        public IActionResult Index()
        {
            // เช็คสิทธิ์ Class 9 ก่อนเข้าหน้า Master (ถ้ามีระบบ Claim)
            var userClass = User.Claims.FirstOrDefault(c => c.Type == "Class")?.Value;
            if (userClass != "9" && !User.IsInRole("Administrator"))
            {
                return Forbid(); // ไม่มีสิทธิ์เข้าถึง
            }

            ViewBag.Sections = GetList<SectionApiModels>(_sectionPath);
            ViewBag.Users = GetList<UserApiModels>(_userPath);

            return View();
        }

        // --- 🏢 จัดการ Section ---
        [HttpPost]
        public IActionResult SaveSection(SectionApiModels model)
        {
            var list = GetList<SectionApiModels>(_sectionPath);
            var existing = list.FirstOrDefault(x => x.Sections == model.Sections);

            if (existing != null)
            {
                existing.Name = model.Name; // อัพเดทชื่อ
            }
            else
            {
                list.Add(model); // เพิ่มใหม่
            }

            SaveToFile(_sectionPath, list);
            return RedirectToAction("Index");
        }

        [HttpPost]
        public IActionResult DeleteSection(string id)
        {
            var list = GetList<SectionApiModels>(_sectionPath);
            var item = list.FirstOrDefault(x => x.Sections == id);
            if (item != null)
            {
                list.Remove(item);
                SaveToFile(_sectionPath, list);
            }
            return RedirectToAction("Index");
        }

        // --- 👥 จัดการ User ---
        [HttpPost]
        public IActionResult SaveUser(UserApiModels model)
        {
            var list = GetList<UserApiModels>(_userPath);
            var existing = list.FirstOrDefault(x => x.Username == model.Username);

            if (existing != null)
            {
                // อัพเดทข้อมูลอื่นๆ (username เป็น Key ห้ามเปลี่ยน)
                existing.LastName = model.LastName;
                existing.Rule = model.Rule;
                // existing.section = model.section; // ถ้าจะอัพเดทแผนกด้วย
            }
            else
            {
                list.Add(model);
            }

            SaveToFile(_userPath, list);
            return RedirectToAction("Index");
        }

        [HttpPost]
        public IActionResult DeleteUser(string id)
        {
            var list = GetList<UserApiModels>(_userPath);
            var item = list.FirstOrDefault(x => x.Username == id);
            if (item != null)
            {
                list.Remove(item);
                SaveToFile(_userPath, list);
            }
            return RedirectToAction("Index");
        }

        // --- ⚙️ Helper Methods (ตัวช่วยจัดการไฟล์) ---
        private List<T> GetList<T>(string path)
        {
            try
            {
                if (!System.IO.File.Exists(path)) return new List<T>();
                var json = System.IO.File.ReadAllText(path);
                return JsonConvert.DeserializeObject<List<T>>(json) ?? new List<T>();
            }
            catch
            {
                return new List<T>();
            }
        }

        private void SaveToFile<T>(string path, List<T> data)
        {
            var json = JsonConvert.SerializeObject(data, Formatting.Indented);
            System.IO.File.WriteAllText(path, json);
        }
    }
}