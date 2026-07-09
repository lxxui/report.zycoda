using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System;
using System.Data;
using Microsoft.Data.SqlClient; // หรือใช้ System.Data.SqlClient ตามที่โปรเจกต์ใช้อยู่ค่ะ
using System.Threading;
using System.Threading.Tasks;

namespace YourProjectName.Services // <-- อย่าลืมเปลี่ยนชื่อ Namespace ให้ตรงกับโปรเจกต์นะคะ
{
    public class ZycodaSyncBackgroundService : BackgroundService
    {
        private readonly ILogger<ZycodaSyncBackgroundService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _period = TimeSpan.FromMinutes(5); // ตั้งเวลาทำงานอัตโนมัติทุกๆ 5 นาที

        public ZycodaSyncBackgroundService(ILogger<ZycodaSyncBackgroundService> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Zycoda Sync Background Service is starting.");

            using PeriodicTimer timer = new PeriodicTimer(_period);

            // วนลูปทำงานไปเรื่อยๆ จนกว่าแอปพลิเคชันจะหยุดทำงาน
            while (await timer.WaitForNextTickAsync(stoppingToken) && !stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Starting sync job at: {time}", DateTimeOffset.Now);

                    using (var scope = _serviceProvider.CreateScope())
                    {
                        // ดึง IConfiguration มาจาก DI Container เพื่ออ่านค่า Connection String
                        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

                        string zycodaApiConnStr = configuration.GetConnectionString("ZycodaApiConnectionString");
                        string localConnStr = configuration.GetConnectionString("DefaultConnection"); // หรือชื่อตาม appsettings.json ของคุณฝ้ายค่ะ

                        // 1. ดึงข้อมูลจาก Gateway API คัดเฉพาะรายการที่ refid ไม่เป็นค่าว่าง
                        DataTable dtSourceJobs = new DataTable();
                        string selectQuery = @"
                            SELECT [id], [refid], [reforder], [MN], [MO], [detail], [safety], [code], [fl], [downtime], 
                                   [difftime], [timerepair], [timerepair_ot], [tag_abnormal], [tag], [tags], [jobtype], 
                                   [section], [productionstop], [planner], [statustext], [statussection], [opt_5s], 
                                   [opt_5s_comment], [opt_protect], [opt_protect_comment], [rates], [timecreate], 
                                   [timeassign], [timestart], [timeend], [timefinish], [timeaccept], [timestartrepair], 
                                   [timeendrepair], [timeclose], [timerunning], [timeday], [fldetail], [flrank], 
                                   [flpngrp], [priority], [status], [usercreate], [sectioncreate], [useraccept], 
                                   [userfinish], [comment], [solution], [problem], [causes], [preventive], 
                                   [ordertype], [submiss], [bdfac], [id_db]
                            FROM [ZycodaApiByPB].[dbo].[Job_order]
                            WHERE [refid] IS NOT NULL AND [refid] <> ''";

                        using (SqlConnection apiConn = new SqlConnection(zycodaApiConnStr))
                        {
                            using (SqlCommand cmd = new SqlCommand(selectQuery, apiConn))
                            {
                                using (SqlDataAdapter da = new SqlDataAdapter(cmd))
                                {
                                    await apiConn.OpenAsync(stoppingToken);
                                    da.Fill(dtSourceJobs);
                                }
                            }
                        }

                        _logger.LogInformation($"Fetched {dtSourceJobs.Rows.Count} jobs from Zycoda API Gateway.");

                        // 2. นำข้อมูลมาวนลูปเพื่อทำ Upsert ลง Database Local ของระบบเรา
                        if (dtSourceJobs.Rows.Count > 0)
                        {
                            string upsertQuery = @"
                                IF EXISTS (SELECT 1 FROM [dbo].[Job_order] WHERE [refid] = @refid)
                                BEGIN
                                    UPDATE [dbo].[Job_order]
                                    SET [id_db] = @id_db, [reforder] = @reforder, [MN] = @MN, [MO] = @MO, [detail] = @detail, 
                                        [safety] = @safety, [code] = @code, [fl] = @fl, [downtime] = @downtime, [difftime] = @difftime, 
                                        [timerepair] = @timerepair, [timerepair_ot] = @timerepair_ot, [tag_abnormal] = @tag_abnormal, 
                                        [tag] = @tag, [tags] = @tags, [jobtype] = @jobtype, [section] = @section, 
                                        [productionstop] = @productionstop, [planner] = @planner, [statustext] = @statustext, 
                                        [statussection] = @statussection, [opt_5s] = @opt_5s, [opt_5s_comment] = @opt_5s_comment, 
                                        [opt_protect] = @opt_protect, [opt_protect_comment] = @opt_protect_comment, [rates] = @rates, 
                                        [timecreate] = @timecreate, [timeassign] = @timeassign, [timestart] = @timestart, 
                                        [timeend] = @timeend, [timefinish] = @timefinish, [timeaccept] = @timeaccept, 
                                        [timestartrepair] = @timestartrepair, [timeendrepair] = @timeendrepair, [timeclose] = @timeclose, 
                                        [timerunning] = @timerunning, [timeday] = @timeday, [fldetail] = @fldetail, [flrank] = @flrank, 
                                        [flpngrp] = @flpngrp, [priority] = @priority, [status] = @status, [usercreate] = @usercreate, 
                                        [sectioncreate] = @sectioncreate, [useraccept] = @useraccept, [userfinish] = @userfinish, 
                                        [comment] = @comment, [solution] = @solution, [problem] = @problem, [causes] = @causes, 
                                        [preventive] = @preventive, [ordertype] = @ordertype, [submiss] = @submiss, [bdfac] = @bdfac,
                                        [LastSyncDate] = GETDATE()
                                    WHERE [refid] = @refid
                                END
                                ELSE
                                BEGIN
                                    INSERT INTO [dbo].[Job_order] 
                                        ([refid], [id_db], [reforder], [MN], [MO], [detail], [safety], [code], [fl], [downtime], 
                                         [difftime], [timerepair], [timerepair_ot], [tag_abnormal], [tag], [tags], [jobtype], 
                                         [section], [productionstop], [planner], [statustext], [statussection], [opt_5s], 
                                         [opt_5s_comment], [opt_protect], [opt_protect_comment], [rates], [timecreate], 
                                         [timeassign], [timestart], [timeend], [timefinish], [timeaccept], [timestartrepair], 
                                         [timeendrepair], [timeclose], [timerunning], [timeday], [fldetail], [flrank], 
                                         [flpngrp], [priority], [status], [usercreate], [sectioncreate], [useraccept], 
                                         [userfinish], [comment], [solution], [problem], [causes], [preventive], 
                                         [ordertype], [submiss], [bdfac], [LastSyncDate])
                                    VALUES 
                                        (@refid, @id_db, @reforder, @MN, @MO, @detail, @safety, @code, @fl, @downtime, 
                                         @difftime, @timerepair, @timerepair_ot, @tag_abnormal, @tag, @tags, @jobtype, 
                                         @section, @productionstop, @planner, @statustext, @statussection, @opt_5s, 
                                         @opt_5s_comment, @opt_protect, @opt_protect_comment, @rates, @timecreate, 
                                         @timeassign, @timestart, @timeend, @timefinish, @timeaccept, @timestartrepair, 
                                         @timeendrepair, @timeclose, @timerunning, @timeday, @fldetail, @flrank, 
                                         @flpngrp, @priority, @status, @usercreate, @sectioncreate, @useraccept, 
                                         @userfinish, @comment, @solution, @problem, @causes, @preventive, 
                                         @ordertype, @submiss, @bdfac, GETDATE())
                                END";

                            using (SqlConnection localConn = new SqlConnection(localConnStr))
                            {
                                await localConn.OpenAsync(stoppingToken);

                                foreach (DataRow row in dtSourceJobs.Rows)
                                {
                                    using (SqlCommand cmd = new SqlCommand(upsertQuery, localConn))
                                    {
                                        // นำค่าดั้งเดิมจากคอลัมน์ [id] บนเกตเวย์ ไปเก็บในคอลัมน์ [id_db] ของฝั่ง Local
                                        cmd.Parameters.AddWithValue("@id_db", row["id"] ?? DBNull.Value);

                                        // แฟลตพารามิเตอร์ของฟิลด์อื่นๆ ที่เหลือลงไปในคิวรี
                                        cmd.Parameters.AddWithValue("@refid", row["refid"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@reforder", row["reforder"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@MN", row["MN"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@MO", row["MO"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@detail", row["detail"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@safety", row["safety"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@code", row["code"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@fl", row["fl"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@downtime", row["downtime"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@difftime", row["difftime"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@timerepair", row["timerepair"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@timerepair_ot", row["timerepair_ot"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@tag_abnormal", row["tag_abnormal"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@tag", row["tag"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@tags", row["tags"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@jobtype", row["jobtype"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@section", row["section"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@productionstop", row["productionstop"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@planner", row["planner"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@statustext", row["statustext"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@statussection", row["statussection"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@opt_5s", row["opt_5s"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@opt_5s_comment", row["opt_5s_comment"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@opt_protect", row["opt_protect"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@opt_protect_comment", row["opt_protect_comment"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@rates", row["rates"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@timecreate", row["timecreate"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@timeassign", row["timeassign"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@timestart", row["timestart"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@timeend", row["timeend"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@timefinish", row["timefinish"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@timeaccept", row["timeaccept"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@timestartrepair", row["timestartrepair"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@timeendrepair", row["timeendrepair"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@timeclose", row["timeclose"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@timerunning", row["timerunning"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@timeday", row["timeday"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@fldetail", row["fldetail"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@flrank", row["flrank"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@flpngrp", row["flpngrp"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@priority", row["priority"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@status", row["status"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@usercreate", row["usercreate"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@sectioncreate", row["sectioncreate"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@useraccept", row["useraccept"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@userfinish", row["userfinish"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@comment", row["comment"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@solution", row["solution"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@problem", row["problem"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@causes", row["causes"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@preventive", row["preventive"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@ordertype", row["ordertype"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@submiss", row["submiss"] ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@bdfac", row["bdfac"] ?? DBNull.Value);

                                        await cmd.ExecuteNonQueryAsync(stoppingToken);
                                    }
                                }
                            }
                        }

                        _logger.LogInformation("Sync job completed successfully.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred executing sync job.");
                }
            }
        }
    }
}