using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using report_zycoda.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
public class MaintenanceWorker : BackgroundService
{
    private readonly ILogger<MaintenanceWorker> _logger;
    private readonly LatestSyncStore _syncStore; // 🔥 เพิ่มตัวนี้เข้ามาครับ
    private readonly string _connectionString = "Server=CPU1921\\SQLEXPRESS01;Database=ZycodaApiByPB;Trusted_Connection=True;TrustServerCertificate=True;";

    // รับค่าผ่าน Constructor
    public MaintenanceWorker(ILogger<MaintenanceWorker> logger, LatestSyncStore syncStore)
    {
        _logger = logger;
        _syncStore = syncStore;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var jobs = await FetchJobsFromApi("adminfarmhouse", "adminfarmhouse#241024", "EM,CM", "2026-01-01", "2026-12-31");

                if (jobs != null && jobs.Any())
                {
                    await UpsertToDatabase(jobs);

                    // 🔥 บันทึกเวลาความสำเร็จลงใน Store ส่วนกลางหลังบันทึก DB เสร็จ
                    _syncStore.LastSyncTime = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); // ดึงทุก 5 นาทีตามที่คุณฝ้ายตั้งไว้
        }
    }

    // 🌐 ฟังก์ชัน Fetch ดึงข้อมูลจาก API
    private async Task<List<MaintenanceApiModels>> FetchJobsFromApi(string sessionUser, string sessionPass, string jobtype, string start, string end, string view = "followup")
    {
        var combined = new List<MaintenanceApiModels>();
        var viewsToCall = (view == "all") ? new List<string> { "followup", "myjob" } : new List<string> { view };

        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("username", sessionUser);
        client.DefaultRequestHeaders.Add("password", sessionPass);

        foreach (var v in viewsToCall)
        {
            var url = $"https://farmhouse.zycoda.com/apimpros/get_job_order?plant=FARMHOUSE&jobtype={Uri.EscapeDataString(jobtype)}&start={start}&end={end}&view={v}";

            try
            {
                var res = await client.GetAsync(url);
                var json = await res.Content.ReadAsStringAsync();

                if (res.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(json))
                {
                    if (json.Trim().StartsWith("{")) json = "[" + json + "]";

                    var data = JsonConvert.DeserializeObject<List<MaintenanceApiModels>>(json);
                    if (data != null)
                    {
                        combined.AddRange(data);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ {v} Fetch Exception: {ex.Message}");
            }
        }
        return combined;
    }

    private async Task UpsertToDatabase(List<MaintenanceApiModels> jobs)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        int updateCount = 0;
        int insertCount = 0;

        // 🔍 1. เช็คซ้ำจากคอลัมน์ [id] (รหัสจาก API เช่น "0M001204")
        string checkSql = "SELECT COUNT(1) FROM [ZycodaApiByPB].[dbo].[Job_order] WHERE [id] = @id;";

        // 📝 2. คำสั่ง UPDATE ครบทุกคอลัมน์ (ยกเว้น id และ id_db)
        string updateSql = @"
            UPDATE [ZycodaApiByPB].[dbo].[Job_order]
            SET [refid] = @refid, [reforder] = @reforder, [MN] = @MN, [MO] = @MO, [detail] = @detail, 
                [safety] = @safety, [code] = @code, [fl] = @fl, [downtime] = @downtime, [difftime] = @difftime, 
                [timerepair] = @timerepair, [timerepair_ot] = @timerepair_ot, [tag_abnormal] = @tag_abnormal, 
                [tag] = @tag, [tags] = @tags, [jobtype] = @jobtype, [section] = @section, 
                [productionstop] = @productionstop, [planner] = @planner, [statustext] = @statustext, 
                [statussection] = @statussection, [opt_5s] = @opt_5s, [opt_5s_comment] = @opt_5s_comment, 
                [opt_protect] = @opt_protect, [opt_protect_comment] = @opt_protect_comment, [rates] = @rates, 
                [timecreate] = @timecreate, [timeassign] = @timeassign, [timestart] = @timestart, [timeend] = @timeend, 
                [timefinish] = @timefinish, [timeaccept] = @timeaccept, [timestartrepair] = @timestartrepair, 
                [timeendrepair] = @timeendrepair, [timeclose] = @timeclose, [timerunning] = @timerunning, 
                [timeday] = @timeday, [fldetail] = @fldetail, [flrank] = @flrank, [flpngrp] = @flpngrp, 
                [priority] = @priority, [status] = @status, [usercreate] = @usercreate, 
                [sectioncreate] = @sectioncreate, [useraccept] = @useraccept, [userfinish] = @userfinish, 
                [comment] = @comment, [solution] = @solution, [problem] = @problem, [causes] = @causes, 
                [preventive] = @preventive, [ordertype] = @ordertype, [submiss] = @submiss, [bdfac] = @bdfac
            WHERE [id] = @id;";

        // 📥 3. คำสั่ง INSERT ครบทุกคอลัมน์ (ปล่อย id_db ที่อยู่ท้ายตารางให้รัน Auto เองเงียบ ๆ)
        string insertSql = @"
            INSERT INTO [ZycodaApiByPB].[dbo].[Job_order] 
                ([id], [refid], [reforder], [MN], [MO], [detail], [safety], [code], [fl], [downtime], 
                 [difftime], [timerepair], [timerepair_ot], [tag_abnormal], [tag], [tags], [jobtype], [section], 
                 [productionstop], [planner], [statustext], [statussection], [opt_5s], [opt_5s_comment], 
                 [opt_protect], [opt_protect_comment], [rates], [timecreate], [timeassign], [timestart], 
                 [timeend], [timefinish], [timeaccept], [timestartrepair], [timeendrepair], [timeclose], 
                 [timerunning], [timeday], [fldetail], [flrank], [flpngrp], [priority], [status], 
                 [usercreate], [sectioncreate], [useraccept], [userfinish], [comment], [solution], 
                 [problem], [causes], [preventive], [ordertype], [submiss], [bdfac])
            VALUES 
                (@id, @refid, @reforder, @MN, @MO, @detail, @safety, @code, @fl, @downtime, 
                 @difftime, @timerepair, @timerepair_ot, @tag_abnormal, @tag, @tags, @jobtype, @section, 
                 @productionstop, @planner, @statustext, @statussection, @opt_5s, @opt_5s_comment, 
                 @opt_protect, @opt_protect_comment, @rates, @timecreate, @timeassign, @timestart, 
                 @timeend, @timefinish, @timeaccept, @timestartrepair, @timeendrepair, @timeclose, 
                 @timerunning, @timeday, @fldetail, @flrank, @flpngrp, @priority, @status, 
                 @usercreate, @sectioncreate, @useraccept, @userfinish, @comment, @solution, 
                 @problem, @causes, @preventive, @ordertype, @submiss, @bdfac);";

        foreach (var job in jobs)
        {
            // 💡 4. แมปค่าและแก้ไข Logic ตัวแปรที่ซับซ้อนให้ลงล็อกกับ SQL Server
            var param = new
            {
                id = job.id ?? job.id_h,                           // ถ้าระบบใหม่ไม่มี ให้ถอยไปใช้รหัสระบบเดิม
                refid = job.refId,
                reforder = job.reforder,
                MN = job.Mn,
                MO = job.Mo,
                detail = job.detail ?? job.detail_h,               // ถ้าระบบใหม่ไม่มี ให้ถอยไปใช้รายละเอียดระบบเดิม
                safety = (string?)null,                            // ฟิล์ดเผื่อไว้ รองรับค่า Null ป้องกันโครงสร้างพัง
                code = job.job_no,                                 // แมปรหัสบิล (job_no) เข้าช่อง code
                fl = job.fl,
                downtime = job.downtime,
                difftime = job.timedelay,                          // ใช้ระยะเวลาดีเลย์ซ่อมบำรุง
                timerepair = (string?)null,
                timerepair_ot = (string?)null,
                tag_abnormal = (string?)null,
                tag = (string?)null,
                // แปลง List<string> ของช่างรับผิดชอบให้กลายเป็น String คั่นด้วยคอมม่าลง DB ป้องกันบ่นเรื่อง Type mismatch
                tags = job.uassign != null ? string.Join(", ", job.uassign) : (string?)null,
                jobtype = job.jobtype,
                section = job.section,
                productionstop = (string?)null,
                planner = job.createby ?? job.workby,              // แมปตัวคนวางแผน/คนสร้างบิล
                statustext = job.statustext,
                statussection = (string?)null,
                opt_5s = (string?)null,
                opt_5s_comment = (string?)null,
                opt_protect = (string?)null,
                opt_protect_comment = (string?)null,
                rates = (string?)null,
                timecreate = job.timecreate,
                timeassign = job.timeassign,
                timestart = job.timestart,
                timeend = job.timeend,
                timefinish = job.timefinish,
                timeaccept = job.timeaccept,
                timestartrepair = job.timestartrepair,
                timeendrepair = job.timeendrepair,
                timeclose = job.timeclose,
                timerunning = (string?)null,
                timeday = (string?)null,
                fldetail = job.fldetail,
                flrank = (string?)null,
                flpngrp = (string?)null,
                priority = job.priority,
                status = job.status,
                // 🔥 เรียกใช้ Getter Property (UserDisplayName) ที่คุณฝ้ายแกะชื่อคนแจ้งงานไว้แล้ว มั่นใจได้ว่าข้อมูลไม่พังชัวร์
                usercreate = job.UserDisplayName,
                sectioncreate = job.sectioncreate ?? job.SectionName,
                useraccept = job.useraccept,
                userfinish = job.userfinish,
                comment = job.view,                                // แฝง Comment หรือ View สรุปย่อยลงไป
                solution = (string?)null,
                problem = (string?)null,
                causes = (string?)null,
                preventive = (string?)null,
                ordertype = (string?)null,
                submiss = (string?)null,
                bdfac = (string?)null
            };

            // 🛠️ ตรวจสอบความซ้ำซ้อนผ่านตัวแปร id หลัก
            var isExist = await connection.ExecuteScalarAsync<int>(checkSql, new { id = param.id });

            if (isExist > 0)
            {
                await connection.ExecuteAsync(updateSql, param);
                updateCount++;
            }
            else
            {
                await connection.ExecuteAsync(insertSql, param);
                insertCount++;
            }
        }

        _logger.LogInformation($"💾 [SUCCESS] ดึงและบันทึกข้อมูลเรียบร้อย -> เพิ่มงานใหม่: {insertCount} รายการ | อัปเดตงานซ้ำ: {updateCount} รายการ");
    }
}