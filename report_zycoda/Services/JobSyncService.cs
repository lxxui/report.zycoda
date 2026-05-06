using report_zycoda.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration; // เพิ่มเพื่อให้มองเห็น IConfiguration
using System;
using System.Collections.Generic; // สำหรับ List
using System.Linq; // สำหรับ .Any()
using System.Net.Http; // สำหรับ HttpClient
using System.Threading;
using System.Threading.Tasks;
using report_zycoda.Data;
using Microsoft.AspNetCore.WebUtilities;
using Newtonsoft.Json;

namespace report_zycoda.Services
{
    public class JobSyncService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<JobSyncService> _logger;

        // ลบ IConfiguration หรือ ApiService ออกจาก Constructor ให้เหลือแค่นี้
        public JobSyncService(IServiceProvider services, ILogger<JobSyncService> logger)
        {
            _services = services;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // สร้าง Scope ทุกครั้งที่ทำงานเพื่อดึง Scoped Service (DbContext, ApiService)
                using (var scope = _services.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<myDbContext>();
                    // ถ้าจะใช้ ApiService ก็ดึงตรงนี้
                    // var apiService = scope.ServiceProvider.GetRequiredService<ApiService>();

                    try
                    {
                        _logger.LogInformation("Worker กำลังทำงาน...");
                        // ... Logic การดึง API และ Save ลง job_order ...
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Worker Error");
                    }
                }

                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
            }
        }
    }
}