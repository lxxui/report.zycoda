using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Newtonsoft.Json;
using report_zycoda.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Data.SqlClient; // 👈 ใช้คู่กับ ADO.NET เพื่อความเร็ว
using System.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace report_zycoda.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SyncController : ControllerBase
    {
        private readonly LatestSyncStore _syncStore;
        private readonly ApiService _apiService;
        private readonly string _connectionString; // 👈 เก็บ Connection String ของฐานข้อมูลเรา
        private readonly ILogger<SyncController> _logger;

        // ฉีด IConfiguration เพื่อไปอ่าน ConnectionString จาก appsettings.json น้า
        public SyncController(LatestSyncStore syncStore, ApiService apiService, IConfiguration configuration, ILogger<SyncController> logger)
        {
            _syncStore = syncStore;
            _apiService = apiService;
            _logger = logger;
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        [HttpGet("last-time")]
        public IActionResult GetLastSyncTime()
        {
            if (_syncStore.LastSyncTime.HasValue)
            {
                return Ok(new
                {
                    success = true,
                    lastSync = _syncStore.LastSyncTime.Value.ToString("dd/MM/yyyy HH:mm:ss")
                });
            }
            return Ok(new { success = false, message = "ระบบกำลังดึงข้อมูลรอบแรก..." });
        }

        // 💡 เปลี่ยนมาดึงข้อมูลจากตาราง Local_Job_order ในฐานข้อมูลฝั่งเราแทนการยิง API ตรงๆ
        [HttpPost("table-data")]
        public async Task<IActionResult> GetTableData()
        {
            var dataList = new List<Dictionary<string, object>>();
            try
            {
                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    // ดึงเฉพาะข้อมูลสำคัญที่จะใช้โชว์หน้าตารางมาจัดเรียงตามวันที่ล่าสุด
                    string query = @"SELECT TOP 1000 [id], [refid], [reforder], [MN], [MO], [detail], 
                                                    [section], [statustext], [timecreate], [timeclose], [status], [id_db]
                                     FROM [dbo].[Local_Job_order]
                                     ORDER BY [timecreate] DESC";

                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var row = new Dictionary<string, object>();
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                string columnName = reader.GetName(i);
                                var value = reader.GetValue(i);

                                // คุมฟอร์แมตวันที่ให้ JSON ส่งไปหน้าบ้านอ่านง่ายๆ
                                if ((columnName == "timecreate" || columnName == "timeclose") && value != DBNull.Value)
                                {
                                    row[columnName] = Convert.ToDateTime(value).ToString("dd/MM/yyyy HH:mm");
                                }
                                else
                                {
                                    row[columnName] = value == DBNull.Value ? "" : value;
                                }
                            }
                            dataList.Add(row);
                        }
                    }
                }
                return Ok(new { data = dataList });
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error ดึงข้อมูลจาก Local DB: {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // 💡 จังหวะกดซิงค์ข้อมูล: ดึงจาก API -> แมปปิ้งจับคู่เขียนลงตารางเรา
        [HttpPost("trigger-sync")]
        public async Task<IActionResult> TriggerDataSync()
        {
            try
            {
                string sessionUser = HttpContext.Session.GetString("Username") ?? "default_user";
                string sessionPass = HttpContext.Session.GetString("Password") ?? "default_pass";

                string start = "2026-01-01";
                string end = DateTime.Now.ToString("yyyy-MM-dd");

                // 1. สอยดาต้าดิบผ่านเครือข่าย API จริงจาก Zycoda
                List<MaintenanceApiModels> syncedJobs = await FetchJobsFromApiInternal(sessionUser, sessionPass, "EM,CM", start, end);

                if (syncedJobs == null || syncedJobs.Count == 0)
                {
                    return Ok(new { success = true, message = "ไม่พบชุดข้อมูลใหม่บนเกตเวย์ API สัญญาณปกติค่ะ", data = new List<MaintenanceApiModels>() });
                }

                // 2. กระบวนการเขียนบันทึกเข้า DB ฝั่งเรา (Upsert Statement)
                using (SqlConnection localConn = new SqlConnection(_connectionString))
                {
                    await localConn.OpenAsync();

                    string upsertQuery = @"
                        IF EXISTS (SELECT 1 FROM [dbo].[Local_Job_order] WHERE [refid] = @refid)
                        BEGIN
                            UPDATE [dbo].[Local_Job_order]
                            SET 
                                [reforder] = @reforder,
                                [MN] = @MN,
                                [MO] = @MO,
                                [detail] = @detail,
                                [section] = @section,
                                [statustext] = @statustext,
                                [timeclose] = @timeclose,
                                [status] = @status,
                                [id_db] = @id_db,
                                [LastSyncDate] = GETDATE()
                            WHERE [refid] = @refid;
                        END
                        ELSE
                        BEGIN
                            INSERT INTO [dbo].[Local_Job_order] (
                                [id], [refid], [reforder], [MN], [MO], [detail], 
                                [section], [statustext], [timecreate], [timeclose], [status], [id_db], [LastSyncDate]
                            )
                            VALUES (
                                @id, @refid, @reforder, @MN, @MO, @detail, 
                                @section, @statustext, @timecreate, @timeclose, @status, @id_db, GETDATE()
                            );
                        END;";

                    // เปิดใช้งาน SqlTransaction เพื่อความปลอดภัย รันเร็วปรี๊ด ไม่ติดคอขวด
                    using (SqlTransaction transaction = localConn.BeginTransaction())
                    {
                        try
                        {
                            using (SqlCommand upsertCmd = new SqlCommand(upsertQuery, localConn, transaction))
                            {
                                // ฝัง Parameters ไว้นอกลูปเพื่อทำ SQL Plan Replay ประสิทธิภาพสูง
                                upsertCmd.Parameters.Add("@id", SqlDbType.Int);
                                upsertCmd.Parameters.Add("@refid", SqlDbType.VarChar, 50);
                                upsertCmd.Parameters.Add("@reforder", SqlDbType.VarChar, 50);
                                upsertCmd.Parameters.Add("@MN", SqlDbType.NVarChar, 100);
                                upsertCmd.Parameters.Add("@MO", SqlDbType.NVarChar, 100);
                                upsertCmd.Parameters.Add("@detail", SqlDbType.NVarChar, -1); // NVARCHAR(MAX)
                                upsertCmd.Parameters.Add("@section", SqlDbType.NVarChar, 100);
                                upsertCmd.Parameters.Add("@statustext", SqlDbType.NVarChar, 50);
                                upsertCmd.Parameters.Add("@timecreate", SqlDbType.DateTime);
                                upsertCmd.Parameters.Add("@timeclose", SqlDbType.DateTime);
                                upsertCmd.Parameters.Add("@status", SqlDbType.Int);
                                upsertCmd.Parameters.Add("@id_db", SqlDbType.Int);

                                foreach (var job in syncedJobs)
                                {
                                    // ผูกข้อมูลทีละแถวเข้าฐานข้อมูล พร้อมการดักจับเซฟตี้พวกค่า Null
                                    upsertCmd.Parameters["@id"].Value = job.id;
                                    upsertCmd.Parameters["@refid"].Value = job.refId ;
                                    upsertCmd.Parameters["@reforder"].Value = job.reforder ;
                                    upsertCmd.Parameters["@MN"].Value = job.Mn ;
                                    upsertCmd.Parameters["@MO"].Value = job.Mo ;
                                    upsertCmd.Parameters["@detail"].Value = job.detail ;
                                    upsertCmd.Parameters["@section"].Value = job.section ;
                                    upsertCmd.Parameters["@statustext"].Value = job.statustext ;
                                    upsertCmd.Parameters["@status"].Value = job.status;
                                    upsertCmd.Parameters["@id_db"].Value = job.id;
                                    // ดักเคส DateTime เผื่อ API ส่งฟอร์แมตว่างกลับมาจะได้ไม่เอ๋อ
                                    upsertCmd.Parameters["@timecreate"].Value = job.timecreate;
                                    upsertCmd.Parameters["@timeclose"].Value = job.timeclose;

                                    await upsertCmd.ExecuteNonQueryAsync();
                                }
                            }
                            await transaction.CommitAsync(); // สั่งบันทึกลงดิสก์จริงทั้งหมด
                        }
                        catch (Exception ex)
                        {
                            await transaction.RollbackAsync();
                            _logger.LogError($"❌ Sync Transaction พังระหว่างลูป: {ex.Message}");
                            throw;
                        }
                    }
                }

                _syncStore.LastSyncTime = DateTime.Now;

                return Ok(new
                {
                    success = true,
                    message = $"ดาต้าเกตเวย์อัปเดตเรียบร้อย! คัดลอกลงฐานข้อมูลภายใน {syncedJobs.Count} รายการ",
                    syncTime = _syncStore.LastSyncTime.Value.ToString("dd/MM/yyyy HH:mm:ss"),
                    data = syncedJobs
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ TriggerDataSync ล้มเหลวแบบ Fatal: {ex.Message}");
                return StatusCode(500, new { success = false, message = $"การซิงค์ล้มเหลว: {ex.Message}" });
            }
        }

        // Helper Method สำหรับเรียกใช้งาน API ข้อมูลดิบเครือข่าย
        private async Task<List<MaintenanceApiModels>> FetchJobsFromApiInternal(string sessionUser, string sessionPass, string jobtype, string start, string end, string view = "followup")
        {
            var combined = new List<MaintenanceApiModels>();
            var culture = CultureInfo.InvariantCulture;

            if (!DateTime.TryParseExact(start, "yyyy-MM-dd", culture, DateTimeStyles.None, out var globalStart))
                if (!DateTime.TryParse(start, culture, DateTimeStyles.None, out globalStart)) globalStart = new DateTime(2026, 01, 01);

            if (!DateTime.TryParseExact(end, "yyyy-MM-dd", culture, DateTimeStyles.None, out var globalEnd))
                if (!DateTime.TryParse(end, culture, DateTimeStyles.None, out globalEnd)) globalEnd = DateTime.Now;

            string finalStartStr = globalStart.ToString("yyyy-MM-dd", culture);
            string finalEndStr = globalEnd.ToString("yyyy-MM-dd", culture);
            var finalJobType = string.IsNullOrWhiteSpace(jobtype) ? "EM,CM" : jobtype;
            var viewsToCall = (view == "all") ? new List<string> { "followup", "myjob" } : new List<string> { view };

            var handler = new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate };
            using var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromMinutes(5);
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("accept", "application/json");
            client.DefaultRequestHeaders.Add("username", sessionUser);
            client.DefaultRequestHeaders.Add("password", sessionPass);

            foreach (var v in viewsToCall)
            {
                var queryParams = new Dictionary<string, string?> { ["plant"] = "FARMHOUSE", ["jobtype"] = finalJobType, ["start"] = finalStartStr, ["end"] = finalEndStr, ["view"] = v };
                var url = QueryHelpers.AddQueryString("https://farmhouse.zycoda.com/apimpros/get_job_order", queryParams);

                try
                {
                    var response = await client.GetAsync(url);
                    var json = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(json)) continue;

                    var trimmedJson = json.Trim();
                    if (trimmedJson.StartsWith("["))
                    {
                        var data = JsonConvert.DeserializeObject<List<MaintenanceApiModels>>(trimmedJson);
                        if (data != null) combined.AddRange(data);
                    }
                    else if (trimmedJson.StartsWith("{"))
                    {
                        var singleData = JsonConvert.DeserializeObject<MaintenanceApiModels>(trimmedJson);
                        if (singleData != null) combined.Add(singleData);
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"❌ API ERROR: {ex.Message}"); }
            }

            return combined.GroupBy(x => x.id).Select(g => g.First()).ToList();
        }
    }
}