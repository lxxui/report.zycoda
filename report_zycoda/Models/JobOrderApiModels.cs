using System;

namespace report_zycoda.Models
{
    public class JobOrderApiModels
    {
        public int id { get; set; }
        public string? refid { get; set; }
        public string? reforder { get; set; }
        public string? MN { get; set; }
        public string? MO { get; set; }
        public string? detail { get; set; }
        public string? safety { get; set; }
        public string? code { get; set; }
        public string? fl { get; set; }
        public string? downtime { get; set; }
        public string? difftime { get; set; }

        // กลุ่มฟิลด์วันที่ ปรับเป็น DateTime? เพื่อรองรับค่า NULL จากฐานข้อมูล
        public DateTime? timerepair { get; set; }
        public DateTime? timerepair_ot { get; set; }
        public string? tag_abnormal { get; set; }
        public string? tag { get; set; }
        public string? tags { get; set; }
        public string? jobtype { get; set; }
        public string? section { get; set; }
        public string? productionstop { get; set; }
        public string? planner { get; set; }
        public string? statustext { get; set; }
        public string? statussection { get; set; }
        public string? opt_5s { get; set; }
        public string? opt_5s_comment { get; set; }
        public string? opt_protect { get; set; }
        public string? opt_protect_comment { get; set; }
        public string? rates { get; set; }

        // กลุ่มบันทึกเวลาของระบบ Zycoda
        public DateTime? timecreate { get; set; }
        public DateTime? timeassign { get; set; }
        public DateTime? timestart { get; set; }
        public DateTime? timeend { get; set; }
        public DateTime? timefinish { get; set; }
        public DateTime? timeaccept { get; set; }
        public DateTime? timestartrepair { get; set; }
        public DateTime? timeendrepair { get; set; }
        public DateTime? timeclose { get; set; }

        public string? timerunning { get; set; }
        public string? timeday { get; set; }
        public string? fldetail { get; set; }
        public string? flrank { get; set; }
        public string? flpngrp { get; set; }
        public string? priority { get; set; }
        public string? status { get; set; }
        public string? usercreate { get; set; }
        public string? sectioncreate { get; set; }
        public string? useraccept { get; set; }
        public string? userfinish { get; set; }
        public string? comment { get; set; }
        public string? solution { get; set; }
        public string? problem { get; set; }
        public string? causes { get; set; }
        public string? preventive { get; set; }
        public string? ordertype { get; set; }
        public string? submiss { get; set; }
        public string? bdfac { get; set; }
        public string? id_db { get; set; }
    }
}