using Newtonsoft.Json;
using report_zycoda.Models;
using System.Net.Http.Json;

public class ApiService
{
    private readonly IConfiguration _config;
    private readonly HttpClient _httpClient;

    public ApiService(IConfiguration config, HttpClient httpClient)
    {
        _config = config;
        _httpClient = httpClient;
    }

    // ✅ ดึงข้อมูลงาน (Maintenance Jobs)
    public async Task<List<MaintenanceApiModels>> GetJobs(string jobtype, string start, string end, string? user = null, string? pwd = null)
    {
        var baseUrl = _config["ApiSettings:BaseUrl"];
        var apiUsername = user ?? _config["ApiSettings:Username"];
        var apiPassword = pwd ?? _config["ApiSettings:Password"];

        jobtype = string.IsNullOrEmpty(jobtype) ? "EM,CM" : jobtype;
        start = string.IsNullOrEmpty(start) ? DateTime.Today.ToString("yyyy-MM-dd") : start;
        end = string.IsNullOrEmpty(end) ? DateTime.Today.ToString("yyyy-MM-dd") : end;

        string url = $"{baseUrl}/get_job_order?plant=FARMHOUSE&jobtype={jobtype}&start={start}&end={end}";

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("username", apiUsername);
        _httpClient.DefaultRequestHeaders.Add("password", apiPassword);

        var response = await _httpClient.GetAsync(url);
        var json = await response.Content.ReadAsStringAsync();

        return JsonConvert.DeserializeObject<List<MaintenanceApiModels>>(json) ?? new List<MaintenanceApiModels>();
    }

    // ✅ ฟังก์ชัน Login โดยการตรวจสอบจากรายชื่อพนักงาน
    public async Task<UserApiModels?> LoginAsync(string username, string password)
    {
        try
        {
            // 🚩 อ่านจากไฟล์ที่เรา Manual ไว้ในเครื่อง
            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "data", "users.json");
            var json = await System.IO.File.ReadAllTextAsync(filePath);

            var users = JsonConvert.DeserializeObject<List<UserApiModels>>(json);

            if (users != null)
            {
                return users.FirstOrDefault(u =>
                    u.username == username &&
                    u.password == password &&
                    u.active == "on");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Error reading users.json: " + ex.Message);
        }

        return null;
    }
}