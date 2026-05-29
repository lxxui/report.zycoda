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
    private readonly LatestSyncStore _syncStore;
    private readonly IHttpClientFactory _httpClientFactory; // 🔥 เปลี่ยนมาใช้ Factory ป้องกัน Socket เต็ม
    private readonly string _connectionString = "Server=CPU1921\\SQLEXPRESS01;Database=ZycodaApiByPB;Trusted_Connection=True;TrustServerCertificate=True;";

    // รับ IHttpClientFactory เพิ่มเข้ามาใน Constructor
    public MaintenanceWorker(ILogger<MaintenanceWorker> logger, LatestSyncStore syncStore, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _syncStore = syncStore;
        _httpClientFactory = httpClientFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 💡 แนะนำ: ปรับช่วงเวลาลดลงแทนการดึงทั้งปี เช่น ดึงย้อนหลัง 7 วัน หรือปรับตามความเหมาะสมของธุรกิจ
                string startDate = DateTime.Now.AddDays(-7).ToString("yyyy-MM-dd");
                string endDate = DateTime.Now.ToString("yyyy-MM-dd");

                _logger.LogInformation($"🔄 เริ่มดึงข้อมูลซ่อมบำรุง ช่วงวันที่ {startDate} ถึง {endDate}");

                var jobs = await FetchJobsFromApi("adminfarmhouse", "adminfarmhouse#241024", "EM,CM", startDate, endDate);

                if (jobs != null && jobs.Any())
                {
                    await UpsertToDatabase(jobs);
                    _syncStore.LastSyncTime = DateTime.Now;
                }
                else
                {
                    _logger.LogInformation("ℹ️ ไม่มีข้อมูลใหม่จาก API ในรอบนี้");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error ในรอบการทำงาน: {ex.Message}");
            }

            // รอ 5 นาทีค่อยทำรอบถัดไป
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    // 🌐 ฟังก์ชัน Fetch ดึงข้อมูลจาก API (ปรับปรุง HttpClient)
    private async Task<List<MaintenanceApiModels>> FetchJobsFromApi(string sessionUser, string sessionPass, string jobtype, string start, string end, string view = "followup")
    {
        var combined = new List<MaintenanceApiModels>();
        var viewsToCall = (view == "all") ? new List<string> { "followup", "myjob" } : new List<string> { view };

        // 🔥 สร้าง Client จาก Factory (จะช่วย Reuse พอร์ตและลดปัญหา Timeout ได้อย่างดี)
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(60); // ⏱️ เพิ่มเวลาเป็น 60 วินาที เผื่อ API ปลายทางตอบสนองช้า
        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("username", sessionUser);
        client.DefaultRequestHeaders.Add("password", sessionPass);

        foreach (var v in viewsToCall)
        {
            var url = $"https://farmhouse.zycoda.com/apimpros/get_job_order?plant=FARMHOUSE&jobtype={Uri.EscapeDataString(jobtype)}&start={start}&end={end}&view={v}";

            try
            {
                var res = await client.GetAsync(url);

                if (!res.IsSuccessStatusCode)
                {
                    _logger.LogWarning($"⚠️ {v} Fetch ล้มเหลวด้วย Status Code: {res.StatusCode}");
                    continue;
                }

                var json = await res.Content.ReadAsStringAsync();

                if (!string.IsNullOrWhiteSpace(json))
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

        string checkSql = "SELECT COUNT(1) FROM [ZycodaApiByPB].[dbo].[Job_order] WHERE [id] = @id;";

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
                [priority] = @priority, [status] = @status, 
                [usercreate] = @usercreate, [sectioncreate] = @sectioncreate, [useraccept] = @useraccept, [userfinish] = @userfinish, 
                [comment] = @comment, [solution] = @solution, [problem] = @problem, [causes] = @causes, 
                [preventive] = @preventive, [ordertype] = @ordertype, [submiss] = @submiss, [bdfac] = @bdfac
            WHERE [id] = @id;";

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
            var param = new
            {
                id = job.id ?? job.id_h,
                refid = job.refId,
                reforder = job.reforder,
                MN = job.Mn,
                MO = job.Mo,
                detail = job.detail ?? job.detail_h,
                safety = (string?)null,
                code = job.id,
                fl = job.fl,
                downtime = job.downtime,
                difftime = job.timedelay,
                timerepair = (string?)null,
                timerepair_ot = (string?)null,
                tag_abnormal = (string?)null,
                tag = (string?)null,
                tags = job.uassign != null ? string.Join(", ", job.uassign) : (string?)null,
                jobtype = job.jobtype,
                section = job.section,
                productionstop = (string?)null,
                planner = job.createby ?? job.workby,
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
                usercreate = job.UserDisplayName,
                sectioncreate = job.sectioncreate ?? job.SectionName,
                useraccept = job.useraccept,
                userfinish = job.userfinish,
                comment = job.view,
                solution = (string?)null,
                problem = (string?)null,
                causes = (string?)null,
                preventive = (string?)null,
                ordertype = (string?)null,
                submiss = (string?)null,
                bdfac = (string?)null
            };

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

        _logger.LogInformation($"💾 [SUCCESS] บันทึกข้อมูลเสร็จสิ้น -> เพิ่มใหม่: {insertCount} | อัปเดต: {updateCount}");
    }
}