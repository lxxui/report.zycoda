using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ExcelDataReader;
using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;

namespace report_zycoda.Controllers
{
    [Authorize]
    public class SectionController : Controller
    {
        private readonly ILogger<SectionController> _logger;
        private readonly string _connectionString;

        public SectionController(ILogger<SectionController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
        }

        /// <summary>
        /// 🟢 อัปเดตแอกชัน Index: ดึงข้อมูลสรุปส่งไปให้กล่องสถิติบนหน้า View แสดงผลได้ทันที
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    // 🏢 1. ดึงข้อมูล Section ทั้งหมดไปใส่นับจำนวนในระบบ
                    string sectionQuery = @"SELECT [Id], [Sections], [Plant], [Name], [WorkCenter] 
                                           FROM [ZycodaApiByPB].[dbo].[Section]";
                    DataTable dtSection = new DataTable();
                    using (SqlCommand cmd = new SqlCommand(sectionQuery, conn))
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        dtSection.Load(reader);
                    }
                    ViewBag.SectionTable = dtSection; // 🎯 แปะไปให้แท็บแผนกนับยอดรวม

                    // 👥 2. ดึงข้อมูล User ทั้งหมดมาใช้นับยอด Active / Inactive
                    string userQuery = @"SELECT [Username], [IsActive] 
                                         FROM [ZycodaApiByPB].[dbo].[User]";
                    DataTable dtUser = new DataTable();
                    using (SqlCommand cmd = new SqlCommand(userQuery, conn))
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        dtUser.Load(reader);
                    }
                    ViewBag.UserTable = dtUser; // 🎯 แปะไปให้แท็บพนักงานนับยอดสรุป
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error rendering Section Index Dashboard: {ex.Message}");
                // ป้องกันหน้าเว็บบึ้มเวลา SQL หลุด ให้ส่งตารางเปล่าไปเซฟหน้างานค่ะฝ้าย
                ViewBag.SectionTable = new DataTable();
                ViewBag.UserTable = new DataTable();
            }

            // ⚠️ ฝ้ายตรวจสอบให้แน่ใจว่าในโปรเจกต์มีโฟลเดอร์และไฟล์ที่ตำแหน่งนี้: /Views/Section/Index.cshtml นะคะ
            return View();
        }

        /// <summary>
        /// 🟢 1. ดึงข้อมูลรายการแผนก (Section) ไปแสดงผลบนตาราง #sectionTable ผ่าน AJAX
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetSectionData()
        {
            var dataList = new List<Dictionary<string, object>>();

            try
            {
                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    string query = @"SELECT [Id], [Id_Zy], [Sections], [Plant], [Name], 
                                            [PermitRequest], [WorkCenter], [LastUpdateDate], [LastUpdateBy]
                                     FROM [ZycodaApiByPB].[dbo].[Section]";

                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var row = new Dictionary<string, object>();
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                row[reader.GetName(i)] = reader.GetValue(i) == DBNull.Value ? "" : reader.GetValue(i);
                            }
                            dataList.Add(row);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error fetching section JSON: {ex.Message}");
            }

            return Json(new { data = dataList });
        }

        /// <summary>
        /// 🟢 2. อัปโหลดและนำเข้าไฟล์ Excel สังกัดแผนก (Upsert Logic: มีอยู่แล้วอัปเดต / ไม่มีเพิ่มใหม่ พร้อมนับแยกสถิติ)
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadExcel(IFormFile excelFile)
        {
            if (excelFile == null || excelFile.Length == 0)
            {
                TempData["Error"] = "กรุณาเลือกไฟล์ Excel (.xlsx หรือ .xls) ก่อนค่ะฝ้าย";
                return RedirectToAction("Index");
            }

            var fileExtension = Path.GetExtension(excelFile.FileName).ToLower();
            if (fileExtension != ".xlsx" && fileExtension != ".xls")
            {
                TempData["Error"] = "ระบบรองรับเฉพาะไฟล์ Excel (.xlsx หรือ .xls) เท่านั้นค่ะ";
                return RedirectToAction("Index");
            }

            int insertCount = 0; // ➕ ตัวนับรายการนำเข้าใหม่
            int updateCount = 0; // 🔄 ตัวนับรายการอัปเดตทับข้อมูลเดิม
            int totalRows = 0;

            try
            {
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

                using (var stream = excelFile.OpenReadStream())
                using (var reader = ExcelReaderFactory.CreateReader(stream))
                {
                    var result = reader.AsDataSet(new ExcelDataSetConfiguration()
                    {
                        ConfigureDataTable = (_) => new ExcelDataTableConfiguration()
                        {
                            UseHeaderRow = true
                        }
                    });

                    DataTable dt = result.Tables[0];

                    if (dt == null || dt.Rows.Count == 0)
                    {
                        TempData["Error"] = "ไม่พบข้อมูลเรคคอร์ดในไฟล์ Excel เลยค่ะฝ้าย";
                        return RedirectToAction("Index");
                    }

                    totalRows = dt.Rows.Count;

                    // 🎯 ปรับ SQL Query ให้คืนสถานะกลับมาเป็นผลลัพธ์ข้อความ เพื่อให้นำมาแยกเงื่อนไขนับสถิติในโค้ด C#
                    string queryUpsert = @"
                        IF EXISTS (SELECT 1 FROM [dbo].[Section] WHERE [Sections] = @Sections)
                        BEGIN
                            UPDATE [dbo].[Section]
                            SET [Plant] = @Plant,
                                [Name] = @Name,
                                [WorkCenter] = @WorkCenter,
                                [LastUpdateDate] = GETDATE(),
                                [LastUpdateBy] = @LastUpdateBy
                            WHERE [Sections] = @Sections;
                            
                            SELECT 'UPDATED';
                        END
                        ELSE
                        BEGIN
                            INSERT INTO [dbo].[Section] (
                                [Id_Zy], [Sections], [Plant], [Name], 
                                [PermitRequest], [WorkCenter], [LastUpdateDate], [LastUpdateBy]
                            ) VALUES (
                                @Id_Zy, @Sections, @Plant, @Name, 
                                0, @WorkCenter, GETDATE(), @LastUpdateBy
                            );
                            
                            SELECT 'INSERTED';
                        END";

                    string username = User.Identity?.Name ?? "System_Excel";

                    using (SqlConnection conn = new SqlConnection(_connectionString))
                    {
                        await conn.OpenAsync();

                        foreach (DataRow row in dt.Rows)
                        {
                            // ฟังก์ชันดึงค่าจากคอลัมน์ของตาราง Excel แบบ Case-Insensitive
                            Func<string, string> getValue = (colName) => {
                                var col = dt.Columns.Cast<DataColumn>()
                                    .FirstOrDefault(c => c.ColumnName.Trim().Equals(colName.Trim(), StringComparison.OrdinalIgnoreCase));
                                return col != null ? row[col]?.ToString()?.Trim() ?? "" : "";
                            };

                            string sectionId = getValue("section");
                            if (string.IsNullOrEmpty(sectionId)) continue; // ข้ามแถวกรณีรหัส Section หลักในแถวนั้นว่างเปล่า

                            string plant = getValue("plant");
                            string name = getValue("name");
                            string workCenter = getValue("workcenter");

                            // ควบคุมการประมวลผลพารามิเตอร์รายแถวด้วย using block เพื่อรีเซ็ตข้อมูล ไม่ให้พารามิเตอร์ SQL ตีกันเอง
                            using (SqlCommand cmd = new SqlCommand(queryUpsert, conn))
                            {
                                cmd.Parameters.AddWithValue("@Id_Zy", Guid.NewGuid().ToString().Substring(0, 8));
                                cmd.Parameters.AddWithValue("@Sections", sectionId);
                                cmd.Parameters.AddWithValue("@Plant", string.IsNullOrEmpty(plant) ? DBNull.Value : plant);
                                cmd.Parameters.AddWithValue("@Name", string.IsNullOrEmpty(name) ? DBNull.Value : name);
                                cmd.Parameters.AddWithValue("@WorkCenter", string.IsNullOrEmpty(workCenter) ? DBNull.Value : workCenter);
                                cmd.Parameters.AddWithValue("@LastUpdateBy", username);

                                // ใช้ ExecuteScalarAsync เพื่อดึงค่า 'UPDATED' หรือ 'INSERTED' ที่ SQL เลือกทำงานออกมาสะสมยอด
                                var executionResult = await cmd.ExecuteScalarAsync() as string;

                                if (executionResult == "UPDATED")
                                {
                                    updateCount++;
                                }
                                else if (executionResult == "INSERTED")
                                {
                                    insertCount++;
                                }
                            }
                        }
                    }
                }

                // 🌟 ผลลัพธ์แสดงรายงานยอดการทำรายการอย่างชัดเจนบนแจ้งเตือนหน้าบ้าน
                TempData["Success"] = $"ซิงค์ข้อมูลสังกัดแผนกสำเร็จเรียบร้อยค่ะฝ้าย! (เพิ่มข้อมูลใหม่ {insertCount} รายการ | อัปเดตข้อมูลเดิม {updateCount} รายการ จากทั้งหมด {totalRows} แถว)";
            }
            catch (Exception ex)
            {
                _logger.LogError($"Section Excel Import Error: {ex.Message}");
                TempData["Error"] = $"เกิดปัญหาฟิลด์ขัดข้องระหว่างบันทึกแผนก: {ex.Message}";
            }

            return RedirectToAction("Index", "Maintenance");
        }
    }
}