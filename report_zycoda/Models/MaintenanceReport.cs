using Microsoft.AspNetCore.Mvc;


namespace report_zycoda.Models
{
    public class MaintenanceReport
    {

        // header
        public string? id_h { get; set; }
        public string? detail_h { get; set; }
        public int downtime_h { get; set; }

        //detail
        public string? id { get; set; }
        public string? detail { get; set; }
        public string? fl { get; set; }
        public int? downtime { get; set; }
        public int? timerepair { get; set; }
        public string? tag_abnormal { get; set; }
        public string? jobtype { get; set; }
        public string? section { get; set; }
        public string? statustexttext { get; set; }
        public string? fldetail { get; set; }
        public string? statustext { get; set; }
        public string? usercreate { get; set; }
        public string? solution { get; set; }
        public string? problem { get; set; }
        public string? causes { get; set; }
        public string? ordertype { get; set; }
        public string? bdfac { get; set; }
    }
}
