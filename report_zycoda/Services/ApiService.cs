using System.Net.Http;
using Newtonsoft.Json;
using report_zycoda.Models;

public class ApiService
{
    private readonly IConfiguration _config;

    public ApiService(IConfiguration config)
    {
        _config = config;
    }

    // ปรับให้รับ parameters เพื่อเอาไปใช้สร้าง dynamic URL
    public async Task<List<MaintenanceReport>> GetJobs(string jobtype, string start, string end)
    {
        var username = _config["ApiSettings:Username"];
        var password = _config["ApiSettings:Password"];
        var baseUrl = _config["ApiSettings:BaseUrl"];

        // ตรวจสอบค่าว่าง (ถ้าไม่มีให้ใช้ค่า Default)
        jobtype = string.IsNullOrEmpty(jobtype) ? "EM,CM" : jobtype;
        start = string.IsNullOrEmpty(start) ? DateTime.Today.ToString("yyyy-MM-dd") : start;
        end = string.IsNullOrEmpty(end) ? DateTime.Today.ToString("yyyy-MM-dd") : end;

        // ตัวแปร jobtype, start, end จะมาจาก parameter ของฟังก์ชันแล้ว
        string url = $"{baseUrl}/get_job_order?plant=FARMHOUSE&jobtype={jobtype}&start={start}&end={end}";

        using (HttpClient client = new HttpClient())
        {
            client.DefaultRequestHeaders.Add("username", username);
            client.DefaultRequestHeaders.Add("password", password);

            var response = await client.GetAsync(url);
            var json = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"CALLING API: {url}"); // ช่วย Debug ดูว่า URL ที่ส่งไปหน้าตาเป็นยังไง

            return JsonConvert.DeserializeObject<List<MaintenanceReport>>(json)
                   ?? new List<MaintenanceReport>();
        }
    }
}