using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ExcelDataReader;
using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;

namespace report_zycoda.Controllers
{
    [Authorize]
    public class MachineController : Controller
    {
        private readonly ILogger<MachineController> _logger;
        private readonly string _connectionString;

        public MachineController(ILogger<MachineController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
        }

        [HttpGet]
        public IActionResult Index()
        {
            return RedirectToAction("Index", "Maintenance");
        }

        [HttpGet]
        public async Task<IActionResult> GetMachineData()
        {
            var dataList = new List<Dictionary<string, object>>();

            try
            {
                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    string query = @"SELECT [MachineCode], [MachineName], [ParentCode], [Description], 
                                    [Plant], [Sections], [SectionName], [AssetNumber], 
                                    [Zone], [CID], [MachineRank], [MachineLevel], [IsActive] 
                             FROM [dbo].[Machine]";

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
                _logger.LogError($"Error fetching machine JSON: {ex.Message}");
            }

            // ส่งออกเป็น Object JSON ที่มี Property ชื่อ data ตามมาตรฐาน DataTables
            return Json(new { data = dataList });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadExcel(IFormFile excelFile)
        {
            var culture = CultureInfo.InvariantCulture;

            if (excelFile == null || excelFile.Length == 0)
            {
                TempData["Error"] = "กรุณาเลือกไฟล์ Excel (.xlsx หรือ .xls) ก่อนค่ะ";
                return RedirectToAction("Index", "Maintenance");
            }

            var fileExtension = Path.GetExtension(excelFile.FileName).ToLower();
            if (fileExtension != ".xlsx" && fileExtension != ".xls")
            {
                TempData["Error"] = "ระบบรองรับเฉพาะไฟล์ Excel (.xlsx หรือ .xls) เท่านั้นค่ะ";
                return RedirectToAction("Index", "Maintenance");
            }

            int successCount = 0;
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
                                UseHeaderRow = true
                            }
                        });

                        DataTable dt = result.Tables[0];

                        if (dt == null || dt.Rows.Count == 0)
                        {
                            TempData["Error"] = "ไม่พบข้อมูลในไฟล์ Excel ของคุณค่ะ";
                            return RedirectToAction("Index", "Maintenance");
                        }

                        totalRows = dt.Rows.Count;

