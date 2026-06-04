using report_zycoda.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace report_zycoda.Services
{
    public class JobSyncService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<JobSyncService> _logger;
        private readonly IConfiguration _configuration;

        // ดึง IConfiguration เข้ามาเพื่ออ่าน URL ของตัวระบบเอง (เช่น http://localhost:xxxx)
        public JobSyncService(IServiceProvider services, ILogger<JobSyncService> logger, IConfiguration configuration)
        {
            _services = services;
            _logger = logger;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🚀 [Auto Sync Worker] ระบบเริ่มต้นทำงาน Background Service สำเร็จ...");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("⏳ [Auto Sync Worker] กำลังเริ่มรอบการซิงค์ข้อมูลตามเวลารอบอัตโนมัติ...");

                    // 1. อ่านค่า Domain หรือ Base URL ของเซิร์ฟเวอร์ระบบเราเอง (กำหนดไว้ใน appsettings.json หรือใช้ดีฟอลต์)
                    string baseUrl = _configuration["AppUrl"] ?? "https://localhost:7197"; // ⚠️ เปลี่ยนให้ตรงกับ Port รันจริงของโปรเจกต์ฝ้ายน้า
                    string apiEndpoint = $"{baseUrl.TrimEnd('/')}/api/Sync/trigger-sync";

                    // 2. เรียกใช้งาน HttpClient ยิงไปกระตุ้น API ฝั่งคอนโทรลเลอร์
                    using (var client = new HttpClient())
                    {
                        client.Timeout = TimeSpan.FromMinutes(10); // ตั้งเวลาเผื่อดึงดาต้าจ็อบก้อนใหญ่

                        // จำลองยิงเป็น POST เปล่าๆ เข้าไป เพราะฝั่ง SyncController เราตั้งรับแบบ [HttpPost]
                        var response = await client.PostAsync(apiEndpoint, null, stoppingToken);

                        if (response.IsSuccessStatusCode)
                        {
                            string resultJson = await response.Content.ReadAsStringAsync();
                            _logger.LogInformation($"✅ [Auto Sync Worker] ซิงค์ดาต้าเสร็จสิ้น! บันทึกจากข้อความตอบกลับ: {resultJson}");
                        }
                        else
                        {
                            _logger.LogWarning($"⚠️ [Auto Sync Worker] ปลายสาย API ตอบกลับด้วยรหัสสเตตัสขัดข้อง: {response.StatusCode}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ [Auto Sync Worker] เกิดข้อผิดพลาดในระบบเกตเวย์ออโต้ซิงค์ข้อมูล");
                }

                // สั่งหน่วงเวลาการทำงานรอบถัดไปเป็นเวลา 30 นาที
                _logger.LogInformation("💤 [Auto Sync Worker] สิ้นสุดการทำงานรอบปัจจุบัน กำลังสแตนด์บายรอรอบถัดไปอีก 30 นาที...");
                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
            }
        }
    }
}