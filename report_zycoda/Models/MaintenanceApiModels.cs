using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using Newtonsoft.Json; // 👈 มั่นใจว่ามี using ตัวนี้

namespace report_zycoda.Models
{
    public class MaintenanceApiModels
    {
        [JsonProperty("v")] public string? v { get; set; }
        [JsonProperty("id")] public string? id { get; set; }
        [JsonProperty("Mn")] public string? Mn { get; set; }
        [JsonProperty("Mo")] public string? Mo { get; set; }
        [JsonProperty("refId")] public string? refId { get; set; }
        [JsonProperty("reforder")] public string? reforder { get; set; }
        [JsonProperty("id_h")] public string? id_h { get; set; }

        [JsonProperty("detail")] public string? detail { get; set; }
        [JsonProperty("detail_h")] public string? detail_h { get; set; }

        [JsonProperty("section")] public string? section { get; set; }
        [JsonProperty("sectioncreate")] public string? sectioncreate { get; set; }

        [JsonProperty("status")] public string? status { get; set; }
        [JsonProperty("statustext")] public string? statustext { get; set; }

        [JsonProperty("timecreate")] public string? timecreate { get; set; }
        [JsonProperty("fl")] public string? fl { get; set; }
        [JsonProperty("fldetail")] public string? fldetail { get; set; }
        [JsonProperty("downtime")] public double? downtime { get; set; }
        [JsonProperty("jobtype")] public string? jobtype { get; set; }

        // 🔥 ปรับเป็นเดี่ยวๆ หรือดักจับรองรับ String/Object 
        // หาก API บางทีส่งเป็น string บางทีส่งเป็น JSON Object ให้ปล่อยเป็น JToken หรือแก้พฤติกรรมใน Property
        [JsonProperty("usercreate")]
        public object? usercreate { get; set; }

        [JsonProperty("st")] public StatusDetail? st { get; set; }
        [JsonProperty("time")] public TimeDetail? time { get; set; }

        [JsonProperty("acceptby")] public string? acceptby { get; set; }
        [JsonProperty("IsAccept")] public bool IsAccept { get; set; }
        [JsonProperty("IsAssign")] public bool IsAssign { get; set; }

        [JsonProperty("timestart")] public string? timestart { get; set; }
        [JsonProperty("timeend")] public string? timeend { get; set; }
        [JsonProperty("timestartrepair")] public string? timestartrepair { get; set; }
        [JsonProperty("timeendrepair")] public string? timeendrepair { get; set; }
        [JsonProperty("timedelay")] public string? timedelay { get; set; }
        [JsonProperty("timeclose")] public string? timeclose { get; set; }
        [JsonProperty("timeassign")] public string? timeassign { get; set; }
        [JsonProperty("useraccept")] public string? useraccept { get; set; }
        [JsonProperty("userfinish")] public string? userfinish { get; set; }
        [JsonProperty("timefinish")] public string? timefinish { get; set; }
        [JsonProperty("uassign")] public List<string>? uassign { get; set; }
        [JsonProperty("timeaccept")] public string? timeaccept { get; set; }
        [JsonProperty("workby")] public string? workby { get; set; }
        [JsonProperty("createby")] public string? createby { get; set; }
        [JsonProperty("assign_to")] public string? assign_to { get; set; }
        [JsonProperty("view")] public string? view { get; set; }
        [JsonProperty("priority")] public string? priority { get; set; }

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
                    // ถ้าฝั่งนู้นส่งมาเป็น JObject หรือดิกชันนารี
                    var jo = Newtonsoft.Json.Linq.JObject.FromObject(usercreate);
                    return jo["name"]?.ToString() ?? "-";
                }
                catch { return "-"; }
            }
        }

        public string? SectionName { get; internal set; }
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