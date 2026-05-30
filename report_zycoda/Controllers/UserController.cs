using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ExcelDataReader;
using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;

namespace report_zycoda.Controllers
{
    [Authorize]
    public class UserController : Controller
    {
        private readonly ILogger<UserController> _logger;
        private readonly string _connectionString;

        public UserController(ILogger<UserController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
        }

        [HttpGet]
        public IActionResult Index()
        {
            // วิ่งกลับไปหน้าคอนโทรลเลอร์หลักจัดการข้อมูลระบบ Zycoda
            return RedirectToAction("Index", "Maintenance");
        }

        /// <summary>
        /// 🟢 1. ดึงข้อมูลรายการผู้ใช้งาน (User) ทั้งหมดแสดงผลบนตาราง #userTable ผ่าน AJAX
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetUserData()
        {
            var dataList = new List<Dictionary<string, object>>();

            try
            {
                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    string query = @"SELECT [Id]
                                          ,[Id_Zy]
                                          ,[Plant]
                                          ,[FirstName]
                                          ,[LastName]
                                          ,[Email]
                                          ,[Username]
                                          ,[Password]
                                          ,[Position]
                                          ,[Section]
                                          ,[PlannerGroup]
                                          ,[SectionOption]
                                          ,[Class]
                                          ,[Rule]
                                          ,[Active]
                                          ,[Smallgroup]
                                          ,[Tel]
                                          ,[Work_center]
                                          ,[UserAD]
                                          ,[SubUsers]
                                          ,[Expire_active]
                                          ,[LaborRateId]
                                          ,[Last_modify]
                                          ,[Workpremit_class]
                                          ,[Plantoption]
                                      FROM [ZycodaApiByPB_Lenovo].[dbo].[User]";

                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var row = new Dictionary<string, object>();
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                var colName = reader.GetName(i);
                                var value = reader.GetValue(i);

                                // จัดการฟอร์แมตวันที่ให้อยู่ในสไตล์ ISO ป้องกันหน้าบ้านเออร์เรอร์
                                if (value is DateTime dateValue)
                                {
                                    row[colName] = dateValue.ToString("yyyy-MM-ddTHH:mm:ss");
                                }
                                else
                                {
                                    row[colName] = value == DBNull.Value ? "" : value;
                                }
                            }
                            dataList.Add(row);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error fetching user JSON: {ex.Message}");
            }

            return Json(new { data = dataList });
        }

