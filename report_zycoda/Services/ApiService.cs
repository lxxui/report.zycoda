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

public enum LoginStatus
{
    Success,
    InvalidCredentials,
    AccountLocked
}

public class LoginResult
{
    public LoginStatus Status { get; set; }
    public UserApiModels? User { get; set; }
    public int RemainingAttempts { get; set; }
    public DateTime? LockedAt { get; set; }
}

public class ApiService
{
    private readonly IConfiguration _config;
    private readonly HttpClient _httpClient;
    private readonly myDbContext _context;
    private readonly string _externalApiUrl;
    private readonly ILogger<ApiService> _logger;
    private readonly string _connectionString;

    // ✅ จำนวนครั้งที่ใส่รหัสผิดได้สูงสุดก่อนบัญชีถูกล็อค
    private const int MaxFailedAttempts = 3;

    public ApiService(IConfiguration config, HttpClient httpClient, myDbContext context, ILogger<ApiService> logger)
    {
        _config = config;
        _httpClient = httpClient;
        _context = context;
        _logger = logger;
        _externalApiUrl = _config["ApiSettings:BaseUrl"]?.TrimEnd('/') ?? "https://api.zycoda.com/apimpros";
        _connectionString = _config.GetConnectionString("DefaultConnection") ?? "";
    }

