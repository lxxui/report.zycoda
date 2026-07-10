using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Newtonsoft.Json;
using report_zycoda.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Data.SqlClient;
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
        private readonly string _connectionString;
        private readonly ILogger<SyncController> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        public SyncController(
            LatestSyncStore syncStore,
            ApiService apiService,
            IConfiguration configuration,
            ILogger<SyncController> logger,
            IHttpClientFactory httpClientFactory)
        {
            _syncStore = syncStore;
            _apiService = apiService;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
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

        [HttpPost("table-data")]
        public async Task<IActionResult> GetTableData()
        {
            var dataList = new List<Dictionary<string, object>>();
            try
            {
                using (SqlConnection conn = new(_connectionString))
                {
                    await conn.OpenAsync();

                    // 🔧 ปรับฟิลด์คีย์หลักให้ดึงมาจาก [Id] และ [id_db] ให้ครบถ้วนตามที่เราตั้งลำดับใน DB ล่าสุด
                    string query = @"SELECT TOP 1000 [Id], [refid], [reforder], [MN], [MO], [detail], 
                                                     [section], [statustext], [timecreate], [timeclose], [status], [id_db]
                                     FROM [dbo].[Job_order]
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

        [HttpPost("trigger-sync")]
        public async Task<IActionResult> TriggerDataSync()
        {
            try
            {
                if (User.Identity == null || !User.Identity.IsAuthenticated)
                {
                    return Unauthorized(new { success = false, message = "กรุณาเข้าสู่ระบบก่อนทำการซิงค์" });
                }

                string sessionUser = User.Identity.Name ?? "";
                string sessionPass = User.Claims.FirstOrDefault(c => c.Type == "ApiPassword")?.Value ?? "";

                if (string.IsNullOrWhiteSpace(sessionUser) || string.IsNullOrWhiteSpace(sessionPass))
                {
                    return Unauthorized(new { success = false, message = "ไม่พบข้อมูลสิทธิ์เข้าใช้งาน API กรุณาเข้าสู่ระบบใหม่อีกครั้ง" });
                }

                string start = DateTime.Now.AddDays(-7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                string end = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

                List<MaintenanceApiModels> syncedJobs = await FetchJobsFromApiInternal(sessionUser, sessionPass, "EM,CM", start, end, "all");

                if (syncedJobs == null || syncedJobs.Count == 0)
                {
                    return Ok(new { success = true, message = "ไม่พบชุดข้อมูลใหม่บนเกตเวย์ API สัญญาณปกติค่ะ", data = new List<MaintenanceApiModels>() });
                }

                int affectedCount = 0;

                using (SqlConnection localConn = new(_connectionString))
                {
                    await localConn.OpenAsync();

                    // 🔧 อัปเดตโครงสร้าง Query ให้ครอบคลุมฟิลด์ทั้งหมดจากฐานข้อมูลล่าสุด
                    string upsertQuery = @"
                IF EXISTS (SELECT 1 FROM [dbo].[Job_order] WHERE [Id] = @Id)
                BEGIN
                    UPDATE [dbo].[Job_order]
                    SET 
                        [id_db] = @id_db, [refid] = @refid, [reforder] = @reforder, [MN] = @MN, [MO] = @MO,
                        [detail] = @detail, [safety] = @safety, [code] = @code, [fl] = @fl, [downtime] = @downtime,
                        [difftime] = @difftime, [timerepair] = @timerepair, [timerepair_ot] = @timerepair_ot,
                        [tag_abnormal] = @tag_abnormal, [tag] = @tag, [tags] = @tags, [jobtype] = @jobtype,
                        [section] = @section, [productionstop] = @productionstop, [planner] = @planner,
                        [statustext] = @statustext, [statussection] = @statussection, [opt_5s] = @opt_5s,
                        [opt_5s_comment] = @opt_5s_comment, [opt_protect] = @opt_protect, 
                        [opt_protect_comment] = @opt_protect_comment, [rates] = @rates,
                        [timecreate] = @timecreate, [timeassign] = @timeassign, [timestart] = @timestart,
                        [timeend] = @timeend, [timefinish] = @timefinish, [timeaccept] = @timeaccept,
                        [timestartrepair] = @timestartrepair, [timeendrepair] = @timeendrepair, [timeclose] = @timeclose,
                        [timerunning] = @timerunning, [timeday] = @timeday, [fldetail] = @fldetail, [flrank] = @flrank,
                        [flpngrp] = @flpngrp, [priority] = @priority, [status] = @status, [usercreate] = @usercreate,
                        [sectioncreate] = @sectioncreate, [useraccept] = @useraccept, [userfinish] = @userfinish,
                        [comment] = @comment, [solution] = @solution, [problem] = @problem, [causes] = @causes,
                        [preventive] = @preventive, [ordertype] = @ordertype, [submiss] = @submiss, [bdfac] = @bdfac
                    WHERE [Id] = @Id;
                END
                ELSE
                BEGIN
                    INSERT INTO [dbo].[Job_order] (
                        [Id], [id_db], [refid], [reforder], [MN], [MO], [detail], [safety], [code], [fl], [downtime],
                        [difftime], [timerepair], [timerepair_ot], [tag_abnormal], [tag], [tags], [jobtype], [section],
                        [productionstop], [planner], [statustext], [statussection], [opt_5s], [opt_5s_comment],
                        [opt_protect], [opt_protect_comment], [rates], [timecreate], [timeassign], [timestart],
                        [timeend], [timefinish], [timeaccept], [timestartrepair], [timeendrepair], [timeclose],
                        [timerunning], [timeday], [fldetail], [flrank], [flpngrp], [priority], [status], [usercreate],
                        [sectioncreate], [useraccept], [userfinish], [comment], [solution], [problem], [causes],
                        [preventive], [ordertype], [submiss], [bdfac]
                    )
                    VALUES (
                        @Id, @id_db, @refid, @reforder, @MN, @MO, @detail, @safety, @code, @fl, @downtime,
                        @difftime, @timerepair, @timerepair_ot, @tag_abnormal, @tag, @tags, @jobtype, @section,
                        @productionstop, @planner, @statustext, @statussection, @opt_5s, @opt_5s_comment,
                        @opt_protect, @opt_protect_comment, @rates, @timecreate, @timeassign, @timestart,
                        @timeend, @timefinish, @timeaccept, @timestartrepair, @timeendrepair, @timeclose,
                        @timerunning, @timeday, @fldetail, @flrank, @flpngrp, @priority, @status, @usercreate,
                        @sectioncreate, @useraccept, @userfinish, @comment, @solution, @problem, @causes,
                        @preventive, @ordertype, @submiss, @bdfac
                    );
                END;";

                    using (SqlTransaction transaction = localConn.BeginTransaction())
                    {
                        try
                        {
                            using (SqlCommand upsertCmd = new SqlCommand(upsertQuery, localConn, transaction))
                            {
                                // 1. เพิ่มการประกาศ Parameters ให้ครบถ้วนตาม Data Type ของตาราง
                                upsertCmd.Parameters.Add("@Id", SqlDbType.NVarChar, 50);
                                upsertCmd.Parameters.Add("@id_db", SqlDbType.NVarChar, 50);
                                upsertCmd.Parameters.Add("@refid", SqlDbType.NVarChar, 50);
                                upsertCmd.Parameters.Add("@reforder", SqlDbType.NVarChar, 50);
                                upsertCmd.Parameters.Add("@MN", SqlDbType.NVarChar, 100);
                                upsertCmd.Parameters.Add("@MO", SqlDbType.NVarChar, 100);
                                upsertCmd.Parameters.Add("@detail", SqlDbType.NVarChar, -1);
                                upsertCmd.Parameters.Add("@safety", SqlDbType.NVarChar, 100);
                                upsertCmd.Parameters.Add("@code", SqlDbType.NVarChar, 50);
                                upsertCmd.Parameters.Add("@fl", SqlDbType.NVarChar, 100);
                                upsertCmd.Parameters.Add("@downtime", SqlDbType.NVarChar, 50);
                                upsertCmd.Parameters.Add("@difftime", SqlDbType.NVarChar, 50);
                                upsertCmd.Parameters.Add("@timerepair", SqlDbType.NVarChar, 50);
                                upsertCmd.Parameters.Add("@timerepair_ot", SqlDbType.NVarChar, 50);
                                upsertCmd.Parameters.Add("@tag_abnormal", SqlDbType.NVarChar, 50);
                                upsertCmd.Parameters.Add("@tag", SqlDbType.NVarChar, 50);
                                upsertCmd.Parameters.Add("@tags", SqlDbType.NVarChar, 100);
                                upsertCmd.Parameters.Add("@jobtype", SqlDbType.NVarChar, 50);
                                upsertCmd.Parameters.Add("@section", SqlDbType.NVarChar, 100);
                                upsertCmd.Parameters.Add("@productionstop", SqlDbType.NVarChar, 50);
                                upsertCmd.Parameters.Add("@planner", SqlDbType.NVarChar, 100);
                                upsertCmd.Parameters.Add("@statustext", SqlDbType.NVarChar, 50);
                                upsertCmd.Parameters.Add("@statussection", SqlDbType.NVarChar, 50);
                                upsertCmd.Parameters.Add("@opt_5s", SqlDbType.NVarChar, 50);
                                upsertCmd.Parameters.Add("@opt_5s_comment", SqlDbType.NVarChar, -1);
                                upsertCmd.Parameters.Add("@opt_protect", SqlDbType.NVarChar, 50);
                                upsertCmd.Parameters.Add("@opt_protect_comment", SqlDbType.NVarChar, -1);
                                upsertCmd.Parameters.Add("@rates", SqlDbType.NVarChar, 50);

                                // DateTime Parameters
                                upsertCmd.Parameters.Add("@timecreate", SqlDbType.DateTime);
                                upsertCmd.Parameters.Add("@timeassign", SqlDbType.DateTime);
                                upsertCmd.Parameters.Add("@timestart", SqlDbType.DateTime);
                                upsertCmd.Parameters.Add("@timeend", SqlDbType.DateTime);
                                upsertCmd.Parameters.Add("@timefinish", SqlDbType.DateTime);
                                upsertCmd.Parameters.Add("@timeaccept", SqlDbType.DateTime);
                                upsertCmd.Parameters.Add("@timestartrepair", SqlDbType.DateTime);
                                upsertCmd.Parameters.Add("@timeendrepair", SqlDbType.DateTime);
                                upsertCmd.Parameters.Add("@timeclose", SqlDbType.DateTime);

                                upsertCmd.Parameters.Add("@timerunning", SqlDbType.NVarChar, 50);
                                upsertCmd.Parameters.Add("@timeday", SqlDbType.NVarChar, 50);
                                upsertCmd.Parameters.Add("@fldetail", SqlDbType.NVarChar, -1);
                                upsertCmd.Parameters.Add("@flrank", SqlDbType.NVarChar, 50);
                                upsertCmd.Parameters.Add("@flpngrp", SqlDbType.NVarChar, 50);
                                upsertCmd.Parameters.Add("@priority", SqlDbType.NVarChar, 50);
                                upsertCmd.Parameters.Add("@status", SqlDbType.NVarChar, 50);
                                upsertCmd.Parameters.Add("@usercreate", SqlDbType.NVarChar, 100);
                                upsertCmd.Parameters.Add("@sectioncreate", SqlDbType.NVarChar, 100);
                                upsertCmd.Parameters.Add("@useraccept", SqlDbType.NVarChar, 100);
                                upsertCmd.Parameters.Add("@userfinish", SqlDbType.NVarChar, 100);
                                upsertCmd.Parameters.Add("@comment", SqlDbType.NVarChar, -1);
                                upsertCmd.Parameters.Add("@solution", SqlDbType.NVarChar, -1);
                                upsertCmd.Parameters.Add("@problem", SqlDbType.NVarChar, -1);
                                upsertCmd.Parameters.Add("@causes", SqlDbType.NVarChar, -1);
                                upsertCmd.Parameters.Add("@preventive", SqlDbType.NVarChar, -1);
                                upsertCmd.Parameters.Add("@ordertype", SqlDbType.NVarChar, 50);
                                upsertCmd.Parameters.Add("@submiss", SqlDbType.NVarChar, 50);
                                upsertCmd.Parameters.Add("@bdfac", SqlDbType.NVarChar, 50);

                                foreach (var job in syncedJobs)
                                {
                                    if (string.IsNullOrWhiteSpace(job.id))
                                        continue;

                                    // 2. แมปค่าจาก API Model ไปยัง Parameters (รองรับ Null ด้วย ?? DBNull.Value)
                                    upsertCmd.Parameters["@Id"].Value = job.id.Trim();
                                    upsertCmd.Parameters["@id_db"].Value = job.id.Trim();
                                    upsertCmd.Parameters["@refid"].Value = (object?)job.refId ?? DBNull.Value;
                                    upsertCmd.Parameters["@reforder"].Value = (object?)job.reforder ?? DBNull.Value;
                                    upsertCmd.Parameters["@MN"].Value = (object?)job.Mn ?? DBNull.Value;
                                    upsertCmd.Parameters["@MO"].Value = (object?)job.Mo ?? DBNull.Value;
                                    upsertCmd.Parameters["@detail"].Value = (object?)job.detail ?? DBNull.Value;
                                    upsertCmd.Parameters["@safety"].Value = (object?)job.safety ?? DBNull.Value;
                                    upsertCmd.Parameters["@code"].Value = (object?)job.code ?? DBNull.Value;
                                    upsertCmd.Parameters["@fl"].Value = (object?)job.fl ?? DBNull.Value;
                                    upsertCmd.Parameters["@downtime"].Value = (object?)job.downtime ?? DBNull.Value;
                                    upsertCmd.Parameters["@difftime"].Value = (object?)job.difftime ?? DBNull.Value;
                                    upsertCmd.Parameters["@timerepair"].Value = (object?)job.timerepair ?? DBNull.Value;
                                    upsertCmd.Parameters["@timerepair_ot"].Value = (object?)job.timerepair_ot ?? DBNull.Value;
                                    upsertCmd.Parameters["@tag_abnormal"].Value = (object?)job.tag_abnormal ?? DBNull.Value;
                                    upsertCmd.Parameters["@tag"].Value = (object?)job.tag ?? DBNull.Value;
                                    upsertCmd.Parameters["@tags"].Value = (object?)job.tags ?? DBNull.Value;
                                    upsertCmd.Parameters["@jobtype"].Value = (object?)job.jobtype ?? DBNull.Value;
                                    upsertCmd.Parameters["@section"].Value = (object?)(job.sectioncreate ?? job.section) ?? DBNull.Value;
                                    upsertCmd.Parameters["@productionstop"].Value = (object?)job.productionstop ?? DBNull.Value;
                                    upsertCmd.Parameters["@planner"].Value = (object?)job.planner ?? DBNull.Value;
                                    upsertCmd.Parameters["@statustext"].Value = (object?)job.statustext ?? DBNull.Value;
                                    upsertCmd.Parameters["@statussection"].Value = (object?)job.statussection ?? DBNull.Value;
                                    upsertCmd.Parameters["@opt_5s"].Value = (object?)job.opt_5s ?? DBNull.Value;
                                    upsertCmd.Parameters["@opt_5s_comment"].Value = (object?)job.opt_5s_comment ?? DBNull.Value;
                                    upsertCmd.Parameters["@opt_protect"].Value = (object?)job.opt_protect ?? DBNull.Value;
                                    upsertCmd.Parameters["@opt_protect_comment"].Value = (object?)job.opt_protect_comment ?? DBNull.Value;
                                    upsertCmd.Parameters["@rates"].Value = (object?)job.rates ?? DBNull.Value;

                                    // 3. แปลงวันที่ผ่านฟังก์ชัน ParseApiDate
                                    upsertCmd.Parameters["@timecreate"].Value = (object?)ParseApiDate(job.timecreate) ?? DBNull.Value;
                                    upsertCmd.Parameters["@timeassign"].Value = (object?)ParseApiDate(job.timeassign) ?? DBNull.Value;
                                    upsertCmd.Parameters["@timestart"].Value = (object?)ParseApiDate(job.timestart) ?? DBNull.Value;
                                    upsertCmd.Parameters["@timeend"].Value = (object?)ParseApiDate(job.timeend) ?? DBNull.Value;
                                    upsertCmd.Parameters["@timefinish"].Value = (object?)ParseApiDate(job.timefinish) ?? DBNull.Value;
                                    upsertCmd.Parameters["@timeaccept"].Value = (object?)ParseApiDate(job.timeaccept) ?? DBNull.Value;
                                    upsertCmd.Parameters["@timestartrepair"].Value = (object?)ParseApiDate(job.timestartrepair) ?? DBNull.Value;
                                    upsertCmd.Parameters["@timeendrepair"].Value = (object?)ParseApiDate(job.timeendrepair) ?? DBNull.Value;
                                    upsertCmd.Parameters["@timeclose"].Value = (object?)ParseApiDate(job.timeclose) ?? DBNull.Value;

                                    upsertCmd.Parameters["@timerunning"].Value = (object?)job.timerunning ?? DBNull.Value;
                                    upsertCmd.Parameters["@timeday"].Value = (object?)job.timeday ?? DBNull.Value;
                                    upsertCmd.Parameters["@fldetail"].Value = (object?)job.fldetail ?? DBNull.Value;
                                    upsertCmd.Parameters["@flrank"].Value = (object?)job.flrank ?? DBNull.Value;
                                    upsertCmd.Parameters["@flpngrp"].Value = (object?)job.flpngrp ?? DBNull.Value;
                                    upsertCmd.Parameters["@priority"].Value = (object?)job.priority ?? DBNull.Value;
                                    upsertCmd.Parameters["@status"].Value = (object?)job.status ?? DBNull.Value;
                                    upsertCmd.Parameters["@usercreate"].Value = (object?)job.usercreate ?? DBNull.Value;
                                    upsertCmd.Parameters["@sectioncreate"].Value = (object?)job.sectioncreate ?? DBNull.Value;
                                    upsertCmd.Parameters["@useraccept"].Value = (object?)job.useraccept ?? DBNull.Value;
                                    upsertCmd.Parameters["@userfinish"].Value = (object?)job.userfinish ?? DBNull.Value;
                                    upsertCmd.Parameters["@comment"].Value = (object?)job.comment ?? DBNull.Value;
                                    upsertCmd.Parameters["@solution"].Value = (object?)job.solution ?? DBNull.Value;
                                    upsertCmd.Parameters["@problem"].Value = (object?)job.problem ?? DBNull.Value;
                                    upsertCmd.Parameters["@causes"].Value = (object?)job.causes ?? DBNull.Value;
                                    upsertCmd.Parameters["@preventive"].Value = (object?)job.preventive ?? DBNull.Value;
                                    upsertCmd.Parameters["@ordertype"].Value = (object?)job.ordertype ?? DBNull.Value;
                                    upsertCmd.Parameters["@submiss"].Value = (object?)job.submiss ?? DBNull.Value;
                                    upsertCmd.Parameters["@bdfac"].Value = (object?)job.bdfac ?? DBNull.Value;

                                    await upsertCmd.ExecuteNonQueryAsync();

                                    // 👥 4. จัดการตารางลูก Job_assign (เหมือนเดิม)
                                    string deleteAssign = "DELETE FROM [dbo].[Job_assign] WHERE [job_id] = @JobId;";
                                    using (SqlCommand delAssignCmd = new SqlCommand(deleteAssign, localConn, transaction))
                                    {
                                        delAssignCmd.Parameters.AddWithValue("@JobId", job.id.Trim());
                                        await delAssignCmd.ExecuteNonQueryAsync();
                                    }

                                    var assigns = job.MapToJobAssigns();
                                    if (assigns != null && assigns.Count > 0)
                                    {
                                        string insertAssign = "INSERT INTO [dbo].[Job_assign] ([job_id], [user_assign]) VALUES (@JobId, @UserAssign);";
                                        foreach (var assign in assigns)
                                        {
                                            using SqlCommand insAssignCmd = new SqlCommand(insertAssign, localConn, transaction);
                                            insAssignCmd.Parameters.AddWithValue("@JobId", job.id.Trim());
                                            insAssignCmd.Parameters.AddWithValue("@UserAssign", assign.UserAssign);
                                            await insAssignCmd.ExecuteNonQueryAsync();
                                        }
                                    }

                                    // 🛡️ 5. จัดการตารางลูก Job_approve (เหมือนเดิม)
                                    string deleteApprove = "DELETE FROM [dbo].[Job_approve] WHERE [job_id] = @JobId;";
                                    using (SqlCommand delApproveCmd = new SqlCommand(deleteApprove, localConn, transaction))
                                    {
                                        delApproveCmd.Parameters.AddWithValue("@JobId", job.id.Trim());
                                        await delApproveCmd.ExecuteNonQueryAsync();
                                    }

                                    var approves = job.MapToJobApproves();
                                    if (approves != null && approves.Count > 0)
                                    {
                                        string insertApprove = @"INSERT INTO [dbo].[Job_approve] ([job_id], [approve_type], [user_approve], [approve_datetime]) 
                                                       VALUES (@JobId, @ApproveType, @UserApprove, @ApproveDatetime);";
                                        foreach (var approve in approves)
                                        {
                                            using SqlCommand insApproveCmd = new SqlCommand(insertApprove, localConn, transaction);
                                            insApproveCmd.Parameters.AddWithValue("@JobId", job.id.Trim());
                                            insApproveCmd.Parameters.AddWithValue("@ApproveType", approve.ApproveType);
                                            insApproveCmd.Parameters.AddWithValue("@UserApprove", approve.UserApprove);
                                            insApproveCmd.Parameters.AddWithValue("@ApproveDatetime", (object?)approve.ApproveDatetime ?? DBNull.Value);
                                            await insApproveCmd.ExecuteNonQueryAsync();
                                        }
                                    }

                                    affectedCount++;
                                }
                            }
                            await transaction.CommitAsync();
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
                    message = $"ดาต้าเกตเวย์อัปเดตเรียบร้อย! คัดลอกลงฐานข้อมูลภายใน {affectedCount} รายการ",
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

        private static DateTime? ParseApiDate(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return dt;

            string[] formats = { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd" };
            if (DateTime.TryParseExact(raw, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt2))
                return dt2;

            return null;
        }

        private async Task<List<MaintenanceApiModels>> FetchJobsFromApiInternal(
            string sessionUser, string sessionPass, string jobtype, string start, string end, string view = "followup")
        {
            var combined = new List<MaintenanceApiModels>();
            var culture = CultureInfo.InvariantCulture;

            if (!DateTime.TryParseExact(start, "yyyy-MM-dd", culture, DateTimeStyles.None, out var globalStart))
                if (!DateTime.TryParse(start, culture, DateTimeStyles.None, out globalStart)) globalStart = DateTime.Now.AddDays(-7);

            if (!DateTime.TryParseExact(end, "yyyy-MM-dd", culture, DateTimeStyles.None, out var globalEnd))
                if (!DateTime.TryParse(end, culture, DateTimeStyles.None, out globalEnd)) globalEnd = DateTime.Now;

            string finalStartStr = globalStart.ToString("yyyy-MM-dd", culture);
            string finalEndStr = globalEnd.ToString("yyyy-MM-dd", culture);
            var finalJobType = string.IsNullOrWhiteSpace(jobtype) ? "EM,CM" : jobtype;
            var viewsToCall = (view == "all") ? new List<string> { "followup", "myjob" } : new List<string> { view };

            var client = _httpClientFactory.CreateClient("ZycodaApiClient");

            foreach (var v in viewsToCall)
            {
                var queryParams = new Dictionary<string, string?>
                {
                    ["plant"] = "FARMHOUSE",
                    ["jobtype"] = finalJobType,
                    ["start"] = finalStartStr,
                    ["end"] = finalEndStr,
                    ["view"] = v
                };

                var url = QueryHelpers.AddQueryString("apimpros/get_job_order", queryParams);

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("username", sessionUser);
                request.Headers.Add("password", sessionPass);

                try
                {
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                    var json = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(json))
                    {
                        _logger.LogWarning($"⚠️ [SyncController] View: {v} -> Status: {response.StatusCode}");
                        continue;
                    }

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
                catch (Exception ex)
                {
                    _logger.LogError($"❌ [SyncController FetchJobsFromApiInternal] View: {v} -> {ex.Message}");
                }
            }

            return combined
                .Where(x => !string.IsNullOrWhiteSpace(x.id))
                .GroupBy(x => x.id)
                .Select(g => g.First())
                .ToList();
        }
    }
}