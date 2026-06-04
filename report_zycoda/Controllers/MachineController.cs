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
        private readonly string _zycodaApiConnectionString;

        public MachineController(ILogger<MachineController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
            _zycodaApiConnectionString = configuration.GetConnectionString("ZycodaApiConnection") ?? _connectionString;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var dt = new DataTable();
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
                    using (SqlDataAdapter adapter = new SqlDataAdapter(cmd))
                    {
                        adapter.Fill(dt);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error filling MachineTable for ViewBag: {ex.Message}");
            }

            ViewBag.MachineTable = dt;
            return View();
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

            return Json(new { data = dataList });
        }

        [HttpPost]
        public async Task<IActionResult> SyncJobOrders()
        {
            try
            {
                var sourceJobs = new List<Dictionary<string, object>>();

                using (SqlConnection apiConn = new SqlConnection(_zycodaApiConnectionString))
                {
                    await apiConn.OpenAsync();
                    string fetchQuery = @"SELECT [id], [refid], [reforder], [MN], [MO], [detail], [code], 
                                                 [section], [statustext], [timecreate], [timeclose], [status]
                                          FROM [ZycodaApiByPB].[dbo].[Job_order]
                                          WHERE [refid] IS NOT NULL AND [refid] <> ''";

                    using (SqlCommand cmd = new SqlCommand(fetchQuery, apiConn))
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var job = new Dictionary<string, object>();
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                job[reader.GetName(i)] = reader.GetValue(i);
                            }
                            sourceJobs.Add(job);
                        }
                    }
                }

                if (sourceJobs.Count == 0)
                {
                    return Json(new { success = true, message = "ไม่พบข้อมูลจ็อบใหม่บนเกตเวย์ API สัญญาณปกติค่ะ" });
                }

                using (SqlConnection localConn = new SqlConnection(_connectionString))
                {
                    await localConn.OpenAsync();

                    string upsertQuery = @"
                        IF EXISTS (SELECT 1 FROM [dbo].[Job_order] WHERE [refid] = @refid)
                        BEGIN
                            UPDATE [dbo].[Job_order]
                            SET 
                                [id_db] = @id,
                                [reforder] = @reforder,
                                [MN] = @MN,
                                [MO] = @MO,
                                [detail] = @detail,
                                [code] = @code,
                                [section] = @section,
                                [statustext] = @statustext,
                                [timeclose] = @timeclose,
                                [status] = @status,
                                [LastSyncDate] = GETDATE()
                            WHERE [refid] = @refid;
                        END
                        ELSE
                        BEGIN
                            INSERT INTO [dbo].[Job_order] (
                                [id_db], [refid], [reforder], [MN], [MO], [detail], [code], 
                                [section], [statustext], [timecreate], [timeclose], [status], [LastSyncDate]
                            )
                            VALUES (
                                @id, @refid, @reforder, @MN, @MO, @detail, @code, 
                                @section, @statustext, @timecreate, @timeclose, @status, GETDATE()
                            );
                        END;";

                    // สั่งเปิด Transaction สำหรับกลุ่มข้อมูล Sync
                    using (SqlTransaction transaction = localConn.BeginTransaction())
                    {
                        try
                        {
                            using (SqlCommand upsertCmd = new SqlCommand(upsertQuery, localConn, transaction))
                            {
                                // ประกาศกำหนดจำพวกตัวแปรล่วงหน้า (จอง Type & Size ไว้เพื่อเพิ่มความเร็วในการรันซ้ำๆ)
                                upsertCmd.Parameters.Add("@id", SqlDbType.Int);
                                upsertCmd.Parameters.Add("@refid", SqlDbType.VarChar, 50);
                                upsertCmd.Parameters.Add("@reforder", SqlDbType.VarChar, 50);
                                upsertCmd.Parameters.Add("@MN", SqlDbType.NVarChar, 100);
                                upsertCmd.Parameters.Add("@MO", SqlDbType.NVarChar, 100);
                                upsertCmd.Parameters.Add("@detail", SqlDbType.NVarChar, -1); // -1 คือ MAX
                                upsertCmd.Parameters.Add("@code", SqlDbType.VarChar, 50);
                                upsertCmd.Parameters.Add("@section", SqlDbType.NVarChar, 100);
                                upsertCmd.Parameters.Add("@statustext", SqlDbType.NVarChar, 50);
                                upsertCmd.Parameters.Add("@timecreate", SqlDbType.DateTime);
                                upsertCmd.Parameters.Add("@timeclose", SqlDbType.DateTime);
                                upsertCmd.Parameters.Add("@status", SqlDbType.Int);

                                foreach (var job in sourceJobs)
                                {
                                    upsertCmd.Parameters["@id"].Value = job["id"] ?? DBNull.Value;
                                    upsertCmd.Parameters["@refid"].Value = job["refid"] ?? DBNull.Value;
                                    upsertCmd.Parameters["@reforder"].Value = job["reforder"] ?? DBNull.Value;
                                    upsertCmd.Parameters["@MN"].Value = job["MN"] ?? DBNull.Value;
                                    upsertCmd.Parameters["@MO"].Value = job["MO"] ?? DBNull.Value;
                                    upsertCmd.Parameters["@detail"].Value = job["detail"] ?? DBNull.Value;
                                    upsertCmd.Parameters["@code"].Value = job["code"] ?? DBNull.Value;
                                    upsertCmd.Parameters["@section"].Value = job["section"] ?? DBNull.Value;
                                    upsertCmd.Parameters["@statustext"].Value = job["statustext"] ?? DBNull.Value;
                                    upsertCmd.Parameters["@timecreate"].Value = job["timecreate"] ?? DBNull.Value;
                                    upsertCmd.Parameters["@timeclose"].Value = job["timeclose"] ?? DBNull.Value;
                                    upsertCmd.Parameters["@status"].Value = job["status"] ?? DBNull.Value;

                                    await upsertCmd.ExecuteNonQueryAsync();
                                }
                            }
                            await transaction.CommitAsync();
                        }
                        catch
                        {
                            await transaction.RollbackAsync();
                            throw;
                        }
                    }
                }

                return Json(new { success = true, message = $"ระบบ Sync และจับคู่ชุดข้อมูลจ็อบเรียบร้อยแล้วค่ะ ทั้งหมด {sourceJobs.Count} รายการ" });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Zycoda Job Sync Error: {ex.Message}");
                return Json(new { success = false, message = $"เกิดข้อผิดพลาดในการเชื่อมต่อโครงข่าย: {ex.Message}" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadExcel(IFormFile excelFile)
        {
            if (excelFile == null || excelFile.Length == 0)
            {
                TempData["Error"] = "กรุณาเลือกไฟล์ Excel (.xlsx หรือ .xls) ก่อนค่ะ";
                return RedirectToAction("Index");
            }

            var fileExtension = Path.GetExtension(excelFile.FileName).ToLower();
            if (fileExtension != ".xlsx" && fileExtension != ".xls")
            {
                TempData["Error"] = "ระบบรองรับเฉพาะไฟล์ Excel (.xlsx หรือ .xls) เท่านั้นค่ะ";
                return RedirectToAction("Index");
            }

            int successCount = 0;
            int totalRows = 0;

            try
            {
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

                using (var stream = excelFile.OpenReadStream())
                using (var reader = ExcelReaderFactory.CreateReader(stream))
                {
                    var result = reader.AsDataSet(new ExcelDataSetConfiguration()
                    {
                        ConfigureDataTable = (_) => new ExcelDataTableConfiguration() { UseHeaderRow = true }
                    });

                    DataTable dt = result.Tables[0];
                    if (dt == null || dt.Rows.Count == 0)
                    {
                        TempData["Error"] = "ไม่พบข้อมูลในไฟล์ Excel ของคุณค่ะ";
                        return RedirectToAction("Index");
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

                        // ใช้ Transaction ครอบการอัปโหลดไฟล์ Excel เพื่อความชัวร์ของข้อมูล
                        using (SqlTransaction transaction = conn.BeginTransaction())
                        {
                            try
                            {
                                using (SqlCommand cmd = new SqlCommand(query, conn, transaction))
                                {
                                    // กำหนดประเภทตัวแปรล่วงหน้าก่อนเข้าลูป
                                    cmd.Parameters.Add("@MachineCode", SqlDbType.VarChar, 50);
                                    cmd.Parameters.Add("@MachineName", SqlDbType.NVarChar, 255);
                                    cmd.Parameters.Add("@Description", SqlDbType.NVarChar, -1);
                                    cmd.Parameters.Add("@Plant", SqlDbType.NVarChar, 100);
                                    cmd.Parameters.Add("@Sections", SqlDbType.VarChar, 50);
                                    cmd.Parameters.Add("@SectionName", SqlDbType.NVarChar, 100);
                                    cmd.Parameters.Add("@AssetNumber", SqlDbType.VarChar, 100);
                                    cmd.Parameters.Add("@Zone", SqlDbType.VarChar, 50);
                                    cmd.Parameters.Add("@CID", SqlDbType.VarChar, 50);
                                    cmd.Parameters.Add("@ParentCode", SqlDbType.VarChar, 50);
                                    cmd.Parameters.Add("@MachineRank", SqlDbType.VarChar, 10);
                                    cmd.Parameters.Add("@MachineLevel", SqlDbType.Int);
                                    cmd.Parameters.Add("@LastUpdateBy", SqlDbType.NVarChar, 100);

                                    foreach (DataRow row in dt.Rows)
                                    {
                                        Func<string, string> getValue = (colName) => {
                                            var col = dt.Columns.Cast<DataColumn>()
                                                .FirstOrDefault(c => c.ColumnName.Trim().Equals(colName.Trim(), StringComparison.OrdinalIgnoreCase));
                                            return col != null ? row[col]?.ToString()?.Trim() ?? "" : "";
                                        };

                                        string machineCode = getValue("id");
                                        if (string.IsNullOrEmpty(machineCode)) continue;

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
                                        string parentCode = getValue("parent_id");
                                        string machineRank = getValue("ran");
                                        if (string.IsNullOrEmpty(machineRank)) machineRank = getValue("rank");

                                        int.TryParse(getValue("level"), out int machineLevel);

                                        // ส่งข้อมูลเข้า Parameters ในตู้จอง
                                        cmd.Parameters["@MachineCode"].Value = machineCode;
                                        cmd.Parameters["@MachineName"].Value = machineName;
                                        cmd.Parameters["@Description"].Value = string.IsNullOrEmpty(description) ? DBNull.Value : description;
                                        cmd.Parameters["@Plant"].Value = string.IsNullOrEmpty(plant) ? DBNull.Value : plant;
                                        cmd.Parameters["@Sections"].Value = string.IsNullOrEmpty(sections) ? DBNull.Value : sections;
                                        cmd.Parameters["@SectionName"].Value = string.IsNullOrEmpty(sectionName) ? DBNull.Value : sectionName;
                                        cmd.Parameters["@AssetNumber"].Value = string.IsNullOrEmpty(assetNumber) ? DBNull.Value : assetNumber;
                                        cmd.Parameters["@Zone"].Value = string.IsNullOrEmpty(zone) ? DBNull.Value : zone;
                                        cmd.Parameters["@CID"].Value = string.IsNullOrEmpty(cid) ? DBNull.Value : cid;
                                        cmd.Parameters["@ParentCode"].Value = string.IsNullOrEmpty(parentCode) ? DBNull.Value : parentCode;
                                        cmd.Parameters["@MachineRank"].Value = string.IsNullOrEmpty(machineRank) ? DBNull.Value : machineRank;
                                        cmd.Parameters["@MachineLevel"].Value = machineLevel == 0 ? DBNull.Value : machineLevel;
                                        cmd.Parameters["@LastUpdateBy"].Value = username;

                                        await cmd.ExecuteNonQueryAsync();
                                        successCount++;
                                    }
                                }
                                await transaction.CommitAsync();
                            }
                            catch
                            {
                                await transaction.RollbackAsync();
                                throw;
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

            return RedirectToAction("Index");
        }
    }
}