        /// <summary>
        /// 🟢 2. อัปโหลดและนำเข้าไฟล์ Excel ข้อมูลผู้ใช้งาน (Upsert Logic: มีอยู่แล้วอัปเดต / ไม่มีเพิ่มใหม่ พร้อมนับแยกสถิติ)
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadExcel(IFormFile excelFile)
        {
            if (excelFile == null || excelFile.Length == 0)
            {
                TempData["Error"] = "กรุณาเลือกไฟล์ Excel (.xlsx หรือ .xls) ก่อนค่ะฝ้าย";
                return RedirectToAction("Index", "Maintenance");
            }

            var fileExtension = Path.GetExtension(excelFile.FileName).ToLower();
            if (fileExtension != ".xlsx" && fileExtension != ".xls")
            {
                TempData["Error"] = "ระบบรองรับเฉพาะไฟล์ Excel (.xlsx หรือ .xls) เท่านั้นค่ะ";
                return RedirectToAction("Index", "Maintenance");
            }

            int insertCount = 0; // ➕ ตัวนับรายการผู้ใช้ใหม่
            int updateCount = 0; // 🔄 ตัวนับรายการผู้ใช้ที่อัปเดตข้อมูลเดิม
            int totalRows = 0;

            try
            {
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

                using (var stream = excelFile.OpenReadStream())
                {
                    using (var reader = ExcelReaderFactory.CreateReader(stream))
                    {
                        var result = reader.AsDataSet(new ExcelDataSetConfiguration()
                        {
                            ConfigureDataTable = (_) => new ExcelDataTableConfiguration()
                            {
                                UseHeaderRow = true // ใช้แถวแรกของ Excel เป็นหัวคอลัมน์
                            }
                        });

                        DataTable dt = result.Tables[0];

                        if (dt == null || dt.Rows.Count == 0)
                        {
                            TempData["Error"] = "ไม่พบข้อมูลเรคคอร์ดในไฟล์ Excel เลยค่ะฝ้าย";
                            return RedirectToAction("Index", "Maintenance");
                        }

                        totalRows = dt.Rows.Count;

                        using (SqlConnection conn = new SqlConnection(_connectionString))
                        {
                            await conn.OpenAsync();

                            // 🎯 SQL Upsert Logic: เพิ่ม SELECT 'UPDATED' และ SELECT 'INSERTED' กลับมาเช็คในโค้ด C#
                            string queryUpsert = @"
                                IF EXISTS (SELECT 1 FROM [ZycodaApiByPB_Lenovo].[dbo].[User] WHERE [Username] = @Username)
                                BEGIN
                                    UPDATE [ZycodaApiByPB_Lenovo].[dbo].[User]
                                    SET [Plant] = @Plant,
                                        [FirstName] = @FirstName,
                                        [LastName] = @LastName,
                                        [Email] = @Email,
                                        [Password] = @Password,
                                        [Position] = @Position,
                                        [Section] = @Section,
                                        [PlannerGroup] = @PlannerGroup,
                                        [SectionOption] = @SectionOption,
                                        [Class] = @Class,
                                        [Rule] = @Rule,
                                        [Active] = @Active,
                                        [Smallgroup] = @Smallgroup,
                                        [Tel] = @Tel,
                                        [Work_center] = @Work_center,
                                        [UserAD] = @UserAD,
                                        [SubUsers] = @SubUsers,
                                        [Expire_active] = @Expire_active,
                                        [LaborRateId] = @LaborRateId,
                                        [Last_modify] = GETDATE(),
                                        [Workpremit_class] = @Workpremit_class,
                                        [Plantoption] = @Plantoption
                                    WHERE [Username] = @Username;

                                    SELECT 'UPDATED';
                                END
                                ELSE
                                BEGIN
                                    INSERT INTO [ZycodaApiByPB_Lenovo].[dbo].[User] (
                                        [Id_Zy], [Plant], [FirstName], [LastName], [Email], [Username], [Password],
                                        [Position], [Section], [PlannerGroup], [SectionOption], [Class], [Rule], [Active],
                                        [Smallgroup], [Tel], [Work_center], [UserAD], [SubUsers], [Expire_active],
                                        [LaborRateId], [Last_modify], [Workpremit_class], [Plantoption]
                                    ) VALUES (
                                        @Id_Zy, @Plant, @FirstName, @LastName, @Email, @Username, @Password,
                                        @Position, @Section, @PlannerGroup, @SectionOption, @Class, @Rule, @Active,
                                        @Smallgroup, @Tel, @Work_center, @UserAD, @SubUsers, @Expire_active,
                                        @LaborRateId, GETDATE(), @Workpremit_class, @Plantoption
                                    );

                                    SELECT 'INSERTED';
                                END";

                            foreach (DataRow row in dt.Rows)
                            {
                                // ฟังก์ชันช่วยดึงค่าจาก Excel แบบ Case-Insensitive
                                Func<string, string> getValue = (colName) => {
                                    var col = dt.Columns.Cast<DataColumn>()
                                        .FirstOrDefault(c => c.ColumnName.Trim().Equals(colName.Trim(), StringComparison.OrdinalIgnoreCase));
                                    return col != null ? row[col]?.ToString()?.Trim() ?? "" : "";
                                };

                                string username = getValue("username");
                                if (string.IsNullOrEmpty(username)) continue; // ถ้าไม่มี Username ให้ข้ามแถวนี้ไปเลย

                                using (SqlCommand cmd = new SqlCommand(queryUpsert, conn))
                                {
                                    // ผูกพารามิเตอร์ระบบข้อมูลผู้ใช้
                                    cmd.Parameters.AddWithValue("@Id_Zy", getValue("id_zy") != "" ? getValue("id_zy") : Guid.NewGuid().ToString().Substring(0, 8));
                                    cmd.Parameters.AddWithValue("@Plant", getValue("plant") != "" ? getValue("plant") : DBNull.Value);
                                    cmd.Parameters.AddWithValue("@FirstName", getValue("firstname") != "" ? getValue("firstname") : DBNull.Value);
                                    cmd.Parameters.AddWithValue("@LastName", getValue("lastname") != "" ? getValue("lastname") : DBNull.Value);
                                    cmd.Parameters.AddWithValue("@Email", getValue("email") != "" ? getValue("email") : DBNull.Value);
                                    cmd.Parameters.AddWithValue("@Username", username);
                                    cmd.Parameters.AddWithValue("@Password", getValue("password") != "" ? getValue("password") : "123456"); // Default Password ถ้าไม่มีระบุ
                                    cmd.Parameters.AddWithValue("@Position", getValue("position") != "" ? getValue("position") : DBNull.Value);
                                    cmd.Parameters.AddWithValue("@Section", getValue("section") != "" ? getValue("section") : DBNull.Value);
                                    cmd.Parameters.AddWithValue("@PlannerGroup", getValue("plannergroup") != "" ? getValue("plannergroup") : DBNull.Value);
                                    cmd.Parameters.AddWithValue("@SectionOption", getValue("sectionoption") != "" ? getValue("sectionoption") : DBNull.Value);
                                    cmd.Parameters.AddWithValue("@Class", getValue("class") != "" ? getValue("class") : DBNull.Value);
                                    cmd.Parameters.AddWithValue("@Rule", getValue("rule") != "" ? getValue("rule") : DBNull.Value);

                                    // จัดการฟิลด์ประเภท Status/Active (แปลงเป็น 1 หรือ 0)
                                    string activeVal = getValue("active");
                                    cmd.Parameters.AddWithValue("@Active", activeVal.Equals("true", StringComparison.OrdinalIgnoreCase) || activeVal == "1" ? 1 : 0);

                                    cmd.Parameters.AddWithValue("@Smallgroup", getValue("smallgroup") != "" ? getValue("smallgroup") : DBNull.Value);
                                    cmd.Parameters.AddWithValue("@Tel", getValue("tel") != "" ? getValue("tel") : DBNull.Value);
                                    cmd.Parameters.AddWithValue("@Work_center", getValue("work_center") != "" ? getValue("work_center") : getValue("workcenter") != "" ? getValue("workcenter") : DBNull.Value);
                                    cmd.Parameters.AddWithValue("@UserAD", getValue("userad") != "" ? getValue("userad") : DBNull.Value);
                                    cmd.Parameters.AddWithValue("@SubUsers", getValue("subusers") != "" ? getValue("subusers") : DBNull.Value);

                                    // จัดการฟิลด์ประเภทวันที่ถ้ามีใน Excel
                                    string expireActive = getValue("expire_active");
                                    if (DateTime.TryParse(expireActive, out DateTime parsedExpire))
                                        cmd.Parameters.AddWithValue("@Expire_active", parsedExpire);
                                    else
                                        cmd.Parameters.AddWithValue("@Expire_active", DBNull.Value);

                                    cmd.Parameters.AddWithValue("@LaborRateId", getValue("laborrateid") != "" ? getValue("laborrateid") : DBNull.Value);
                                    cmd.Parameters.AddWithValue("@Workpremit_class", getValue("workpremit_class") != "" ? getValue("workpremit_class") : DBNull.Value);
                                    cmd.Parameters.AddWithValue("@Plantoption", getValue("plantoption") != "" ? getValue("plantoption") : DBNull.Value);

                                    // ใช้ ExecuteScalarAsync เพื่อจับคำที่ส่งกลับมาจาก SQL
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
                }

                // 🌟 แจ้งผลลัพธ์แยกกลุ่มให้เห็นชัดเจนบนหน้า Dashboard หลัก
                TempData["Success"] = $"ซิงค์ข้อมูลผู้ใช้งานสำเร็จเรียบร้อยค่ะฝ้าย! (เพิ่มข้อมูลใหม่ {insertCount} รายการ | อัปเดตข้อมูลเดิม {updateCount} รายการ จากทั้งหมด {totalRows} แถว)";
            }
            catch (Exception ex)
            {
                _logger.LogError($"User Excel Import Error: {ex.Message}");
                TempData["Error"] = $"เกิดปัญหาฟิลด์ขัดข้องระหว่างบันทึกข้อมูลผู้ใช้: {ex.Message}";
            }

            return RedirectToAction("Index", "Maintenance");
        }
    }
}