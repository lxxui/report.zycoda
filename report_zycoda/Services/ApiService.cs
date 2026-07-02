using report_zycoda.Data;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using report_zycoda.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;

public class ApiService
{
    private readonly IConfiguration _config;
    private readonly HttpClient _httpClient;
    private readonly myDbContext _context;
    private readonly string _externalApiUrl;
    private readonly ILogger<ApiService> _logger;
    private readonly string _connectionString;

    public ApiService(IConfiguration config, HttpClient httpClient, myDbContext context, ILogger<ApiService> logger)
    {
        _config = config;
        _httpClient = httpClient;
        _context = context;
        _logger = logger;
        _externalApiUrl = _config["ApiSettings:BaseUrl"]?.TrimEnd('/') ?? "https://api.zycoda.com/apimpros";
        _connectionString = _config.GetConnectionString("DefaultConnection") ?? "";
    }

    // ✅ 1. Login: ปรับปรุง SQL ปลดล็อก Index และป้องกัน DBNull
    public async Task<UserApiModels?> LoginAsync(string username, string password)
    {
        try
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)) return null;

            string cleanUsername = username.Trim();
            string cleanPassword = password.Trim();

            _logger.LogInformation($"🔍 [ADO-Login] กำลังค้นหาข้อมูลล็อกอินสำหรับ: '{cleanUsername}'");

            // ถอด LTRIM/RTRIM ออกจากคอลัมน์ฝั่ง SQL เพื่อให้ทำงานเร็วสายฟ้าแลบ (ผ่าน Index)
            string query = @"SELECT [Id], [Id_Zy], [Plant], [FirstName], [LastName], [Email], 
                                    [Username], [Password], [Position], [Section], [Class], [Rule], [Active]
                             FROM [ZycodaApiByPB].[dbo].[User]
                             WHERE [Username] = @Username 
                               AND [Password] = @Password";

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@Username", cleanUsername);
            cmd.Parameters.AddWithValue("@Password", cleanPassword);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var user = new UserApiModels
                {
                    // ป้องกันโปรแกรมพังหากเกิดค่า Null ในฐานข้อมูล
                    Id = reader["Id"] == DBNull.Value ? 0 : Convert.ToInt32(reader["Id"]),
                    Id_Zy = reader["Id_Zy"]?.ToString(),
                    Plant = reader["Plant"]?.ToString(),
                    FirstName = reader["FirstName"]?.ToString(),
                    LastName = reader["LastName"]?.ToString(),
                    Email = reader["Email"]?.ToString(),
                    Username = reader["Username"]?.ToString(),
                    Password = reader["Password"]?.ToString(),
                    Position = reader["Position"]?.ToString(),
                    Section = reader["Section"]?.ToString(),
                    Class = reader["Class"]?.ToString(),
                    Rule = reader["Rule"]?.ToString(),
                    Active = reader["Active"] == DBNull.Value ? 0 : Convert.ToInt32(reader["Active"])
                };

                _logger.LogInformation($"✅ [ADO-Login] สำเร็จ! ยินดีต้อนรับคุณ {user.FirstName}");
                return user;
            }

            _logger.LogWarning($"❌ [ADO-Login] ไม่พบคู่ Username/Password นี้ในระบบ: '{cleanUsername}'");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError($"💥 [ADO-Login Exception] ระบบมีข้อผิดพลาด: {ex.Message}");
            return null;
        }
    }

    // ✅ 2. ค้นหาผู้ใช้งานด้วย Username (ปลดล็อกความเร็ว SQL)
    public async Task<UserApiModels?> GetUsersFromApiAsync(string username)
    {
        try
        {
            if (string.IsNullOrEmpty(username)) return null;
            string cleanUsername = username.Trim();

            _logger.LogInformation($"🔍 [ADO-GetUsers] กำลังค้นหาชื่อ: '{cleanUsername}'");

            string query = @"SELECT [Id], [Id_Zy], [FirstName], [LastName], [Username], [Active]
                             FROM [ZycodaApiByPB].[dbo].[User]
                             WHERE [Username] = @Username"; // 👈 ถอดฟังก์ชันครอบออกเพื่อให้ใช้ Index ได้

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@Username", cleanUsername);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                _logger.LogInformation($"✅ [ADO-GetUsers] เจอ Username ในฐานข้อมูลหลักแล้ว!");
                return new UserApiModels
                {
                    Id = reader["Id"] == DBNull.Value ? 0 : Convert.ToInt32(reader["Id"]),
                    Id_Zy = reader["Id_Zy"]?.ToString(),
                    FirstName = reader["FirstName"]?.ToString(),
                    LastName = reader["LastName"]?.ToString(),
                    Username = reader["Username"]?.ToString(),
                    Active = reader["Active"] == DBNull.Value ? 0 : Convert.ToInt32(reader["Active"])
                };
            }

            _logger.LogWarning($"❌ [ADO-GetUsers] ไม่พบไอดี '{cleanUsername}' ในเบสหลัก");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError($"💥 [ADO-GetUsers Exception] พังตอนหาชื่อ: {ex.Message}");
            return null;
        }
    }

    // ✅ 3. ดึงข้อมูลแผนก
    public async Task<List<SectionApiModels>> GetSectionsFromApiAsync()
    {
        try
        {
            return await _context.Section
                .AsNoTracking()
                .OrderBy(s => s.Sections)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError($"🚩 DB Section Error: {ex.Message}");
            return new List<SectionApiModels>();
        }
    }

    // ✅ 4. ดึงสถานะ
    public async Task<List<StatusApiModels>> GetStatusesFromApiAsync()
    {
        try
        {
            return await _context.Statuses.AsNoTracking().ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError($"🚩 DB Status Error: {ex.Message}");
            return new List<StatusApiModels>();
        }
    }

    // ✅ 5. ดึงรายชื่อพนักงาน (แนะนำให้ใส่ .Take() จำกัดจำนวนไว้ก่อนหากข้อมูลเยอะ)
    public async Task<List<UserApiModels>> GetUsersFromApiAsync()
    {
        try
        {
            // แนะนำ: หากข้อมูลเยอะมาก ให้เปลี่ยนเป็น .Take(100).ToListAsync() เพื่อทดสอบว่าหายค้างไหม
            return await _context.User
                .AsNoTracking()
                .Take(500) // 👈 ป้องกันข้อมูลล้นทะลักเบราว์เซอร์จนหมุนค้าง
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError($"🚩 DB Error: {ex.Message}");
            return new List<UserApiModels>();
        }
    }

    // แก้ไขโครงสร้างส่งกลับเพื่อให้สอดคล้องกับชื่อด้านบน
    internal async Task<List<SectionApiModels>> GetSectionsAsync()
    {
        return await GetSectionsFromApiAsync();
    }
}