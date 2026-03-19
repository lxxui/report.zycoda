using Microsoft.AspNetCore.Mvc;

namespace report_zycoda.Models
{
    // Class หลัก
    public class MaintenanceApiModels
    {
        // --- ส่วนที่ 1: ของเดิม (รักษาไว้) ---
        public string? id_h { get; set; }
        public string? detail_h { get; set; }
        public int downtime_h { get; set; }
        public string? timecreate { get; set; }
        public string? tag_abnormal { get; set; }
        public string? solution { get; set; }
        public string? problem { get; set; }
        public string? causes { get; set; }
        public string? ordertype { get; set; }
        public string? sectioncreate { get; set; }
        public string? status { get; set; }
        public int? timerepair { get; set; }
        public string? usercreate { get; set; } // แบบ string เดิม

        // --- ส่วนที่ 2: ของใหม่ (รองรับ JSON farmhouse) ---
        public string? id { get; set; }
        public string? detail { get; set; }
        public string? fl { get; set; }
        public string? fldetail { get; set; }
        public double? downtime { get; set; }
        public string? jobtype { get; set; }
        public string? section { get; set; }
        public string? statustext { get; set; }

        // --- ส่วนที่ 3: แบบ Object (แก้ Error CS0246) ---
        public StatusDetail? st { get; set; }
        public UserCreateDetail? usercreate_obj { get; set; }
        public TimeDetail? time { get; set; }

        // --- ส่วนที่ 4: ฟิลด์ที่ใช้ใน Controller (แก้ Error CS1061) ---
        public string? acceptby { get; set; } // เพิ่มตัวนี้เพื่อให้ Controller เรียกใช้ได้
        public bool IsAccept { get; set; }
        public bool IsAssign { get; set; }

        // --- ส่วนที่ 5: Logic สีสถานะ ---
        public string StatusCssClass
        {
            get
            {
                string currentStatus = (statustext ?? status ?? "").Trim();
                return currentStatus switch
                {
                    "Create" => "text-danger",
                    "Pending" => "text-info",
                    "Accept" => "text-warning",
                    "Assigned" => "text-primary",
                    "Finish" => "text-success",
                    "Confirm" => "text-success",
                    _ => "text-secondary"
                };
            }
        }
    }

    // --- ส่วนที่ 6: Class เสริม (ต้องอยู่นอก Class หลักแต่อยู่ใน Namespace เดียวกัน) ---
    public class StatusDetail
    {
        public string? name { get; set; }
        public string? color { get; set; }
    }

    public class UserCreateDetail
    {
        public string? id { get; set; }
        public string? name { get; set; }
        public string? section { get; set; }
    }

    public class TimeDetail
    {
        public object? create { get; set; }
    }
}