using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace report_zycoda.Models
{
    public class MaintenanceApiModels
    {
        [JsonProperty("v")] public string? v { get; set; }

        [Column("id")]
        [JsonProperty("id")] public string? id { get; set; }
        [JsonProperty("id_db")] public string? id_db { get; set; } // 🔧 เพิ่มเพื่อให้ตรงกับโครงสร้างหลักฐานข้อมูล
        [JsonProperty("Mn")] public string? Mn { get; set; }
        [JsonProperty("Mo")] public string? Mo { get; set; }
        [JsonProperty("refId")] public string? refId { get; set; }
        [JsonProperty("reforder")] public string? reforder { get; set; }
        [JsonProperty("id_h")] public string? id_h { get; set; }

        // --- กลุ่มข้อมูลรายละเอียดและประเภทหน้างาน ---
        [JsonProperty("detail")] public string? detail { get; set; }
        [JsonProperty("detail_h")] public string? detail_h { get; set; }
        [JsonProperty("safety")] public string? safety { get; set; } // 🔧 เพิ่มเติม
        [JsonProperty("code")] public string? code { get; set; } // 🔧 เพิ่มเติม
        [JsonProperty("fl")] public string? fl { get; set; }
        [JsonProperty("fldetail")] public string? fldetail { get; set; }
        [JsonProperty("flrank")] public string? flrank { get; set; } // 🔧 เพิ่มเติม
        [JsonProperty("flpngrp")] public string? flpngrp { get; set; } // 🔧 เพิ่มเติม
        [JsonProperty("priority")] public string? priority { get; set; }
        [JsonProperty("jobtype")] public string? jobtype { get; set; }
        [JsonProperty("ordertype")] public string? ordertype { get; set; } // 🔧 เพิ่มเติม

        // --- กลุ่มข้อมูลหน่วยงานและผู้ดูแล ---
        [JsonProperty("section")] public string? section { get; set; }
        [JsonProperty("sectioncreate")] public string? sectioncreate { get; set; }
        [JsonProperty("productionstop")] public string? productionstop { get; set; } // 🔧 เพิ่มเติม
        [JsonProperty("planner")] public string? planner { get; set; } // 🔧 เพิ่มเติม
        [JsonProperty("statussection")] public string? statussection { get; set; } // 🔧 เพิ่มเติม
        [JsonProperty("submiss")] public string? submiss { get; set; } // 🔧 เพิ่มเติม
        [JsonProperty("bdfac")] public string? bdfac { get; set; } // 🔧 เพิ่มเติม

        // --- กลุ่มข้อมูลสถานะการดำเนินงาน ---
        [JsonProperty("status")] public string? status { get; set; }
        [JsonProperty("statustext")] public string? statustext { get; set; }

        // --- กลุ่มฟิลด์การประเมิน / ตรวจรับงาน (5S และ ความปลอดภัย) ---
        [JsonProperty("opt_5s")] public string? opt_5s { get; set; } // 🔧 เพิ่มเติม
        [JsonProperty("opt_5s_comment")] public string? opt_5s_comment { get; set; } // 🔧 เพิ่มเติม
        [JsonProperty("opt_protect")] public string? opt_protect { get; set; } // 🔧 เพิ่มเติม
        [JsonProperty("opt_protect_comment")] public string? opt_protect_comment { get; set; } // 🔧 เพิ่มเติม
        [JsonProperty("rates")] public string? rates { get; set; } // 🔧 เพิ่มเติม

        // --- กลุ่มข้อมูลบันทึกเวลาต่างๆ ---
        [JsonProperty("timecreate")] public string? timecreate { get; set; }
        [JsonProperty("timeassign")] public string? timeassign { get; set; }
        [JsonProperty("timestart")] public string? timestart { get; set; }
        [JsonProperty("timeend")] public string? timeend { get; set; }
        [JsonProperty("timefinish")] public string? timefinish { get; set; }
        [JsonProperty("timeaccept")] public string? timeaccept { get; set; }
        [JsonProperty("timestartrepair")] public string? timestartrepair { get; set; }
        [JsonProperty("timeendrepair")] public string? timeendrepair { get; set; }
        [JsonProperty("timeclose")] public string? timeclose { get; set; }

        [JsonProperty("downtime")] public double? downtime { get; set; }
        [JsonProperty("difftime")] public string? difftime { get; set; } // 🔧 เพิ่มเติม
        [JsonProperty("timerepair")] public string? timerepair { get; set; } // 🔧 เพิ่มเติม
        [JsonProperty("timerepair_ot")] public string? timerepair_ot { get; set; } // 🔧 เพิ่มเติม
        [JsonProperty("tag_abnormal")] public string? tag_abnormal { get; set; } // 🔧 เพิ่มเติม
        [JsonProperty("tag")] public string? tag { get; set; } // 🔧 เพิ่มเติม
        [JsonProperty("tags")] public string? tags { get; set; } // 🔧 เพิ่มเติม
        [JsonProperty("timerunning")] public string? timerunning { get; set; } // 🔧 เพิ่มเติม
        [JsonProperty("timeday")] public string? timeday { get; set; } // 🔧 เพิ่มเติม
        [JsonProperty("timedelay")] public string? timedelay { get; set; }

        // --- กลุ่มข้อมูลผู้รับผิดชอบและบันทึกผู้ปฏิบัติงาน ---
        [JsonProperty("usercreate")] public object? usercreate { get; set; }
        [JsonProperty("useraccept")] public string? useraccept { get; set; }
        [JsonProperty("userfinish")] public string? userfinish { get; set; }
        [JsonProperty("acceptby")] public string? acceptby { get; set; }
        [JsonProperty("workby")] public string? workby { get; set; }
        [JsonProperty("createby")] public string? createby { get; set; }
        [JsonProperty("assign_to")] public string? assign_to { get; set; }
        [JsonProperty("IsAccept")] public bool IsAccept { get; set; }
        [JsonProperty("IsAssign")] public bool IsAssign { get; set; }

        // --- กลุ่มบันทึกข้อมูลหน้างาน / สรุปการซ่อมแซม ---
        [JsonProperty("comment")] public string? comment { get; set; } // 🔧 เพิ่มเติม
        [JsonProperty("problem")] public string? problem { get; set; } // 🔧 เพิ่มเติม
        [JsonProperty("causes")] public string? causes { get; set; } // 🔧 เพิ่มเติม
        [JsonProperty("solution")] public string? solution { get; set; } // 🔧 เพิ่มเติม
        [JsonProperty("preventive")] public string? preventive { get; set; } // 🔧 เพิ่มเติม

        // --- กลุ่มข้อมูลโครงสร้างการอนุมัติและคลาสย่อย ---
        [JsonProperty("st")] public StatusDetail? st { get; set; }
        [JsonProperty("time")] public TimeDetail? time { get; set; }
        [JsonProperty("view")] public string? view { get; set; }

        // 👥 รายชื่อผู้รับผิดชอบงาน
        [JsonProperty("uassign")] public List<string>? uassign { get; set; }

        // 🛡️ ข้อมูลการ Approve
        [JsonProperty("userApprove")] public List<string>? userApprove { get; set; }
        [JsonProperty("timeApprove")] public string? timeApprove { get; set; }
        [JsonProperty("Approval")] public List<object>? Approval { get; set; }
        [JsonProperty("ApproveCreate")] public List<ApproveCreateDetail>? ApproveCreate { get; set; }

        // --- Logic Helper Properties ---
        public string StatusCssClass
        {
            get
            {
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
                    var jo = JObject.FromObject(usercreate);
                    return jo["name"]?.ToString() ?? "-";
                }
                catch { return "-"; }
            }
        }

        public string? SectionName { get; internal set; }

        // --- Database Mapping Helpers ---

        private DateTime? ParseDateTime(string? dateStr)
        {
            if (string.IsNullOrWhiteSpace(dateStr)) return null;
            if (DateTime.TryParse(dateStr, out DateTime result)) return result;
            return null;
        }

        /// <summary>
        /// ดึงรายชื่อผู้รับผิดชอบงานเพื่อเตรียม Insert ลงตาราง Job_assign
        /// </summary>
        public List<JobAssignModel> MapToJobAssigns()
        {
            var list = new List<JobAssignModel>();
            if (uassign == null || string.IsNullOrEmpty(id)) return list;

            foreach (var user in uassign)
            {
                if (!string.IsNullOrWhiteSpace(user))
                {
                    list.Add(new JobAssignModel { JobId = this.id, UserAssign = user.Trim() });
                }
            }
            return list;
        }

        /// <summary>
        /// รวบรวมข้อมูลการ Approve ทุกประเภท (ApproveCreate, userApprove) เพื่อบันทึกลงตาราง Job_approve
        /// </summary>
        public List<JobApproveModel> MapToJobApproves()
        {
            var list = new List<JobApproveModel>();
            if (string.IsNullOrEmpty(id)) return list;

            // 1. ดักจับจาก ApproveCreate (ตอนเปิดงาน)
            if (ApproveCreate != null)
            {
                foreach (var appCreate in ApproveCreate)
                {
                    if (!string.IsNullOrWhiteSpace(appCreate.name))
                    {
                        list.Add(new JobApproveModel
                        {
                            JobId = this.id,
                            ApproveType = "Create", // ระบุสเตตัสตอนสร้างงาน
                            UserApprove = appCreate.name.Trim(),
                            ApproveDatetime = ParseDateTime(appCreate.datetime)
                        });
                    }
                }
            }

            // 2. ดักจับจาก userApprove (ตอนปิดงาน/ขั้นตอนทั่วไป)
            if (userApprove != null)
            {
                foreach (var user in userApprove)
                {
                    if (!string.IsNullOrWhiteSpace(user))
                    {
                        list.Add(new JobApproveModel
                        {
                            JobId = this.id,
                            ApproveType = "Final", // ระบุเป็นขั้นตอนสุดท้าย
                            UserApprove = user.Trim(),
                            ApproveDatetime = ParseDateTime(this.timeApprove)
                        });
                    }
                }
            }
            return list;
        }
    }

    // --- คลาสย่อยสำหรับ Mapping และโครงสร้างภายใน ---

    public class JobAssignModel
    {
        public string JobId { get; set; } = null!;
        public string UserAssign { get; set; } = null!;
    }

    public class JobApproveModel
    {
        public string JobId { get; set; } = null!;
        public string ApproveType { get; set; } = null!; // แยกแยะ 'Create' หรือ 'Final'
        public string UserApprove { get; set; } = null!;
        public DateTime? ApproveDatetime { get; set; }
    }

    public class ApproveCreateDetail
    {
        [JsonProperty("name")] public string? name { get; set; }
        [JsonProperty("datetime")] public string? datetime { get; set; }
    }

    public class UserCreateDetail
    {
        [JsonProperty("id")] public string? id { get; set; }
        [JsonProperty("name")] public string? name { get; set; }
        [JsonProperty("section")] public string? section { get; set; }
    }

    public class StatusDetail
    {
        [JsonProperty("state")] public int state { get; set; }
        [JsonProperty("name")] public string? name { get; set; }
        [JsonProperty("color")] public string? color { get; set; }
    }

    public class TimeDetail
    {
        [JsonProperty("create")] public object? create { get; set; }
        [JsonProperty("start")] public object? start { get; set; }
        [JsonProperty("end")] public object? end { get; set; }
    }

    public class ReportSummary
    {
        public string? SectionName { get; set; }
        public int CarriedOver { get; set; }
        public int OpenedToday { get; set; }
        public int AcceptJob { get; set; }
        public int Repair { get; set; }
        public int Finished { get; set; }
        public int RejectedToday { get; set; }
        public int WaitPart { get; set; }
        public int NotApproved { get; set; }
        public int NotAccepted { get; set; }
    }
}