    // ✅ 1. Login: เช็ค/นับรหัสผิด + ล็อคบัญชีเมื่อผิดครบ 3 ครั้ง
    // ใช้คอลัมน์ FailedLoginCount / IsLocked / LockedAt ที่มีอยู่แล้วในตาราง
    // การปลดล็อค: ต้องให้แอดมิน/ไอทีกดปลดล็อคเองเท่านั้น (ไม่มีปลดอัตโนมัติตามเวลา)
    public async Task<LoginResult> LoginAsync(string username, string password)
    {
        var result = new LoginResult { Status = LoginStatus.InvalidCredentials };

        try
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                return result;

            string cleanUsername = username.Trim();
            string cleanPassword = password.Trim();

            _logger.LogInformation($"🔍 [ADO-Login] กำลังค้นหาข้อมูลล็อกอินสำหรับ: '{cleanUsername}'");

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            // ✅ Step 1: ดึงข้อมูลผู้ใช้ด้วย Username อย่างเดียวก่อน (ยังไม่เช็ครหัสผ่าน)
            string checkQuery = @"SELECT [Id], [Id_Zy], [Plant], [FirstName], [LastName], [Email],
                                         [Username], [Password], [Position], [Section], [Class], [Rule], [Active],
                                         [FailedLoginCount], [IsLocked], [LockedAt]
                                   FROM [ZycodaApiByPB].[dbo].[User]
                                   WHERE [Username] = @Username";

            UserApiModels? candidate = null;
            int failedCount = 0;
            bool isLocked = false;
            DateTime? lockedAt = null;
            string? storedPassword = null;

            using (var cmd = new SqlCommand(checkQuery, conn))
            {
                cmd.Parameters.AddWithValue("@Username", cleanUsername);
                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    candidate = new UserApiModels
                    {
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
                    storedPassword = candidate.Password;
                    failedCount = reader["FailedLoginCount"] == DBNull.Value ? 0 : Convert.ToInt32(reader["FailedLoginCount"]);
                    isLocked = reader["IsLocked"] != DBNull.Value && Convert.ToInt32(reader["IsLocked"]) == 1;
                    lockedAt = reader["LockedAt"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(reader["LockedAt"]);
                }
            }

            // ไม่พบ username เลย -> ตอบแบบ generic (กัน user enumeration)
            if (candidate == null)
            {
                _logger.LogWarning($"❌ [ADO-Login] ไม่พบ Username นี้ในระบบ: '{cleanUsername}'");
                result.Status = LoginStatus.InvalidCredentials;
                return result;
            }

            // ✅ Step 2: เช็คว่าบัญชีนี้ถูกล็อคอยู่หรือไม่ (ต้องรอแอดมินปลดล็อคเท่านั้น)
            if (isLocked)
            {
                _logger.LogWarning($"🔒 [ADO-Login] บัญชี '{cleanUsername}' ถูกล็อคอยู่ (ล็อคเมื่อ {lockedAt})");
                result.Status = LoginStatus.AccountLocked;
                result.LockedAt = lockedAt;
                return result;
            }

            // ✅ Step 3: ตรวจรหัสผ่าน
            if (storedPassword == cleanPassword)
            {
                // ล็อกอินสำเร็จ -> รีเซ็ตตัวนับกลับเป็น 0
                using var resetCmd = new SqlCommand(@"UPDATE [ZycodaApiByPB].[dbo].[User]
                                                        SET [FailedLoginCount] = 0
                                                        WHERE [Username] = @Username", conn);
                resetCmd.Parameters.AddWithValue("@Username", cleanUsername);
                await resetCmd.ExecuteNonQueryAsync();

                _logger.LogInformation($"✅ [ADO-Login] สำเร็จ! ยินดีต้อนรับคุณ {candidate.FirstName}");
                result.Status = LoginStatus.Success;
                result.User = candidate;
                return result;
            }
            else
            {
                // รหัสผิด -> เพิ่มตัวนับ
                failedCount++;

                if (failedCount >= MaxFailedAttempts)
                {
                    using var lockCmd = new SqlCommand(@"UPDATE [ZycodaApiByPB].[dbo].[User]
                                                          SET [FailedLoginCount] = @Count, [IsLocked] = 1, [LockedAt] = GETDATE()
                                                          WHERE [Username] = @Username", conn);
                    lockCmd.Parameters.AddWithValue("@Count", failedCount);
                    lockCmd.Parameters.AddWithValue("@Username", cleanUsername);
                    await lockCmd.ExecuteNonQueryAsync();

                    _logger.LogWarning($"🔒 [ADO-Login] '{cleanUsername}' ใส่รหัสผิดครบ {failedCount} ครั้ง -> บัญชีถูกล็อค (รอไอทีปลดล็อค)");

                    result.Status = LoginStatus.AccountLocked;
                    result.LockedAt = DateTime.Now;
                    return result;
                }
                else
                {
                    using var incCmd = new SqlCommand(@"UPDATE [ZycodaApiByPB].[dbo].[User]
                                                          SET [FailedLoginCount] = @Count
                                                          WHERE [Username] = @Username", conn);
                    incCmd.Parameters.AddWithValue("@Count", failedCount);
                    incCmd.Parameters.AddWithValue("@Username", cleanUsername);
                    await incCmd.ExecuteNonQueryAsync();

                    _logger.LogWarning($"❌ [ADO-Login] '{cleanUsername}' รหัสผิด (ครั้งที่ {failedCount}/{MaxFailedAttempts})");

                    result.Status = LoginStatus.InvalidCredentials;
                    result.RemainingAttempts = MaxFailedAttempts - failedCount;
                    return result;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"💥 [ADO-Login Exception] ระบบมีข้อผิดพลาด: {ex.Message}");
            result.Status = LoginStatus.InvalidCredentials;
            return result;
        }
    }

    // ✅ 1.1 ปลดล็อคบัญชีผู้ใช้ (สำหรับปุ่มในหน้า User Management ให้ไอทีกด)
    public async Task<bool> UnlockUserAsync(string username)
    {
        try
        {
            if (string.IsNullOrEmpty(username)) return false;
            string cleanUsername = username.Trim();

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            string query = @"UPDATE [ZycodaApiByPB].[dbo].[User]
                              SET [IsLocked] = 0, [FailedLoginCount] = 0, [LockedAt] = NULL
                              WHERE [Username] = @Username";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@Username", cleanUsername);

            int rowsAffected = await cmd.ExecuteNonQueryAsync();

            if (rowsAffected > 0)
            {
                _logger.LogInformation($"🔓 [ADO-Unlock] ปลดล็อคบัญชี '{cleanUsername}' สำเร็จ");
                return true;
            }

            _logger.LogWarning($"⚠️ [ADO-Unlock] ไม่พบ Username '{cleanUsername}' ให้ปลดล็อค");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"💥 [ADO-Unlock Exception] ปลดล็อคไม่สำเร็จ: {ex.Message}");
            return false;
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

            string query = @"SELECT [Id], [Id_Zy], [FirstName], [LastName], [Username], [Active] , [Class]
                             FROM [ZycodaApiByPB].[dbo].[User]
                             WHERE [Username] = @Username";

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
                    Class = reader["Class"]?.ToString(),
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
            return await _context.User
                .AsNoTracking()
                .Take(500)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError($"🚩 DB Error: {ex.Message}");
            return new List<UserApiModels>();
        }
    }

    internal async Task<List<SectionApiModels>> GetSectionsAsync()
    {
        return await GetSectionsFromApiAsync();
    }
}