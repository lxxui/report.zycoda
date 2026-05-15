using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace report_zycoda.Models
{
    // Class หลักสำหรับรับข้อมูลใบแจ้งงาน
    public class MaintenanceApiModels
    {
        // --- ส่วนที่ 1 & 2: รวมร่าง Property (รองรับทั้งระบบเก่าและ JSON Farmhouse) ---
        public string? v { get; set; }
        public string? job_no { get; set; }

        public string? id { get; set; }           // จาก farmhouse
        public string? id_h { get; set; }         // จากระบบเดิม

        public string? detail { get; set; }       // จาก farmhouse
        public string? detail_h { get; set; }     // จากระบบเดิม

        public string? section { get; set; }      // หน่วยงานของ "เครื่องจักร/ใบงาน"
        public string? sectioncreate { get; set; } // หน่วยงานที่สร้าง (ระบบเดิม)

        public string? status { get; set; }       // เก็บ ID สถานะ (เช่น 20, 30, 40)
        public string? statustext { get; set; }   // เก็บชื่อสถานะ (เช่น Create, Confirm)

        public string? timecreate { get; set; }
        public string? fl { get; set; }
        public string? fldetail { get; set; }
        public double? downtime { get; set; }
        public string? jobtype { get; set; }

        // --- ส่วนที่ 3: แก้ไขจาก string เป็น Object (แก้ Error CS1061) ---
        // เปลี่ยนจาก public string? usercreate เป็น UserCreateDetail
        public object? usercreate { get; set; } // เปลี่ยนจาก UserCreateDetail เป็น object
        public StatusDetail? st { get; set; }
        public TimeDetail? time { get; set; }

        // --- ส่วนที่ 4: ฟิลด์สำหรับ Logic ใน Controller ---
        public string? acceptby { get; set; }
        public bool IsAccept { get; set; }
        public bool IsAssign { get; set; }

        public string? timestart { get; set; }
        public string? timeend { get; set; }
        public string? timestartrepair { get; set; }
        public string? timeendrepair { get; set; }
        public string? timedelay { get; set; }

        public string? timeclose { get; set; }

        public string? timeassign { get; set; }

        public string? useraccept { get; set; }

        public string? userfinish { get; set; }

        public string? timefinish { get; set; }

        public List<string>? uassign { get; set; }


        public string? timeaccept { get; set; }

        public string? workby { get; set; }

        public string? createby { get; set; }

        public string? assign_to { get; set; }

        public string? view { get; set; }

        public string? priority { get; set; }






        // --- ส่วนที่ 5: Logic สีสถานะ (ปรับให้เช็ค statustext เป็นหลัก) ---
        public string StatusCssClass
        {
            get
            {
                // ตัดช่องว่างและเช็คค่าว่าง
                string currentStatus = (statustext ?? status ?? "").Trim();

                return currentStatus switch
                {
                    "Create" or "20" => "bg-danger text-white",
                    "Pending" => "bg-info text-white",
                    "Accept" or "30" => "bg-warning text-dark",
                    "Assigned" => "bg-primary text-white",
                    "Finish" or "Confirm" or "40" => "bg-success text-white",
                    _ => "bg-secondary text-white"
                };
            }
        }

        public string UserDisplayName
        {
            get
            {
                if (usercreate == null) return "-";
                if (usercreate is string s) return s;
                try
                {
                    var jo = Newtonsoft.Json.Linq.JObject.FromObject(usercreate);
                    return jo["name"]?.ToString() ?? "-";
                }
                catch { return "-"; }
            }
        }

        public string? SectionName { get; internal set; }
    }


    // --- ส่วนที่ 6: Class เสริมสำหรับ Nested Objects ---

    public class UserCreateDetail
    {
        public string? id { get; set; }
        public string? name { get; set; }
        public string? section { get; set; } // แผนกของคนแจ้ง (เช่น 322010)
    }

    public class StatusDetail
    {
        public int state { get; set; }
        public string? name { get; set; }
        public string? color { get; set; }
    }

    public class TimeDetail
    {
        // ใช้ object? หรือ string? เพราะ JSON บางทีส่งมาหลายรูปแบบ
        public object? create { get; set; }
        public object? start { get; set; }
        public object? end { get; set; }
    }

    public class ReportSummary
    {
        public string? SectionName { get; set; }
        public int CarriedOver { get; set; }
        public int OpenedToday { get; set; }
        public int Finished { get; set; }
        public int RejectedToday { get; set; }
        public int Repair { get; set; }
        public int AcceptJob { get; set; }
        public int WaitPart { get; set; }

        public string? rptType { get; set; }
    }



}