using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using report_zycoda.Data;
using report_zycoda.Models;
using System.Linq;
using System.Threading.Tasks;

namespace report_zycoda.Controllers
{
    public class AdminController : Controller
    {
        private readonly myDbContext _context;

        public AdminController(myDbContext context)
        {
            _context = context;
        }

        // 🟢 เปลี่ยนจาก Index เป็น Machine เพื่อให้ตรงกับชื่อไฟล์ Machine.cshtml ในโฟลเดอร์ Views
        //public async Task<IActionResult> Machine()
        //{
        //    //var viewModel = new AdminDashboardViewModel
        //    //{
        //    //    // 🟢 1. เปลี่ยนมาดึงข้อมูลจากตาราง .JobOrders (ตามที่ประกาศไว้ใน myDbContext)
        //    //    MaintenanceApis = await _context.JobOrders.ToListAsync(),

        //    //    // 🟢 2. เปลี่ยนตัวระบุประเภทข้อมูลภายในวงเล็บแหลมเป็น <UserApiModels> เพื่อให้ตรงรุ่นกับ DbSet ใน DB ครับ
        //    //    Users = await _context.Users.OrderByDescending(u => u.Id).ToListAsync<UserApiModels>()
        //    //};

        //    return View(viewModel);
        //}
    }
}