                        using (SqlConnection conn = new SqlConnection(_connectionString))
                        {
                            await conn.OpenAsync();

                            string query = @"
                                IF EXISTS (SELECT 1 FROM [dbo].[Machine] WHERE [MachineCode] = @MachineCode)
                                BEGIN
                                    UPDATE [dbo].[Machine]
                                    SET [MachineName] = @MachineName,
                                        [Description] = @Description,
                                        [Plant] = @Plant,
                                        [Sections] = @Sections,
                                        [SectionName] = @SectionName,
                                        [AssetNumber] = @AssetNumber,
                                        [Zone] = @Zone,
                                        [CID] = @CID,
                                        [ParentCode] = @ParentCode,
                                        [MachineRank] = @MachineRank,
                                        [MachineLevel] = @MachineLevel,
                                        [LastUpdateDate] = GETDATE(),
                                        [LastUpdateBy] = @LastUpdateBy
                                    WHERE [MachineCode] = @MachineCode
                                END
                                ELSE
                                BEGIN
                                    INSERT INTO [dbo].[Machine] (
                                        [MachineCode], [MachineName], [Description], [Plant], [Sections], 
                                        [SectionName], [AssetNumber], [Zone], [CID], [ParentCode], 
                                        [MachineRank], [MachineLevel], [IsActive], [CreatedDate], [LastUpdateBy]
                                    ) VALUES (
                                        @MachineCode, @MachineName, @Description, @Plant, @Sections, 
                                        @SectionName, @AssetNumber, @Zone, @CID, @ParentCode, 
                                        @MachineRank, @MachineLevel, 1, GETDATE(), @LastUpdateBy
                                    )
                                END";

                            string username = User.Identity?.Name ?? "System_Excel";

                            foreach (DataRow row in dt.Rows)
                            {
                                Func<string, string> getValue = (colName) => {
                                    var col = dt.Columns.Cast<DataColumn>()
                                        .FirstOrDefault(c => c.ColumnName.Trim().Equals(colName.Trim(), StringComparison.OrdinalIgnoreCase));
                                    return col != null ? row[col]?.ToString()?.Trim() ?? "" : "";
                                };

                                // 🟢 แมปตามหัวตารางใบจริงที่ฝ้ายส่งมาเป๊ะๆ ครับ
                                string machineCode = getValue("id");
                                if (string.IsNullOrEmpty(machineCode)) continue;

                                // ดึงจากคอลัมน์ machineName ท้ายตาราง ถ้าไม่มีให้ใช้ตัวแปรรายละเอียด (detail) หรือรหัสแทน
                                string machineName = getValue("machineName");
                                if (string.IsNullOrEmpty(machineName)) machineName = getValue("detail");
                                if (string.IsNullOrEmpty(machineName)) machineName = machineCode;

                                string description = getValue("detail");
                                string plant = getValue("plant");
                                string sections = getValue("sectionid");
                                if (string.IsNullOrEmpty(sections)) sections = getValue("section_id");

                                string sectionName = getValue("sectionowner");
                                string assetNumber = getValue("asset_replacement_value");
                                string zone = getValue("zone_id");
                                string cid = getValue("cid");
                                string parentCode = getValue("parent_id"); // 🎯 เปลี่ยนเป็น parent_id ตามรูปจริง
                                string machineRank = getValue("ran");       // 🎯 ในรูป Excel หัวคอลัมน์หลุดพิมพ์คำว่า 'ran' 
                                if (string.IsNullOrEmpty(machineRank)) machineRank = getValue("rank");

                                int.TryParse(getValue("level"), out int machineLevel);

                                using (SqlCommand cmd = new SqlCommand(query, conn))
                                {
                                    cmd.Parameters.AddWithValue("@MachineCode", machineCode);
                                    cmd.Parameters.AddWithValue("@MachineName", machineName);
                                    cmd.Parameters.AddWithValue("@Description", string.IsNullOrEmpty(description) ? DBNull.Value : description);
                                    cmd.Parameters.AddWithValue("@Plant", string.IsNullOrEmpty(plant) ? DBNull.Value : plant);
                                    cmd.Parameters.AddWithValue("@Sections", string.IsNullOrEmpty(sections) ? DBNull.Value : sections);
                                    cmd.Parameters.AddWithValue("@SectionName", string.IsNullOrEmpty(sectionName) ? DBNull.Value : sectionName);
                                    cmd.Parameters.AddWithValue("@AssetNumber", string.IsNullOrEmpty(assetNumber) ? DBNull.Value : assetNumber);
                                    cmd.Parameters.AddWithValue("@Zone", string.IsNullOrEmpty(zone) ? DBNull.Value : zone);
                                    cmd.Parameters.AddWithValue("@CID", string.IsNullOrEmpty(cid) ? DBNull.Value : cid);
                                    cmd.Parameters.AddWithValue("@ParentCode", string.IsNullOrEmpty(parentCode) ? DBNull.Value : parentCode);
                                    cmd.Parameters.AddWithValue("@MachineRank", string.IsNullOrEmpty(machineRank) ? DBNull.Value : machineRank);
                                    cmd.Parameters.AddWithValue("@MachineLevel", machineLevel == 0 ? DBNull.Value : machineLevel);
                                    cmd.Parameters.AddWithValue("@LastUpdateBy", username);

                                    await cmd.ExecuteNonQueryAsync();
                                    successCount++;
                                }
                            }
                        }
                    }
                }

                TempData["Success"] = $"นำเข้าข้อมูลสำเร็จเรียบร้อยแล้วค่ะ! (ผ่านทั้งหมด {successCount} จาก {totalRows} รายการ)";
            }
            catch (Exception ex)
            {
                _logger.LogError($"Excel Import Error: {ex.Message}");
                TempData["Error"] = $"เกิดข้อผิดพลาดในระหว่างบันทึกข้อมูล: {ex.Message}";
            }

            return RedirectToAction("Index", "Maintenance");
        }
    }
}